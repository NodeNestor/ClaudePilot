using System;
using UnityEngine;
using KSP.UI.Screens;

namespace ClaudePilot
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class ClaudePilotAddon : MonoBehaviour
    {
        public static ClaudePilotAddon Instance { get; private set; }

        private ChatWindow chatWindow;
        private TelemetryHUD telemetryHUD;
        private TelemetryProvider telemetryProvider;
        private ClaudeClient claudeClient;
        private ToolExecutor toolExecutor;
        private MechJebBridge mechJebBridge;
        private KramaxBridge kramaxBridge;
        private FlightController flightController;
        private CraftFileManager craftFileManager;
        private MissionPlanner missionPlanner;
        private SceneController sceneController;
        private ScienceController scienceController;

        private ApplicationLauncherButton toolbarButton;
        private bool toolbarButtonAdded = false;
        private static bool hasShownReady = false;

        // Autopilot monitoring
        private float autopilotCheckTimer = 0f;
        private const float AUTOPILOT_CHECK_INTERVAL = 3f; // Check every 3 seconds
        private static string lastAutopilotState = "";
        private static bool wasInFlight = false;

        private static void Log(string msg)
        {
            Debug.Log("[ClaudePilot] " + msg);
        }

        private void Awake()
        {
            Instance = this;
            Log("Awake in scene: " + HighLogic.LoadedScene);
        }

        private void Start()
        {
            try
            {
                Log("Start() begin...");

                Settings.Load();
                Log("Settings loaded. API key set: " + (!string.IsNullOrEmpty(Settings.apiKey)));
                Log("Model: " + Settings.model);

                telemetryProvider = new TelemetryProvider();
                Log("TelemetryProvider created.");

                mechJebBridge = new MechJebBridge();
                try
                {
                    mechJebBridge.Initialize();
                    Log("MechJebBridge initialized.");
                }
                catch (Exception ex)
                {
                    Log("MechJebBridge init failed (MechJeb may not be installed): " + ex.Message);
                }

                flightController = new FlightController();
                craftFileManager = new CraftFileManager();
                missionPlanner = new MissionPlanner();
                sceneController = new SceneController();
                scienceController = new ScienceController();

                kramaxBridge = new KramaxBridge();
                try
                {
                    kramaxBridge.Initialize();
                    Log("KramaxAutoPilot bridge initialized.");
                }
                catch (Exception ex)
                {
                    Log("KramaxAutoPilot bridge init failed: " + ex.Message);
                }

                Log("Bridge components created.");

                toolExecutor = new ToolExecutor(telemetryProvider, mechJebBridge, kramaxBridge, flightController, craftFileManager, missionPlanner, sceneController, scienceController);
                claudeClient = new ClaudeClient(toolExecutor);
                Log("Claude client created.");

                // Check if we need to resume from a scene change
                if (ClaudeClient.HasPendingAction)
                {
                    Log("Detected pending action, will resume after initialization.");
                }

                chatWindow = new ChatWindow();
                chatWindow.telemetryProvider = telemetryProvider;
                chatWindow.claudeClient = claudeClient;
                chatWindow.missionPlanner = missionPlanner;
                claudeClient.chatWindow = chatWindow;

                telemetryHUD = new TelemetryHUD();
                telemetryHUD.telemetryProvider = telemetryProvider;
                Log("UI components created.");

                // Only show ready message on first load, not every scene switch
                if (!hasShownReady)
                {
                    chatWindow.AddMessage("system", "ClaudePilot ready. Model: " + Settings.ModelDisplayName + ". Press Alt+P or use toolbar button.");
                    hasShownReady = true;
                }

                GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
                GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnGUIApplicationLauncherUnreadifying);

                // If the launcher is already ready, add the button now
                if (ApplicationLauncher.Ready)
                    OnGUIApplicationLauncherReady();

                // Resume conversation if we had a scene change
                if (claudeClient != null)
                {
                    claudeClient.OnSceneChange();
                }

                Log("Start() complete!");
            }
            catch (Exception ex)
            {
                Log("FATAL ERROR in Start(): " + ex.ToString());
            }
        }

        private void OnGUIApplicationLauncherReady()
        {
            try
            {
                if (toolbarButtonAdded) return;

                Texture2D icon = new Texture2D(38, 38, TextureFormat.ARGB32, false);
                Color cpColor = new Color(0.4f, 0.7f, 1f);
                for (int x = 0; x < 38; x++)
                    for (int y = 0; y < 38; y++)
                        icon.SetPixel(x, y, (x + y) % 8 < 4 ? cpColor : Color.clear);
                icon.Apply();

                toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarOn,
                    OnToolbarOff,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.FLIGHT
                        | ApplicationLauncher.AppScenes.VAB
                        | ApplicationLauncher.AppScenes.SPH
                        | ApplicationLauncher.AppScenes.SPACECENTER
                        | ApplicationLauncher.AppScenes.TRACKSTATION,
                    icon
                );
                toolbarButtonAdded = true;
                Log("Toolbar button added.");
            }
            catch (Exception ex)
            {
                Log("ERROR adding toolbar button: " + ex.ToString());
            }
        }

        private void OnGUIApplicationLauncherUnreadifying(GameScenes scene)
        {
            RemoveToolbarButton();
        }

        private void OnToolbarOn()
        {
            Log("Toolbar ON - showing chat.");
            chatWindow.Show();
        }

        private void OnToolbarOff()
        {
            Log("Toolbar OFF - hiding chat.");
            chatWindow.Hide();
        }

        private void Update()
        {
            try
            {
                if (claudeClient != null)
                    claudeClient.ProcessMainThreadQueue();

                // Alt+P to toggle (Alt+C conflicts with KSP IVA view)
                if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.P))
                {
                    Log("Keybind Alt+P pressed, toggling chat.");
                    chatWindow.Toggle();

                    if (toolbarButton != null)
                    {
                        if (chatWindow.IsVisible)
                            toolbarButton.SetTrue(false);
                        else
                            toolbarButton.SetFalse(false);
                    }
                }

                // Monitor autopilot status changes in flight
                if (HighLogic.LoadedSceneIsFlight)
                {
                    autopilotCheckTimer += Time.deltaTime;
                    if (autopilotCheckTimer >= AUTOPILOT_CHECK_INTERVAL)
                    {
                        autopilotCheckTimer = 0f;
                        CheckAutopilotEvents();
                    }
                    wasInFlight = true;
                }
                else
                {
                    // Detect scene changes from flight (e.g. recovered vessel)
                    if (wasInFlight)
                    {
                        wasInFlight = false;
                        lastAutopilotState = "";
                        AutoNotify("Scene changed from Flight to " + HighLogic.LoadedScene + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR in Update(): " + ex.Message);
            }
        }

        private void CheckAutopilotEvents()
        {
            if (mechJebBridge == null || !mechJebBridge.IsAvailable) return;
            if (FlightGlobals.ActiveVessel == null) return;

            string statusJson = mechJebBridge.GetAutopilotStatus();
            if (statusJson == null || statusJson.Contains("error")) return;

            // Check for state transitions
            if (string.IsNullOrEmpty(lastAutopilotState))
            {
                lastAutopilotState = statusJson;
                return;
            }

            // Detect transitions: something was "running" and is now "idle"
            string[] modules = { "ascent", "landing", "nodeExecutor", "rendezvous", "docking" };
            string[] friendlyNames = { "Ascent autopilot", "Landing autopilot", "Node executor", "Rendezvous autopilot", "Docking autopilot" };

            for (int i = 0; i < modules.Length; i++)
            {
                bool wasRunning = lastAutopilotState.Contains("\"" + modules[i] + "\":\"running\"");
                bool isRunning = statusJson.Contains("\"" + modules[i] + "\":\"running\"");

                if (wasRunning && !isRunning)
                {
                    // This autopilot just finished!
                    string situation = FlightGlobals.ActiveVessel.situation.ToString();
                    string alt = FlightGlobals.ActiveVessel.altitude.ToString("F0");
                    string msg = friendlyNames[i] + " has completed. Vessel situation: " + situation + ", altitude: " + alt + "m. What should I do next?";
                    Log("Autopilot event: " + friendlyNames[i] + " finished.");
                    AutoNotify(msg);
                }
            }

            // Detect vessel situation changes
            var vessel = FlightGlobals.ActiveVessel;
            bool nowInOrbit = vessel.situation == Vessel.Situations.ORBITING;
            bool wasOrbiting = lastAutopilotState.Contains("ORBITING");
            string currentSit = vessel.situation.ToString();
            if (!lastAutopilotState.Contains(currentSit))
            {
                // Situation changed — embed it in the state for next check
            }

            lastAutopilotState = statusJson + "|SIT:" + currentSit;
        }

        private void AutoNotify(string eventMessage)
        {
            if (claudeClient == null || chatWindow == null) return;

            // Don't notify if Claude is already processing something
            if (claudeClient.IsBusy) return;

            Log("Auto-notify: " + eventMessage);
            chatWindow.AddMessage("system", "[Event] " + eventMessage);
            claudeClient.SendMessage("[AUTOPILOT_EVENT] " + eventMessage);
        }

        private void OnGUI()
        {
            try
            {
                if (chatWindow != null)
                    chatWindow.OnGUI();

                if (telemetryHUD != null)
                {
                    telemetryHUD.chatWindowVisible = chatWindow != null && chatWindow.IsVisible;
                    telemetryHUD.OnGUI();
                }
            }
            catch (Exception ex)
            {
                Log("ERROR in OnGUI(): " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            Settings.Save();
            RemoveToolbarButton();

            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnGUIApplicationLauncherUnreadifying);

            if (Instance == this) Instance = null;

            Log("Addon destroyed.");
        }

        private void RemoveToolbarButton()
        {
            if (toolbarButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
                toolbarButtonAdded = false;
            }
        }
    }
}
