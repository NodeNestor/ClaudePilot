using System;
using System.Collections.Generic;

namespace ClaudePilot
{
    public class ToolExecutor
    {
        private TelemetryProvider telemetryProvider;
        private MechJebBridge mechJebBridge;
        private KramaxBridge kramaxBridge;
        private FlightController flightController;
        private CraftFileManager craftFileManager;
        private MissionPlanner missionPlanner;
        private SceneController sceneController;
        private ScienceController scienceController;

        public ToolExecutor(TelemetryProvider telemetryProvider, MechJebBridge mechJebBridge,
            KramaxBridge kramaxBridge, FlightController flightController,
            CraftFileManager craftFileManager, MissionPlanner missionPlanner,
            SceneController sceneController, ScienceController scienceController)
        {
            this.telemetryProvider = telemetryProvider;
            this.mechJebBridge = mechJebBridge;
            this.kramaxBridge = kramaxBridge;
            this.flightController = flightController;
            this.craftFileManager = craftFileManager;
            this.missionPlanner = missionPlanner;
            this.sceneController = sceneController;
            this.scienceController = scienceController;
        }

        public string ExecuteTool(string toolName, Dictionary<string, string> parameters)
        {
            try
            {
                switch (toolName)
                {
                    case "get_vessel_telemetry":
                        return telemetryProvider.GetTelemetryJson();

                    case "get_orbit_info":
                        return telemetryProvider.GetOrbitJson();

                    case "get_delta_v":
                        return telemetryProvider.GetDeltaVJson();

                    case "get_resources":
                        return telemetryProvider.GetResourcesJson();

                    case "get_available_bodies":
                        return telemetryProvider.GetBodiesJson();

                    case "set_target":
                        return flightController.SetTarget(GetParam(parameters, "name"));

                    case "launch_to_orbit":
                    {
                        double altitude = GetDoubleParam(parameters, "altitude", 80000);
                        double inclination = GetDoubleParam(parameters, "inclination", 0);
                        return mechJebBridge.LaunchToOrbit(altitude, inclination);
                    }

                    case "plan_transfer":
                        return mechJebBridge.PlanTransfer(GetParam(parameters, "target_body"));

                    case "circularize":
                    {
                        bool atApoapsis = GetBoolParam(parameters, "at_apoapsis", true);
                        return mechJebBridge.Circularize(atApoapsis);
                    }

                    case "execute_next_node":
                        return mechJebBridge.ExecuteNextNode();

                    case "land":
                    {
                        double? lat = GetOptionalDoubleParam(parameters, "latitude");
                        double? lon = GetOptionalDoubleParam(parameters, "longitude");
                        return mechJebBridge.Land(lat, lon);
                    }

                    case "start_rendezvous":
                        return mechJebBridge.StartRendezvous();

                    case "start_docking":
                        return mechJebBridge.StartDocking();

                    case "set_time_warp":
                    {
                        int rateIndex = GetIntParam(parameters, "rate_index", 0);
                        return flightController.SetTimeWarp(rateIndex);
                    }

                    case "stop_time_warp":
                        return flightController.StopTimeWarp();

                    case "warp_to_next_node":
                    {
                        double leadTime = GetDoubleParam(parameters, "lead_time", 60);
                        return flightController.WarpToNextNode(leadTime);
                    }

                    case "activate_stage":
                        return flightController.ActivateStage();

                    case "set_sas_mode":
                        return flightController.SetSASMode(GetParam(parameters, "mode"));

                    case "toggle_action_group":
                    {
                        int group = GetIntParam(parameters, "group", 1);
                        return flightController.ToggleActionGroup(group);
                    }

                    case "wait_for_autopilot":
                        return mechJebBridge.GetAutopilotStatus();

                    case "list_craft_files":
                        return craftFileManager.ListCraftFiles(GetParam(parameters, "facility"));

                    case "read_craft_file":
                        return craftFileManager.ReadCraftFile(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"));

                    case "analyze_craft":
                        return craftFileManager.AnalyzeCraft(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"));

                    case "modify_craft_part":
                    {
                        string newPart = null;
                        parameters.TryGetValue("new_part", out newPart);
                        return craftFileManager.ModifyCraftPart(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"),
                            GetParam(parameters, "action"),
                            GetIntParam(parameters, "part_index", 0),
                            newPart);
                    }

                    // Mission planning tools
                    case "get_delta_v_map":
                        return missionPlanner.GetDeltaVMap(
                            GetParam(parameters, "from"),
                            GetParam(parameters, "to"));

                    case "get_full_delta_v_map":
                        return missionPlanner.GetFullDeltaVMap();

                    case "suggest_mission":
                        return missionPlanner.SuggestMissionSteps(
                            GetParam(parameters, "destination"),
                            GetBoolParam(parameters, "round_trip", true));

                    case "create_mission_plan":
                        return missionPlanner.CreateMissionPlan(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "destination"),
                            GetParam(parameters, "steps_json"));

                    case "get_mission_plan":
                        return missionPlanner.GetMissionPlan();

                    case "update_mission_step":
                    {
                        string result = null;
                        parameters.TryGetValue("result", out result);
                        return missionPlanner.UpdateMissionStep(
                            GetIntParam(parameters, "step_index", 0),
                            GetParam(parameters, "status"),
                            result);
                    }

                    case "get_next_mission_step":
                        return missionPlanner.GetNextStep();

                    case "cancel_mission_plan":
                        return missionPlanner.CancelMissionPlan();

                    case "execute_mission_plan":
                        return ExecuteMissionPlan(GetBoolParam(parameters, "stop_on_failure", true));

                    // Scene switching tools
                    case "go_to_vab":
                        return sceneController.GoToVAB();

                    case "go_to_sph":
                        return sceneController.GoToSPH();

                    case "go_to_tracking_station":
                        return sceneController.GoToTrackingStation();

                    case "go_to_space_center":
                        return sceneController.GoToSpaceCenter();

                    case "get_current_scene":
                        return sceneController.GetCurrentScene();

                    case "load_craft_in_editor":
                        return sceneController.LoadCraftInEditor(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"));

                    case "launch_vessel":
                        return sceneController.LaunchVessel();

                    case "quick_launch":
                        return sceneController.QuickLaunch(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"));

                    case "recover_vessel":
                        return sceneController.RecoverVessel();

                    case "create_maneuver_node":
                        return flightController.CreateManeuverNode(
                            GetDoubleParam(parameters, "prograde", 0),
                            GetDoubleParam(parameters, "normal", 0),
                            GetDoubleParam(parameters, "radial", 0),
                            GetDoubleParam(parameters, "ut", 0));

                    case "delete_all_maneuver_nodes":
                        return flightController.DeleteAllManeuverNodes();

                    case "auto_transfer":
                        return flightController.AutoTransfer(
                            GetParam(parameters, "target_body"),
                            GetBoolParam(parameters, "wait_for_window", true),
                            GetBoolParam(parameters, "execute", true));

                    case "calculate_transfer":
                        return TransferCalculator.CalculateTransfer(
                            GetParam(parameters, "target_body"),
                            0);

                    case "get_maneuver_node_info":
                        return flightController.GetManeuverNodeInfo();

                    case "warp_to_apoapsis":
                        return flightController.WarpToApoapsis(
                            GetDoubleParam(parameters, "lead_time", 30));

                    case "warp_to_periapsis":
                        return flightController.WarpToPeriapsis(
                            GetDoubleParam(parameters, "lead_time", 30));

                    case "warp_to_time":
                        return flightController.WarpToTime(
                            GetDoubleParam(parameters, "ut", 0));

                    case "warp_to_soi_change":
                        return flightController.WarpToSOIChange(
                            GetDoubleParam(parameters, "lead_time", 60));

                    case "get_available_parts":
                    {
                        string cat = null, search = null, size = null, mod = null;
                        parameters.TryGetValue("category", out cat);
                        parameters.TryGetValue("search", out search);
                        parameters.TryGetValue("size", out size);
                        parameters.TryGetValue("mod", out mod);
                        bool includeLocked = GetBoolParam(parameters, "include_locked", false);
                        int page = GetIntParam(parameters, "page", 1);
                        return craftFileManager.GetAvailableParts(cat, search, size, mod, includeLocked, page);
                    }

                    case "get_part_categories":
                        return craftFileManager.GetPartCategories();

                    case "create_craft":
                        return craftFileManager.CreateCraft(
                            GetParam(parameters, "name"),
                            GetParam(parameters, "facility"),
                            GetParam(parameters, "parts_json"));

                    case "land_at":
                        return mechJebBridge.LandAt(GetParam(parameters, "location"));

                    case "get_landing_locations":
                        return mechJebBridge.GetLandingLocations();

                    // Plane autopilot tools (KramaxAutoPilot)
                    case "plane_set_heading":
                        return kramaxBridge.SetHeading(GetDoubleParam(parameters, "heading", 0));

                    case "plane_set_altitude":
                        return kramaxBridge.SetAltitude(GetDoubleParam(parameters, "altitude", 5000));

                    case "plane_set_speed":
                        return kramaxBridge.SetSpeed(GetDoubleParam(parameters, "speed", 200));

                    case "plane_cruise":
                        return kramaxBridge.SetCruise(
                            GetDoubleParam(parameters, "heading", 90),
                            GetDoubleParam(parameters, "altitude", 5000),
                            GetDoubleParam(parameters, "speed", 200));

                    case "plane_fly_to":
                        return kramaxBridge.FlyTo(
                            GetDoubleParam(parameters, "latitude", 0),
                            GetDoubleParam(parameters, "longitude", 0),
                            GetDoubleParam(parameters, "altitude", 0));

                    case "plane_auto_land":
                        return kramaxBridge.AutoLand();

                    case "plane_disengage":
                        return kramaxBridge.Disengage();

                    // Science, economy, tech tree, contracts, crew
                    case "get_game_economy":
                        return scienceController.GetGameEconomy();

                    case "get_science_experiments":
                        return scienceController.GetScienceExperiments();

                    case "run_experiment":
                        return scienceController.RunExperiment(
                            GetIntParam(parameters, "experiment_index", 0));

                    case "run_all_experiments":
                        return scienceController.RunAllExperiments();

                    case "collect_all_science":
                        return scienceController.CollectAllScience();

                    case "transmit_all_science":
                        return scienceController.TransmitAllScience();

                    case "get_tech_tree":
                        return scienceController.GetTechTree();

                    case "research_tech":
                        return scienceController.ResearchTech(
                            GetParam(parameters, "tech_id"));

                    case "get_contracts":
                        return scienceController.GetContracts();

                    case "accept_contract":
                        return scienceController.AcceptContract(
                            GetIntParam(parameters, "contract_index", 0));

                    case "get_crew_info":
                        return scienceController.GetCrewInfo();

                    case "go_eva":
                        return scienceController.GoEVA(
                            GetIntParam(parameters, "crew_index", 0));

                    default:
                        return "{\"error\": \"Unknown tool: " + EscapeJsonString(toolName) + "\"}";
                }
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private static string GetParam(Dictionary<string, string> parameters, string key)
        {
            string value;
            if (parameters != null && parameters.TryGetValue(key, out value))
                return value;
            return "";
        }

        private static double GetDoubleParam(Dictionary<string, string> parameters, string key, double defaultValue)
        {
            string value = GetParam(parameters, key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            double result;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return defaultValue;
        }

        private static double? GetOptionalDoubleParam(Dictionary<string, string> parameters, string key)
        {
            string value = GetParam(parameters, key);
            if (string.IsNullOrEmpty(value))
                return null;
            double result;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return null;
        }

        private static int GetIntParam(Dictionary<string, string> parameters, string key, int defaultValue)
        {
            string value = GetParam(parameters, key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            int result;
            if (int.TryParse(value, out result))
                return result;
            return defaultValue;
        }

        private static bool GetBoolParam(Dictionary<string, string> parameters, string key, bool defaultValue)
        {
            string value = GetParam(parameters, key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            if (value == "true" || value == "True" || value == "1")
                return true;
            if (value == "false" || value == "False" || value == "0")
                return false;
            return defaultValue;
        }

        public string ExecuteMissionPlan(bool stopOnFailure)
        {
            var plan = missionPlanner.ActivePlan;
            if (plan == null || !plan.isActive)
                return "{\"error\":\"No active mission plan to execute\"}";

            var results = new System.Text.StringBuilder();
            results.Append("{\"mission\":\"").Append(EscapeJsonString(plan.name)).Append("\",\"steps\":[");

            bool hadFailure = false;

            for (int i = 0; i < plan.steps.Count; i++)
            {
                var step = plan.steps[i];
                if (step.status == MissionStepStatus.Completed || step.status == MissionStepStatus.Skipped)
                    continue;

                if (hadFailure && stopOnFailure)
                {
                    step.status = MissionStepStatus.Skipped;
                    continue;
                }

                step.status = MissionStepStatus.InProgress;
                string result = ExecuteMissionStep(step);

                if (result.Contains("\"error\""))
                {
                    step.status = MissionStepStatus.Failed;
                    step.result = result;
                    hadFailure = true;
                }
                else
                {
                    step.status = MissionStepStatus.Completed;
                    step.result = result;
                }

                if (results.Length > 30) results.Append(",");
                results.Append("{\"step\":").Append(i).Append(",\"status\":\"").Append(step.status.ToString()).Append("\"}");
            }

            // Check if all done
            bool allDone = true;
            foreach (var s in plan.steps)
            {
                if (s.status == MissionStepStatus.Pending || s.status == MissionStepStatus.InProgress)
                {
                    allDone = false;
                    break;
                }
            }
            if (allDone) plan.isActive = false;

            results.Append("],\"completed\":").Append(allDone ? "true" : "false").Append("}");
            return results.ToString();
        }

        private string ExecuteMissionStep(MissionStep step)
        {
            // Map step type to tool execution
            switch (step.type)
            {
                case MissionStepType.Launch:
                    return mechJebBridge.LaunchToOrbit(
                        GetStepParam(step, "altitude", 80000),
                        GetStepParam(step, "inclination", 0));

                case MissionStepType.SetOrbit:
                    // This would need more params - just do a circularize
                    return mechJebBridge.Circularize(true);

                case MissionStepType.Transfer:
                    return mechJebBridge.PlanTransfer(GetStepParamString(step, "target", "Mun"));

                case MissionStepType.Circularize:
                    return mechJebBridge.Circularize(GetStepParamBool(step, "at_apoapsis", true));

                case MissionStepType.Land:
                    double? lat = step.parameters.ContainsKey("latitude") ? GetStepParam(step, "latitude", 0) : (double?)null;
                    double? lon = step.parameters.ContainsKey("longitude") ? GetStepParam(step, "longitude", 0) : (double?)null;
                    return mechJebBridge.Land(lat, lon);

                case MissionStepType.Ascend:
                    // From surface to orbit - use launch
                    return mechJebBridge.LaunchToOrbit(GetStepParam(step, "altitude", 80000), 0);

                case MissionStepType.Rendezvous:
                    return mechJebBridge.StartRendezvous();

                case MissionStepType.Dock:
                    return mechJebBridge.StartDocking();

                case MissionStepType.WarpTo:
                    return flightController.WarpToNextNode(GetStepParam(step, "lead_time", 60));

                case MissionStepType.Recover:
                    return sceneController.RecoverVessel();

                case MissionStepType.Aerobrake:
                    return "{\"status\":\"skipped\",\"note\":\"Aerobraking is passive - just set periapsis in atmosphere\"}";

                case MissionStepType.CheckStatus:
                    return telemetryProvider.GetTelemetryJson();

                default:
                    return "{\"error\":\"Unknown step type: " + step.type.ToString() + "\"}";
            }
        }

        private double GetStepParam(MissionStep step, string key, double defaultValue)
        {
            if (step.parameters == null) return defaultValue;
            string value;
            if (step.parameters.TryGetValue(key, out value))
            {
                double result;
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result))
                    return result;
            }
            return defaultValue;
        }

        private string GetStepParamString(MissionStep step, string key, string defaultValue)
        {
            if (step.parameters == null) return defaultValue;
            string value;
            if (step.parameters.TryGetValue(key, out value))
                return value;
            return defaultValue;
        }

        private bool GetStepParamBool(MissionStep step, string key, bool defaultValue)
        {
            if (step.parameters == null) return defaultValue;
            string value;
            if (step.parameters.TryGetValue(key, out value))
            {
                if (value == "true" || value == "True") return true;
                if (value == "false" || value == "False") return false;
            }
            return defaultValue;
        }

        private static string EscapeJsonString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
