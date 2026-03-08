using System;
using UnityEngine;

namespace ClaudePilot
{
    public class SceneController
    {
        // Switch to VAB editor
        public string GoToVAB()
        {
            try
            {
                if (HighLogic.LoadedScene == GameScenes.EDITOR &&
                    EditorDriver.editorFacility == EditorFacility.VAB)
                    return "{\"status\":\"already_in_vab\"}";

                EditorFacility facility = EditorFacility.VAB;
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
                EditorDriver.editorFacility = facility;
                HighLogic.LoadScene(GameScenes.EDITOR);
                return "{\"status\":\"switching_to_vab\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Switch to SPH editor
        public string GoToSPH()
        {
            try
            {
                if (HighLogic.LoadedScene == GameScenes.EDITOR &&
                    EditorDriver.editorFacility == EditorFacility.SPH)
                    return "{\"status\":\"already_in_sph\"}";

                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
                EditorDriver.editorFacility = EditorFacility.SPH;
                HighLogic.LoadScene(GameScenes.EDITOR);
                return "{\"status\":\"switching_to_sph\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Switch to tracking station
        public string GoToTrackingStation()
        {
            try
            {
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                HighLogic.LoadScene(GameScenes.TRACKSTATION);
                return "{\"status\":\"switching_to_tracking_station\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Switch to space center
        public string GoToSpaceCenter()
        {
            try
            {
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                HighLogic.LoadScene(GameScenes.SPACECENTER);
                return "{\"status\":\"switching_to_space_center\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Load a craft file in the current editor
        public string LoadCraftInEditor(string craftName, string facility)
        {
            try
            {
                if (HighLogic.LoadedScene != GameScenes.EDITOR)
                    return "{\"error\":\"Not in editor scene. Switch to VAB or SPH first.\"}";

                string path = KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder
                    + "/Ships/" + facility + "/" + craftName + ".craft";

                if (!System.IO.File.Exists(path))
                    return "{\"error\":\"Craft file not found: " + EscapeJson(craftName) + "\"}";

                EditorLogic.LoadShipFromFile(path);
                return "{\"status\":\"loaded\",\"craft\":\"" + EscapeJson(craftName) + "\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Launch the currently loaded vessel in editor
        public string LaunchVessel()
        {
            try
            {
                if (HighLogic.LoadedScene != GameScenes.EDITOR)
                    return "{\"error\":\"Not in editor. Load a craft first.\"}";

                if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null
                    || EditorLogic.fetch.ship.parts.Count == 0)
                    return "{\"error\":\"No vessel loaded in editor\"}";

                EditorLogic.fetch.launchVessel();
                return "{\"status\":\"launching\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Recover the active vessel (from flight)
        public string RecoverVessel()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null)
                    return "{\"error\":\"No active vessel\"}";

                if (!vessel.IsRecoverable)
                    return "{\"error\":\"Vessel is not recoverable (too far from KSC or not landed)\"}";

                GameEvents.OnVesselRecoveryRequested.Fire(vessel);
                return "{\"status\":\"recovering\",\"vessel\":\"" + EscapeJson(vessel.vesselName) + "\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Get current scene info
        public string GetCurrentScene()
        {
            try
            {
                string scene = HighLogic.LoadedScene.ToString();
                string facility = "";
                if (HighLogic.LoadedScene == GameScenes.EDITOR)
                    facility = EditorDriver.editorFacility.ToString();

                string vesselName = "";
                if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null)
                    vesselName = FlightGlobals.ActiveVessel.vesselName;

                return "{\"scene\":\"" + scene + "\""
                    + (facility != "" ? ",\"facility\":\"" + facility + "\"" : "")
                    + (vesselName != "" ? ",\"vessel\":\"" + EscapeJson(vesselName) + "\"" : "")
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Quick launch: load craft + launch in one step
        public string QuickLaunch(string craftName, string facility)
        {
            try
            {
                string path = KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder
                    + "/Ships/" + facility + "/" + craftName + ".craft";

                if (!System.IO.File.Exists(path))
                    return "{\"error\":\"Craft file not found: " + EscapeJson(craftName) + "\"}";

                // If we're already in flight or elsewhere, go to editor first then launch
                if (HighLogic.LoadedScene != GameScenes.EDITOR)
                {
                    // Direct launch from file using FlightDriver
                    FlightDriver.StartWithNewLaunch(path,
                        EditorLogic.FlagURL,
                        facility == "SPH" ? "Runway" : "LaunchPad",
                        new VesselCrewManifest());
                    return "{\"status\":\"launching\",\"craft\":\"" + EscapeJson(craftName) + "\"}";
                }
                else
                {
                    EditorLogic.LoadShipFromFile(path);
                    EditorLogic.fetch.launchVessel();
                    return "{\"status\":\"launching\",\"craft\":\"" + EscapeJson(craftName) + "\"}";
                }
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
