using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ClaudePilot
{
    public class ClaudeClient
    {
        private readonly ToolExecutor toolExecutor;

        // Static so conversation history survives scene switches
        private static readonly List<MessageEntry> messages = new List<MessageEntry>();

        // Queue for marshalling callbacks to the main Unity thread
        private readonly Queue<Action> mainThreadQueue = new Queue<Action>();

        public ChatWindow chatWindow;
        private bool isBusy = false;
        private bool stopRequested = false;
        public bool IsBusy => isBusy;

        // Track pending continuation for scene changes
        private static bool pendingContinue = false;
        private static int pendingLoop = 0;

        // Tools that change scenes and will kill the coroutine
        private static readonly HashSet<string> sceneChangingTools = new HashSet<string>
        {
            "quick_launch",
            "launch_vessel",
            "go_to_vab",
            "go_to_sph",
            "go_to_tracking_station",
            "go_to_space_center",
            "load_craft_in_editor",
            "recover_vessel"
        };

        // Tools that should block until MechJeb completes
        private static readonly HashSet<string> blockingTools = new HashSet<string>
        {
            "launch_to_orbit_wait",
            "execute_next_node_wait",
            "land_wait",
            "circularize_wait",
            "start_rendezvous_wait",
            "start_docking_wait"
        };

        public ClaudeClient(ToolExecutor toolExecutor)
        {
            this.toolExecutor = toolExecutor;
            Debug.Log("[ClaudePilot] ClaudeClient created (OpenAI format mode).");
        }

        public void ProcessMainThreadQueue()
        {
            lock (mainThreadQueue)
            {
                while (mainThreadQueue.Count > 0)
                {
                    var action = mainThreadQueue.Dequeue();
                    try { action(); }
                    catch (Exception ex) { Debug.LogError("[ClaudePilot] Callback error: " + ex.Message); }
                }
            }
        }

        public void Stop()
        {
            stopRequested = true;
            pendingContinue = false;
            Debug.Log("[ClaudePilot] Stop requested.");
        }

        // Called when scene changes - check if we need to resume
        public void OnSceneChange()
        {
            if (pendingContinue && messages.Count > 0)
            {
                Debug.Log("[ClaudePilot] Scene changed, resuming conversation loop at step " + pendingLoop);
                pendingContinue = false;
                isBusy = true;
                stopRequested = false;
                if (ClaudePilotAddon.Instance != null)
                    ClaudePilotAddon.Instance.StartCoroutine(ConversationLoop(pendingLoop));
            }
        }

        public static bool HasPendingAction => pendingContinue;

        public void SendMessage(string userMessage)
        {
            if (isBusy) return;
            isBusy = true;
            stopRequested = false;

            if (chatWindow != null)
                chatWindow.SetStatus("Thinking...");

            messages.Add(new MessageEntry { role = "user", content = userMessage });
            TrimHistory();

            if (ClaudePilotAddon.Instance != null)
                ClaudePilotAddon.Instance.StartCoroutine(ConversationLoop(0));
            else
                Debug.LogError("[ClaudePilot] No addon instance to run coroutine!");
        }

        private IEnumerator ConversationLoop(int startLoop)
        {
            const int maxToolLoops = 100;

            string lastScene = HighLogic.LoadedScene.ToString();

            for (int loop = startLoop; loop < maxToolLoops; loop++)
            {
                if (stopRequested)
                {
                    Debug.Log("[ClaudePilot] Stopping at user request.");
                    if (chatWindow != null)
                    {
                        chatWindow.AddMessage("system", "[Stopped by user]");
                        chatWindow.SetStatus("Ready");
                    }
                    isBusy = false;
                    stopRequested = false;
                    yield break;
                }

                string requestJson = BuildOpenAIRequestJson();
                string baseUrl = string.IsNullOrEmpty(Settings.apiBaseUrl) ? "http://127.0.0.1:9212" : Settings.apiBaseUrl;
                // Convert /v1/messages to /v1/chat/completions if needed
                string apiUrl = baseUrl;
                if (apiUrl.EndsWith("/v1/messages"))
                    apiUrl = apiUrl.Substring(0, apiUrl.Length - "/v1/messages".Length) + "/v1/chat/completions";
                else if (!apiUrl.EndsWith("/v1/chat/completions"))
                    apiUrl = apiUrl.TrimEnd('/') + "/v1/chat/completions";

                Debug.Log("[ClaudePilot] Sending OpenAI request (loop " + loop + ") to " + apiUrl + ", length=" + requestJson.Length);

                var request = new UnityWebRequest(apiUrl, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + Settings.apiKey);
                request.timeout = 180;

                if (chatWindow != null)
                    chatWindow.SetStatus(loop == 0 ? "Thinking..." : "Tool loop " + (loop + 1) + "...");

                yield return request.SendWebRequest();

                string responseBody = "";

                if (request.isNetworkError || request.isHttpError)
                {
                    string errBody = (request.downloadHandler != null) ? request.downloadHandler.text : "";
                    string errMsg = "API Error: " + request.error;
                    if (!string.IsNullOrEmpty(errBody))
                        errMsg += " - " + errBody;
                    Debug.LogError("[ClaudePilot] " + errMsg);

                    // Retry once on 400
                    if (request.responseCode == 400 && loop < 1)
                    {
                        Debug.Log("[ClaudePilot] Got 400, retrying in 2s...");
                        yield return new WaitForSeconds(2);

                        requestJson = BuildOpenAIRequestJson();
                        request = new UnityWebRequest(apiUrl, "POST");
                        bodyRaw = Encoding.UTF8.GetBytes(requestJson);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.SetRequestHeader("Authorization", "Bearer " + Settings.apiKey);
                        request.timeout = 180;
                        yield return request.SendWebRequest();

                        if (!request.isNetworkError && !request.isHttpError)
                        {
                            responseBody = request.downloadHandler.text;
                            Debug.Log("[ClaudePilot] Retry succeeded");
                            goto ResponseOK;
                        }

                        errBody = (request.downloadHandler != null) ? request.downloadHandler.text : "";
                        errMsg = "API Error (retry failed): " + request.error + " - " + errBody;
                        Debug.LogError("[ClaudePilot] " + errMsg);
                    }

                    if (chatWindow != null)
                    {
                        chatWindow.streamingMessage = null;
                        chatWindow.AddMessage("error", errMsg);
                        chatWindow.SetStatus("Ready");
                    }
                    isBusy = false;
                    yield break;
                }

                responseBody = request.downloadHandler.text;
                Debug.Log("[ClaudePilot] Response received, length=" + responseBody.Length);

                ResponseOK:
                var parsed = ParseOpenAIResponse(responseBody);

                if (parsed.toolCalls.Count == 0)
                {
                    messages.Add(new MessageEntry { role = "assistant", content = parsed.fullText });
                    TrimHistory();
                    if (chatWindow != null)
                    {
                        chatWindow.streamingMessage = null;
                        chatWindow.AddMessage("assistant", parsed.fullText);
                        chatWindow.SetStatus("Ready");
                    }
                    Debug.Log("[ClaudePilot] Response complete (no tools).");
                    isBusy = false;
                    yield break;
                }

                // Add assistant message with tool calls
                messages.Add(new MessageEntry
                {
                    role = "assistant",
                    content = parsed.fullText,
                    toolCalls = parsed.toolCalls
                });

                if (!string.IsNullOrEmpty(parsed.fullText) && chatWindow != null)
                    chatWindow.streamingMessage = parsed.fullText;

                // Execute tools and collect results
                foreach (var toolCall in parsed.toolCalls)
                {
                    if (stopRequested) break;

                    Debug.Log("[ClaudePilot] Executing tool: " + toolCall.name);
                    if (chatWindow != null)
                        chatWindow.SetStatus("Running: " + toolCall.name + "...");

                    var parameters = MiniJson.ParseFlat(toolCall.arguments);

                    string result;
                    if (blockingTools.Contains(toolCall.name))
                    {
                        var resultList = new List<ToolResultEntry>();
                        yield return ExecuteBlockingTool(toolCall.name, toolCall.id, parameters, resultList);
                        result = resultList.Count > 0 ? resultList[0].content : "{}";
                    }
                    else
                    {
                        try
                        {
                            result = toolExecutor.ExecuteTool(toolCall.name, parameters);
                        }
                        catch (Exception ex)
                        {
                            result = "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
                            Debug.LogError("[ClaudePilot] Tool error: " + ex.Message);
                        }
                        Debug.Log("[ClaudePilot] Tool result: " + (result.Length > 200 ? result.Substring(0, 200) + "..." : result));

                        // Check if this tool will cause a scene change
                        if (sceneChangingTools.Contains(toolCall.name) && !result.Contains("\"error\""))
                        {
                            // Mark for continuation after scene change
                            pendingContinue = true;
                            pendingLoop = loop + 1; // Continue at next loop iteration
                            messages.Add(new ToolResultEntry { toolCallId = toolCall.id, content = result });
                            Debug.Log("[ClaudePilot] Scene-changing tool detected, marking for continuation at loop " + pendingLoop);
                            // Coroutine will die here, new scene will pick it up
                            yield break;
                        }

                        yield return null;
                    }

                    messages.Add(new ToolResultEntry { toolCallId = toolCall.id, content = result });
                }

                TrimHistory();
            }

            if (chatWindow != null)
            {
                chatWindow.streamingMessage = null;
                chatWindow.AddMessage("system", "[Tool loop limit reached]");
                chatWindow.SetStatus("Ready");
            }
            isBusy = false;
        }

        private ParsedResponse ParseOpenAIResponse(string json)
        {
            var result = new ParsedResponse();

            // Find choices array
            int choicesIdx = json.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choicesIdx < 0) return result;

            // Find first choice object
            int choiceStart = json.IndexOf('{', choicesIdx + 9);
            if (choiceStart < 0) return result;

            // Find message object
            int msgIdx = json.IndexOf("\"message\"", choiceStart);
            if (msgIdx < 0) return result;

            int msgStart = json.IndexOf('{', msgIdx);
            if (msgStart < 0) return result;

            // Extract message content
            string content = MiniJson.ExtractString(json.Substring(msgStart), "content");
            if (content != null) result.fullText = content;

            // Find tool_calls array
            int toolCallsIdx = json.IndexOf("\"tool_calls\"", msgStart);
            if (toolCallsIdx < 0) return result;

            int toolCallsArrayStart = json.IndexOf('[', toolCallsIdx);
            if (toolCallsArrayStart < 0) return result;

            // Parse each tool call
            int pos = toolCallsArrayStart + 1;
            while (pos < json.Length)
            {
                while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == ','))
                    pos++;

                if (pos >= json.Length || json[pos] == ']') break;

                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;

                // Find matching }
                int depth = 1;
                int objEnd = objStart + 1;
                bool inStr = false;
                while (objEnd < json.Length && depth > 0)
                {
                    char c = json[objEnd];
                    if (inStr)
                    {
                        if (c == '\\') objEnd++;
                        else if (c == '"') inStr = false;
                    }
                    else
                    {
                        if (c == '"') inStr = true;
                        else if (c == '{') depth++;
                        else if (c == '}') depth--;
                    }
                    objEnd++;
                }

                string toolObj = json.Substring(objStart, objEnd - objStart);

                // Extract id
                string id = MiniJson.ExtractString(toolObj, "id") ?? ("call_" + Guid.NewGuid().ToString("N").Substring(0, 8));

                // Find function object
                int funcIdx = toolObj.IndexOf("\"function\"");
                if (funcIdx >= 0)
                {
                    int funcStart = toolObj.IndexOf('{', funcIdx);
                    if (funcStart >= 0)
                    {
                        int d = 1;
                        int funcEnd = funcStart + 1;
                        bool inS = false;
                        while (funcEnd < toolObj.Length && d > 0)
                        {
                            char c = toolObj[funcEnd];
                            if (inS) { if (c == '\\') funcEnd++; else if (c == '"') inS = false; }
                            else { if (c == '"') inS = true; else if (c == '{') d++; else if (c == '}') d--; }
                            funcEnd++;
                        }
                        string funcObj = toolObj.Substring(funcStart, funcEnd - funcStart);

                        string name = MiniJson.ExtractString(funcObj, "name") ?? "";
                        string args = MiniJson.ExtractString(funcObj, "arguments") ?? "{}";

                        if (!string.IsNullOrEmpty(name))
                        {
                            result.toolCalls.Add(new ToolCallData
                            {
                                id = id,
                                name = name,
                                arguments = args
                            });
                        }
                    }
                }

                pos = objEnd;
            }

            return result;
        }

        private string BuildOpenAIRequestJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"model\":\"").Append(EscapeJson(Settings.model)).Append("\",");
            sb.Append("\"max_tokens\":4096,");

            // Messages array - start with system prompt
            sb.Append("\"messages\":[");

            // System message first
            sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(SystemPrompt.Get())).Append("\"}");

            // Add conversation messages
            for (int i = 0; i < messages.Count; i++)
            {
                sb.Append(",");
                var msg = messages[i];

                if (msg is ToolResultEntry toolResult)
                {
                    // OpenAI tool result format
                    sb.Append("{\"role\":\"tool\",\"tool_call_id\":\"").Append(EscapeJson(toolResult.toolCallId));
                    sb.Append("\",\"content\":\"").Append(EscapeJson(toolResult.content)).Append("\"}");
                }
                else if (msg.toolCalls != null && msg.toolCalls.Count > 0)
                {
                    // Assistant message with tool calls
                    sb.Append("{\"role\":\"assistant\"");
                    if (!string.IsNullOrEmpty(msg.content))
                        sb.Append(",\"content\":\"").Append(EscapeJson(msg.content)).Append("\"");
                    sb.Append(",\"tool_calls\":[");
                    for (int j = 0; j < msg.toolCalls.Count; j++)
                    {
                        if (j > 0) sb.Append(",");
                        var tc = msg.toolCalls[j];
                        sb.Append("{\"id\":\"").Append(EscapeJson(tc.id)).Append("\",");
                        sb.Append("\"type\":\"function\",");
                        sb.Append("\"function\":{\"name\":\"").Append(EscapeJson(tc.name)).Append("\",");
                        sb.Append("\"arguments\":\"").Append(EscapeJson(tc.arguments)).Append("\"}}");
                    }
                    sb.Append("]}");
                }
                else
                {
                    // Regular message
                    sb.Append("{\"role\":\"").Append(EscapeJson(msg.role)).Append("\",");
                    sb.Append("\"content\":\"").Append(EscapeJson(msg.content ?? "")).Append("\"}");
                }
            }
            sb.Append("],");

            // Tools
            sb.Append("\"tools\":").Append(ToolDefinitions.GetOpenAIToolsJson());

            sb.Append("}");
            return sb.ToString();
        }

        private IEnumerator ExecuteBlockingTool(string toolName, string toolCallId, Dictionary<string, string> parameters, List<ToolResultEntry> results)
        {
            string baseTool = toolName.Replace("_wait", "");

            string result;
            try
            {
                result = toolExecutor.ExecuteTool(baseTool, parameters);
            }
            catch (Exception ex)
            {
                results.Add(new ToolResultEntry { toolCallId = toolCallId, content = "{\"error\":\"" + EscapeJson(ex.Message) + "\"}" });
                yield break;
            }

            if (result.Contains("\"error\""))
            {
                results.Add(new ToolResultEntry { toolCallId = toolCallId, content = result });
                yield break;
            }

            if (chatWindow != null)
                chatWindow.SetStatus("Waiting for: " + baseTool + "...");

            Debug.Log("[ClaudePilot] Waiting for autopilot: " + baseTool);

            float timeout = 600f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                yield return new WaitForSeconds(1f);
                elapsed += 1f;

                if (stopRequested)
                {
                    results.Add(new ToolResultEntry { toolCallId = toolCallId, content = "{\"status\":\"cancelled\"}" });
                    yield break;
                }

                string status = toolExecutor.ExecuteTool("wait_for_autopilot", new Dictionary<string, string>());
                Debug.Log("[ClaudePilot] Autopilot status: " + status);

                // Check if all autopilots are idle/unavailable (not running)
                // Status format: {"success":true,"autopilots":{"ascent":"running","landing":"idle",...}}
                if (!status.Contains("\"running\""))
                {
                    // No autopilot is running - we're done
                    string telemetry = toolExecutor.ExecuteTool("get_vessel_telemetry", new Dictionary<string, string>());
                    string orbit = toolExecutor.ExecuteTool("get_orbit_info", new Dictionary<string, string>());
                    result = "{\"status\":\"completed\",\"tool\":\"" + baseTool + "\",\"telemetry\":" + telemetry + ",\"orbit\":" + orbit + "}";
                    Debug.Log("[ClaudePilot] Autopilot completed: " + baseTool);
                    break;
                }

                if (chatWindow != null && elapsed % 10f < 1f)
                    chatWindow.SetStatus("Waiting for " + baseTool + "... (" + (int)elapsed + "s)");
            }

            if (elapsed >= timeout)
                result = "{\"status\":\"timeout\",\"tool\":\"" + baseTool + "\"}";

            results.Add(new ToolResultEntry { toolCallId = toolCallId, content = result });
        }

        private void TrimHistory()
        {
            int max = Settings.maxHistoryMessages > 0 ? Settings.maxHistoryMessages : 200;

            while (messages.Count > max)
            {
                // Remove pairs: assistant with tool_calls + tool results
                if (messages.Count >= 2 && messages[0].role == "assistant" && messages[0].toolCalls != null && messages[0].toolCalls.Count > 0)
                {
                    messages.RemoveAt(0);
                    // Remove following tool results
                    while (messages.Count > 0 && messages[0] is ToolResultEntry)
                        messages.RemoveAt(0);
                    continue;
                }

                // Plain user/assistant pair
                if (messages[0].role == "user" && (messages[0].toolCalls == null || messages[0].toolCalls.Count == 0))
                {
                    messages.RemoveAt(0);
                    if (messages.Count > 0 && messages[0].role == "assistant" && (messages[0].toolCalls == null || messages[0].toolCalls.Count == 0))
                        messages.RemoveAt(0);
                    continue;
                }

                messages.RemoveAt(0);
            }

            // Must start with user or tool
            while (messages.Count > 0 && messages[0].role == "assistant")
                messages.RemoveAt(0);
        }

        public void ClearHistory()
        {
            messages.Clear();
        }

        internal static string EscapeJson(string s)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
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

        // Internal types
        private class MessageEntry
        {
            public string role;
            public string content;
            public List<ToolCallData> toolCalls;
        }

        private class ToolCallData
        {
            public string id;
            public string name;
            public string arguments;
        }

        private class ToolResultEntry : MessageEntry
        {
            public string toolCallId;

            public ToolResultEntry()
            {
                role = "tool";
            }
        }

        private class ParsedResponse
        {
            public string fullText = "";
            public List<ToolCallData> toolCalls = new List<ToolCallData>();
        }
    }

    // MiniJson helper class (unchanged)
    internal static class MiniJson
    {
        public static string ExtractString(string json, string key)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int keyIndex = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            int colonIndex = json.IndexOf(':', keyIndex + pattern.Length);
            if (colonIndex < 0) return null;

            return ExtractStringValueAfter(json, colonIndex + 1);
        }

        public static Dictionary<string, string> ParseFlat(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = json.IndexOf('{');
            if (i < 0) return result;
            i++;

            while (i < json.Length)
            {
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;

                i = colon + 1;
                while (i < json.Length && json[i] == ' ') i++;

                if (i >= json.Length) break;

                string value;
                if (json[i] == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\' && i + 1 < json.Length)
                        {
                            i++;
                            switch (json[i])
                            {
                                case '"': sb.Append('"'); break;
                                case '\\': sb.Append('\\'); break;
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                default: sb.Append(json[i]); break;
                            }
                        }
                        else
                        {
                            sb.Append(json[i]);
                        }
                        i++;
                    }
                    value = sb.ToString();
                    if (i < json.Length) i++;
                }
                else
                {
                    int valStart = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ' ')
                        i++;
                    value = json.Substring(valStart, i - valStart).Trim();
                    if (value == "null") { i++; continue; }
                }

                result[key] = value;

                while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                if (i < json.Length && json[i] == '}') break;
                i++;
            }

            return result;
        }

        private static string ExtractStringValueAfter(string json, int startIndex)
        {
            int i = startIndex;
            while (i < json.Length && json[i] != '"') i++;
            if (i >= json.Length) return null;

            i++;
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(json[i]);
                }
                i++;
            }

            return sb.ToString();
        }
    }
}
