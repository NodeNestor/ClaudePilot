using UnityEngine;

namespace ClaudePilot
{
    public class TelemetryHUD
    {
        private Rect hudRect;
        private GUIStyle backgroundStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized = false;

        public TelemetryProvider telemetryProvider;
        public bool chatWindowVisible = false;

        public TelemetryHUD()
        {
            hudRect = new Rect(Screen.width - 220, 40, 210, 140);
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            backgroundStyle = new GUIStyle(GUI.skin.box);
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            bgTex.Apply();
            backgroundStyle.normal.background = bgTex;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 11;
            labelStyle.normal.textColor = new Color(0.8f, 0.95f, 1f);
            labelStyle.padding = new RectOffset(4, 4, 1, 1);

            stylesInitialized = true;
        }

        public void OnGUI()
        {
            if (!Settings.enableTelemetryHUD || !chatWindowVisible) return;
            if (telemetryProvider == null) return;

            var data = telemetryProvider.GetTelemetry();
            if (data == null) return;

            InitStyles();

            hudRect.x = Screen.width - 220;

            GUI.Box(hudRect, "", backgroundStyle);

            GUILayout.BeginArea(hudRect);
            GUILayout.Space(4);

            GUILayout.Label(data.vesselName + " - " + data.situation, labelStyle);
            GUILayout.Label("Alt: " + data.altitudeFormatted, labelStyle);
            GUILayout.Label("Srf: " + data.surfaceSpeedFormatted + "  Orb: " + data.orbitalSpeedFormatted, labelStyle);
            GUILayout.Label("Ap: " + data.apoapsisFormatted + "  Pe: " + data.periapsisFormatted, labelStyle);
            GUILayout.Label("Fuel: " + data.fuelPercent.ToString("F1") + "%", labelStyle);

            if (!string.IsNullOrEmpty(data.targetName))
                GUILayout.Label("Target: " + data.targetName, labelStyle);

            GUILayout.EndArea();
        }
    }
}
