using System.IO;
using UnityEngine;

namespace ClaudePilot
{
    public static class Settings
    {
        public static string apiKey = "";
        public static string apiBaseUrl = "";
        public static string apiProxyHost = "127.0.0.1";
        public static int apiProxyPort = 5588;
        public static bool useProxy = true;
        public static string model = "claude-sonnet-4-6";
        public static string keybind = "LeftAlt+C";
        public static float windowPosX = 100f;
        public static float windowPosY = 100f;
        public static float windowWidth = 450f;
        public static float windowHeight = 600f;
        public static int maxHistoryMessages = 200;
        public static bool enableTelemetryHUD = true;
        public static bool enableMcpServer = false;
        public static int mcpPort = 8745;

        private static readonly string[] availableModels = new[]
        {
            "claude-haiku-4-5-20251001",
            "claude-sonnet-4-6",
            "claude-opus-4-6"
        };

        private static string configPath =
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ClaudePilot/config.cfg");

        private static bool showApiKey = false;
        private static int selectedModelIndex = 1;

        public static string ModelDisplayName
        {
            get
            {
                switch (model)
                {
                    case "claude-haiku-4-5-20251001": return "Haiku";
                    case "claude-sonnet-4-6": return "Sonnet";
                    case "claude-opus-4-6": return "Opus";
                    default: return model;
                }
            }
        }

        public static void Load()
        {
            if (!File.Exists(configPath))
            {
                Debug.Log("[ClaudePilot] Config file not found, using defaults.");
                return;
            }

            ConfigNode root = ConfigNode.Load(configPath);
            if (root == null) return;

            ConfigNode cfg = root.HasNode("CLAUDEPILOT_CONFIG")
                ? root.GetNode("CLAUDEPILOT_CONFIG")
                : root;

            if (cfg.HasValue("apiKey")) apiKey = cfg.GetValue("apiKey");
            if (cfg.HasValue("model")) model = cfg.GetValue("model");
            if (cfg.HasValue("keybind")) keybind = cfg.GetValue("keybind");
            if (cfg.HasValue("windowPosX")) float.TryParse(cfg.GetValue("windowPosX"), out windowPosX);
            if (cfg.HasValue("windowPosY")) float.TryParse(cfg.GetValue("windowPosY"), out windowPosY);
            if (cfg.HasValue("windowWidth")) float.TryParse(cfg.GetValue("windowWidth"), out windowWidth);
            if (cfg.HasValue("windowHeight")) float.TryParse(cfg.GetValue("windowHeight"), out windowHeight);
            if (cfg.HasValue("maxHistoryMessages")) int.TryParse(cfg.GetValue("maxHistoryMessages"), out maxHistoryMessages);
            if (cfg.HasValue("enableTelemetryHUD")) bool.TryParse(cfg.GetValue("enableTelemetryHUD"), out enableTelemetryHUD);
            if (cfg.HasValue("apiProxyHost")) apiProxyHost = cfg.GetValue("apiProxyHost");
            if (cfg.HasValue("apiProxyPort")) int.TryParse(cfg.GetValue("apiProxyPort"), out apiProxyPort);
            if (cfg.HasValue("useProxy")) bool.TryParse(cfg.GetValue("useProxy"), out useProxy);
            if (cfg.HasValue("enableMcpServer")) bool.TryParse(cfg.GetValue("enableMcpServer"), out enableMcpServer);
            if (cfg.HasValue("mcpPort")) int.TryParse(cfg.GetValue("mcpPort"), out mcpPort);
            // Build URL from components (KSP ConfigNode mangles "://")
            if (useProxy && !string.IsNullOrEmpty(apiProxyHost))
                apiBaseUrl = "http://" + apiProxyHost + ":" + apiProxyPort + "/v1/messages";

            for (int i = 0; i < availableModels.Length; i++)
            {
                if (availableModels[i] == model) selectedModelIndex = i;
            }

            Debug.Log("[ClaudePilot] Settings loaded.");
        }

        public static void Save()
        {
            ConfigNode root = new ConfigNode();
            ConfigNode cfg = root.AddNode("CLAUDEPILOT_CONFIG");

            cfg.AddValue("apiKey", apiKey);
            cfg.AddValue("model", model);
            cfg.AddValue("keybind", keybind);
            cfg.AddValue("windowPosX", windowPosX);
            cfg.AddValue("windowPosY", windowPosY);
            cfg.AddValue("windowWidth", windowWidth);
            cfg.AddValue("windowHeight", windowHeight);
            cfg.AddValue("maxHistoryMessages", maxHistoryMessages);
            cfg.AddValue("enableTelemetryHUD", enableTelemetryHUD);
            cfg.AddValue("apiProxyHost", apiProxyHost);
            cfg.AddValue("apiProxyPort", apiProxyPort);
            cfg.AddValue("useProxy", useProxy);
            cfg.AddValue("enableMcpServer", enableMcpServer);
            cfg.AddValue("mcpPort", mcpPort);

            root.Save(configPath);
            Debug.Log("[ClaudePilot] Settings saved.");
        }

        public static void DrawSettingsPanel()
        {
            GUILayout.Label("--- Settings ---", GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();
            GUILayout.Label("API Key:", GUILayout.Width(60));
            if (showApiKey)
                apiKey = GUILayout.TextField(apiKey, GUILayout.ExpandWidth(true));
            else
                apiKey = GUILayout.PasswordField(apiKey, '*', GUILayout.ExpandWidth(true));
            if (GUILayout.Button(showApiKey ? "Hide" : "Show", GUILayout.Width(45)))
                showApiKey = !showApiKey;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Model:", GUILayout.Width(60));
            for (int i = 0; i < availableModels.Length; i++)
            {
                string label;
                switch (i)
                {
                    case 0: label = "H"; break;
                    case 1: label = "S"; break;
                    default: label = "O"; break;
                }

                bool isSelected = selectedModelIndex == i;
                GUI.color = isSelected ? Color.green : Color.white;
                if (GUILayout.Button(label, GUILayout.Width(30)))
                {
                    selectedModelIndex = i;
                    model = availableModels[i];
                }
            }
            GUI.color = Color.white;
            GUILayout.Label(ModelDisplayName, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            useProxy = GUILayout.Toggle(useProxy, "Proxy:", GUILayout.Width(60));
            apiProxyHost = GUILayout.TextField(apiProxyHost, GUILayout.ExpandWidth(true));
            string portStr = GUILayout.TextField(apiProxyPort.ToString(), GUILayout.Width(50));
            int.TryParse(portStr, out apiProxyPort);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Keybind:", GUILayout.Width(60));
            keybind = GUILayout.TextField(keybind, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            enableTelemetryHUD = GUILayout.Toggle(enableTelemetryHUD, "Show Telemetry HUD");

            GUILayout.BeginHorizontal();
            enableMcpServer = GUILayout.Toggle(enableMcpServer, "MCP Server:", GUILayout.Width(100));
            string mcpPortStr = GUILayout.TextField(mcpPort.ToString(), GUILayout.Width(50));
            int.TryParse(mcpPortStr, out mcpPort);
            GUILayout.Label(enableMcpServer ? "localhost:" + mcpPort : "off", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Save Settings"))
                Save();
        }
    }
}
