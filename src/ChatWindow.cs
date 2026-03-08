using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClaudePilot
{
    public class ChatMessage
    {
        public string role;
        public string content;
        public DateTime timestamp;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
            this.timestamp = DateTime.Now;
        }
    }

    public class ChatWindow
    {
        private int windowId;
        private Rect windowRect;
        private bool visible = false;
        private Vector2 scrollPosition;
        private string inputText = "";
        private string statusText = "Ready";
        private bool showSettings = false;

        // Static so chat history survives scene switches
        private static List<ChatMessage> messages = new List<ChatMessage>();
        public string streamingMessage = null;

        public ClaudeClient claudeClient;
        public TelemetryProvider telemetryProvider;
        public MissionPlanner missionPlanner;
        private bool showMissionPlan = true;

        public bool IsVisible { get { return visible; } }

        public ChatWindow()
        {
            windowId = "ClaudePilot_Chat".GetHashCode();
            windowRect = new Rect(
                Settings.windowPosX,
                Settings.windowPosY,
                Settings.windowWidth,
                Settings.windowHeight
            );
            // Scroll to bottom on creation (restoring after scene switch)
            scrollPosition.y = float.MaxValue;
        }

        public void Show() { visible = true; }
        public void Hide() { visible = false; }
        public void Toggle() { visible = !visible; }

        public void AddMessage(string role, string content)
        {
            messages.Add(new ChatMessage(role, content));
            if (messages.Count > 100) // Keep more in UI, API history is trimmed separately
                messages.RemoveAt(0);
            scrollPosition.y = float.MaxValue;
        }

        public void SetStatus(string status)
        {
            statusText = status;
        }

        public void OnGUI()
        {
            if (!visible) return;

            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(
                windowId,
                windowRect,
                DrawWindow,
                "ClaudePilot - " + Settings.ModelDisplayName
            );

            Settings.windowPosX = windowRect.x;
            Settings.windowPosY = windowRect.y;
            Settings.windowWidth = windowRect.width;
            Settings.windowHeight = windowRect.height;
        }

        private void DrawWindow(int id)
        {
            DrawTelemetryBar();

            GUILayout.BeginHorizontal();
            DrawModelSelector();
            GUILayout.FlexibleSpace();

            GUI.color = statusText == "Ready" ? Color.green : Color.yellow;
            GUILayout.Label(statusText);
            GUI.color = Color.white;

            if (GUILayout.Button(showSettings ? "Chat" : "Cfg", GUILayout.Width(40)))
                showSettings = !showSettings;

            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Clr", GUILayout.Width(35)))
            {
                messages.Clear();
                if (claudeClient != null)
                    claudeClient.ClearHistory();
                AddMessage("system", "Chat cleared. Model: " + Settings.ModelDisplayName);
            }
            GUI.color = Color.white;

            GUILayout.EndHorizontal();

            if (showSettings)
            {
                Settings.DrawSettingsPanel();
            }
            else
            {
                DrawMissionPlan();
                DrawChatArea();
                DrawInputArea();
            }

            GUI.DragWindow();
        }

        private void DrawTelemetryBar()
        {
            if (telemetryProvider == null) return;

            var data = telemetryProvider.GetTelemetry();
            if (data == null) return;

            GUIStyle barStyle = new GUIStyle(GUI.skin.label);
            barStyle.fontSize = 10;
            barStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                string.Format("Alt:{0} | Spd:{1} | Ap:{2} Pe:{3} | Fuel:{4}%",
                    data.altitudeFormatted,
                    data.speedFormatted,
                    data.apoapsisFormatted,
                    data.periapsisFormatted,
                    data.fuelPercent.ToString("F0")),
                barStyle
            );
            GUILayout.EndHorizontal();
        }

        private void DrawModelSelector()
        {
            string[] labels = { "H", "S", "O" };
            string[] models = {
                "claude-haiku-4-5-20251001",
                "claude-sonnet-4-6",
                "claude-opus-4-6"
            };

            for (int i = 0; i < models.Length; i++)
            {
                bool isActive = Settings.model == models[i];
                GUI.color = isActive ? Color.green : Color.gray;
                if (GUILayout.Button(labels[i], GUILayout.Width(25)))
                {
                    Settings.model = models[i];
                }
            }
            GUI.color = Color.white;
        }

        private void DrawMissionPlan()
        {
            if (missionPlanner == null || missionPlanner.ActivePlan == null || !missionPlanner.ActivePlan.isActive)
                return;

            var plan = missionPlanner.ActivePlan;

            GUILayout.BeginHorizontal();
            GUI.color = Color.cyan;
            if (GUILayout.Button(showMissionPlan ? "[-] Mission: " + plan.name : "[+] Mission: " + plan.name))
                showMissionPlan = !showMissionPlan;
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (!showMissionPlan) return;

            GUIStyle stepStyle = new GUIStyle(GUI.skin.label);
            stepStyle.fontSize = 11;
            stepStyle.margin = new RectOffset(10, 0, 0, 0);

            for (int i = 0; i < plan.steps.Count; i++)
            {
                var step = plan.steps[i];
                string icon;
                switch (step.status)
                {
                    case MissionStepStatus.Completed:
                        GUI.color = Color.green;
                        icon = "[OK]";
                        break;
                    case MissionStepStatus.InProgress:
                        GUI.color = Color.yellow;
                        icon = "[>>]";
                        break;
                    case MissionStepStatus.Failed:
                        GUI.color = Color.red;
                        icon = "[X]";
                        break;
                    case MissionStepStatus.Skipped:
                        GUI.color = Color.gray;
                        icon = "[--]";
                        break;
                    default: // Pending
                        GUI.color = new Color(0.6f, 0.6f, 0.6f);
                        icon = "[ ]";
                        break;
                }

                string dvLabel = step.estimatedDeltaV > 0 ? " (" + step.estimatedDeltaV.ToString("F0") + " m/s)" : "";
                GUILayout.Label(icon + " " + (i + 1) + ". " + step.description + dvLabel, stepStyle);
            }

            GUI.color = new Color(0.7f, 0.9f, 1f);
            GUILayout.Label("  Total dv: " + plan.totalDeltaVRequired.ToString("F0") + " m/s", stepStyle);
            GUI.color = Color.white;

            // Separator
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        }

        private void DrawChatArea()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            foreach (var msg in messages)
            {
                DrawMessage(msg.role, msg.content);
            }

            if (streamingMessage != null)
            {
                DrawMessage("assistant", streamingMessage + " ...");
            }

            GUILayout.EndScrollView();
        }

        private void DrawMessage(string role, string content)
        {
            switch (role)
            {
                case "user":
                    GUI.color = Color.white;
                    break;
                case "assistant":
                    GUI.color = new Color(0.5f, 1f, 0.5f);
                    break;
                case "system":
                    GUI.color = Color.yellow;
                    break;
                case "error":
                    GUI.color = Color.red;
                    break;
            }

            string label = role.Substring(0, 1).ToUpper() + role.Substring(1);
            GUILayout.Label(string.Format("[{0}] {1}", label, content));
            GUI.color = Color.white;
        }

        private void DrawInputArea()
        {
            GUILayout.BeginHorizontal();

            // Show Stop button when busy
            if (claudeClient != null && claudeClient.IsBusy)
            {
                GUI.color = Color.red;
                if (GUILayout.Button("STOP", GUILayout.Width(60)))
                {
                    claudeClient.Stop();
                }
                GUI.color = Color.white;
            }

            GUI.SetNextControlName("ChatInput");
            inputText = GUILayout.TextField(inputText, GUILayout.ExpandWidth(true));

            bool enterPressed = Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "ChatInput";

            GUI.enabled = claudeClient == null || !claudeClient.IsBusy;
            if ((GUILayout.Button("Send", GUILayout.Width(50)) || enterPressed)
                && !string.IsNullOrEmpty(inputText.Trim()))
            {
                string text = inputText.Trim();
                inputText = "";
                AddMessage("user", text);

                if (claudeClient != null)
                    claudeClient.SendMessage(text);
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }
    }
}
