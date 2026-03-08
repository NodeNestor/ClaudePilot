using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ClaudePilot
{
    /// <summary>
    /// Optional MCP (Model Context Protocol) server that exposes all ClaudePilot tools
    /// over HTTP so external agents (Claude Code, custom scripts, etc.) can control KSP.
    ///
    /// Uses JSON-RPC over HTTP with SSE transport per the MCP spec.
    /// Runs on a configurable port (default 8745).
    /// </summary>
    public class McpServer
    {
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;
        private ToolExecutor toolExecutor;
        private int port;

        // Queue for executing tool calls on Unity's main thread
        private readonly Queue<McpRequest> pendingRequests = new Queue<McpRequest>();
        private readonly Dictionary<string, McpRequest> completedRequests = new Dictionary<string, McpRequest>();

        public bool IsRunning => running;
        public int Port => port;

        private class McpRequest
        {
            public string id;
            public string method;
            public string toolName;
            public Dictionary<string, string> parameters;
            public string result;
            public bool completed;
            public ManualResetEvent waitHandle = new ManualResetEvent(false);
        }

        public McpServer(ToolExecutor toolExecutor, int port)
        {
            this.toolExecutor = toolExecutor;
            this.port = port;
        }

        public void Start()
        {
            if (running) return;

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Prefixes.Add("http://localhost:" + port + "/");
                listener.Start();
                running = true;

                listenerThread = new Thread(ListenLoop);
                listenerThread.IsBackground = true;
                listenerThread.Start();

                Debug.Log("[ClaudePilot] MCP server started on port " + port);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ClaudePilot] MCP server failed to start: " + ex.Message);
                running = false;
            }
        }

        public void Stop()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch { }

            Debug.Log("[ClaudePilot] MCP server stopped.");
        }

        /// <summary>
        /// Must be called from Unity's main thread (Update loop) to process tool calls.
        /// </summary>
        public void ProcessMainThread()
        {
            lock (pendingRequests)
            {
                while (pendingRequests.Count > 0)
                {
                    var req = pendingRequests.Dequeue();
                    try
                    {
                        req.result = toolExecutor.ExecuteTool(req.toolName, req.parameters);
                    }
                    catch (Exception ex)
                    {
                        req.result = "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
                    }
                    req.completed = true;
                    req.waitHandle.Set();
                }
            }
        }

        private void ListenLoop()
        {
            while (running)
            {
                try
                {
                    var context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (running)
                        Debug.LogError("[ClaudePilot] MCP listener error: " + ex.Message);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // CORS headers for browser-based clients
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string path = request.Url.AbsolutePath.TrimEnd('/');

                if (request.HttpMethod == "GET" && (path == "" || path == "/"))
                {
                    // Health check / info endpoint
                    SendJson(response, 200, "{\"name\":\"ClaudePilot\",\"version\":\"1.0\",\"status\":\"running\","
                        + "\"protocol\":\"mcp\",\"tools\":" + ToolDefinitions.GetOpenAIToolsJson() + "}");
                    return;
                }

                if (request.HttpMethod == "GET" && path == "/mcp")
                {
                    // SSE endpoint for MCP — send server info then keep alive
                    HandleSseEndpoint(context);
                    return;
                }

                if (request.HttpMethod == "POST" && path == "/mcp")
                {
                    // JSON-RPC endpoint for MCP messages
                    HandleMcpMessage(context);
                    return;
                }

                // Legacy REST endpoints for simple integration
                if (request.HttpMethod == "GET" && path == "/tools")
                {
                    SendJson(response, 200, ToolDefinitions.GetOpenAIToolsJson());
                    return;
                }

                if (request.HttpMethod == "POST" && path == "/tool")
                {
                    HandleToolCall(context);
                    return;
                }

                SendJson(response, 404, "{\"error\":\"Not found. Endpoints: GET /, GET /tools, POST /tool, GET|POST /mcp\"}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ClaudePilot] MCP request error: " + ex.Message);
                try
                {
                    SendJson(context.Response, 500, "{\"error\":\"" + EscapeJson(ex.Message) + "\"}");
                }
                catch { }
            }
        }

        // ─── MCP Protocol Handlers ───

        private void HandleSseEndpoint(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            try
            {
                using (var writer = new StreamWriter(response.OutputStream, Encoding.UTF8))
                {
                    // Send endpoint info
                    string sessionId = Guid.NewGuid().ToString("N").Substring(0, 16);
                    writer.WriteLine("event: endpoint");
                    writer.WriteLine("data: /mcp?session=" + sessionId);
                    writer.WriteLine();
                    writer.Flush();

                    // Keep alive until client disconnects
                    while (running)
                    {
                        Thread.Sleep(15000);
                        try
                        {
                            writer.WriteLine(":keepalive");
                            writer.Flush();
                        }
                        catch
                        {
                            break; // Client disconnected
                        }
                    }
                }
            }
            catch { }
        }

        private void HandleMcpMessage(HttpListenerContext context)
        {
            string body = ReadBody(context.Request);
            var response = context.Response;

            // Parse JSON-RPC request
            string method = MiniJson.ExtractString(body, "method") ?? "";
            string id = MiniJson.ExtractString(body, "id") ?? "1";

            switch (method)
            {
                case "initialize":
                    SendJsonRpc(response, id,
                        "{\"protocolVersion\":\"2025-03-26\","
                        + "\"capabilities\":{\"tools\":{}},"
                        + "\"serverInfo\":{\"name\":\"ClaudePilot\",\"version\":\"1.0\"}}");
                    break;

                case "initialized":
                    SendJsonRpc(response, id, "{}");
                    break;

                case "tools/list":
                    SendJsonRpc(response, id, "{\"tools\":" + BuildMcpToolsList() + "}");
                    break;

                case "tools/call":
                    HandleMcpToolCall(context, body, id);
                    break;

                case "ping":
                    SendJsonRpc(response, id, "{}");
                    break;

                default:
                    SendJsonRpcError(response, id, -32601, "Method not found: " + method);
                    break;
            }
        }

        private void HandleMcpToolCall(HttpListenerContext context, string body, string id)
        {
            // Extract params.name and params.arguments from the JSON-RPC request
            // Body format: {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"...","arguments":{...}}}
            int paramsIdx = body.IndexOf("\"params\"");
            if (paramsIdx < 0)
            {
                SendJsonRpcError(context.Response, id, -32602, "Missing params");
                return;
            }

            // Find the params object
            int paramsStart = body.IndexOf('{', paramsIdx + 8);
            if (paramsStart < 0)
            {
                SendJsonRpcError(context.Response, id, -32602, "Invalid params");
                return;
            }

            string paramsSection = ExtractObject(body, paramsStart);
            string toolName = MiniJson.ExtractString(paramsSection, "name") ?? "";

            // Extract arguments object
            string argsJson = "{}";
            int argsIdx = paramsSection.IndexOf("\"arguments\"");
            if (argsIdx >= 0)
            {
                int argsStart = paramsSection.IndexOf('{', argsIdx);
                if (argsStart >= 0)
                    argsJson = ExtractObject(paramsSection, argsStart);
            }

            var parameters = MiniJson.ParseFlat(argsJson);
            string result = ExecuteOnMainThread(toolName, parameters);

            // MCP tools/call response format
            SendJsonRpc(context.Response, id,
                "{\"content\":[{\"type\":\"text\",\"text\":" + JsonStringify(result) + "}]}");
        }

        // ─── Legacy REST Handler ───

        private void HandleToolCall(HttpListenerContext context)
        {
            string body = ReadBody(context.Request);
            string toolName = MiniJson.ExtractString(body, "name") ?? "";
            string argsJson = "{}";

            int argsIdx = body.IndexOf("\"arguments\"");
            if (argsIdx >= 0)
            {
                int argsStart = body.IndexOf('{', argsIdx);
                if (argsStart >= 0)
                    argsJson = ExtractObject(body, argsStart);
            }

            // Fallback: try "parameters" key
            if (argsJson == "{}")
            {
                int pIdx = body.IndexOf("\"parameters\"");
                if (pIdx >= 0)
                {
                    int pStart = body.IndexOf('{', pIdx);
                    if (pStart >= 0)
                        argsJson = ExtractObject(body, pStart);
                }
            }

            var parameters = MiniJson.ParseFlat(argsJson);
            string result = ExecuteOnMainThread(toolName, parameters);

            SendJson(context.Response, 200, result);
        }

        // ─── Main Thread Execution ───

        private string ExecuteOnMainThread(string toolName, Dictionary<string, string> parameters)
        {
            var req = new McpRequest
            {
                id = Guid.NewGuid().ToString("N"),
                toolName = toolName,
                parameters = parameters
            };

            lock (pendingRequests)
            {
                pendingRequests.Enqueue(req);
            }

            // Wait for Unity main thread to process it (up to 30s)
            if (!req.waitHandle.WaitOne(30000))
            {
                return "{\"error\":\"Timeout waiting for Unity main thread\"}";
            }

            return req.result ?? "{\"error\":\"No result\"}";
        }

        // ─── Helpers ───

        private string BuildMcpToolsList()
        {
            // Convert OpenAI tool format to MCP tool format
            // OpenAI: [{"type":"function","function":{"name":"...","description":"...","parameters":{...}}}]
            // MCP: [{"name":"...","description":"...","inputSchema":{...}}]
            string openAiTools = ToolDefinitions.GetOpenAIToolsJson();

            // Quick transform: extract function objects and rename parameters→inputSchema
            var sb = new StringBuilder();
            sb.Append("[");

            // Parse the array
            int pos = openAiTools.IndexOf('[') + 1;
            bool first = true;
            while (pos < openAiTools.Length)
            {
                // Find next function object
                int funcIdx = openAiTools.IndexOf("\"function\"", pos);
                if (funcIdx < 0) break;

                int funcStart = openAiTools.IndexOf('{', funcIdx + 10);
                if (funcStart < 0) break;

                string funcObj = ExtractObject(openAiTools, funcStart);

                if (!first) sb.Append(",");
                first = false;

                // Replace "parameters" with "inputSchema"
                sb.Append(funcObj.Replace("\"parameters\":", "\"inputSchema\":"));

                pos = funcStart + funcObj.Length;
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static string ExtractObject(string json, int start)
        {
            int depth = 1;
            int end = start + 1;
            bool inStr = false;
            while (end < json.Length && depth > 0)
            {
                char c = json[end];
                if (inStr)
                {
                    if (c == '\\') end++;
                    else if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                end++;
            }
            return json.Substring(start, end - start);
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        private static void SendJson(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private static void SendJsonRpc(HttpListenerResponse response, string id, string result)
        {
            string json = "{\"jsonrpc\":\"2.0\",\"id\":" + JsonStringify(id) + ",\"result\":" + result + "}";
            SendJson(response, 200, json);
        }

        private static void SendJsonRpcError(HttpListenerResponse response, string id, int code, string message)
        {
            string json = "{\"jsonrpc\":\"2.0\",\"id\":" + JsonStringify(id)
                + ",\"error\":{\"code\":" + code + ",\"message\":" + JsonStringify(message) + "}}";
            SendJson(response, 200, json);
        }

        private static string JsonStringify(string s)
        {
            if (s == null) return "null";
            return "\"" + EscapeJson(s) + "\"";
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
