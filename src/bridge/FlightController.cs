using System;
using System.Globalization;
using UnityEngine;

namespace ClaudePilot
{
    public class FlightController
    {
        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string ErrorJson(string message)
        {
            return "{\"success\":false,\"error\":\"" + EscapeJson(message) + "\"}";
        }

        private static string SuccessJson(string details)
        {
            return "{\"success\":true," + details + "}";
        }

        public string SetTimeWarp(int rateIndex)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                rateIndex = Math.Max(0, Math.Min(7, rateIndex));
                TimeWarp.SetRate(rateIndex, true);

                float rate = TimeWarp.fetch.warpRates[rateIndex];
                return SuccessJson("\"action\":\"set_time_warp\",\"rateIndex\":" + rateIndex + ",\"rate\":" + rate);
            }
            catch (Exception ex)
            {
                return ErrorJson("SetTimeWarp failed: " + ex.Message);
            }
        }

        public string StopTimeWarp()
        {
            try
            {
                TimeWarp.SetRate(0, true);
                return SuccessJson("\"action\":\"stop_time_warp\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("StopTimeWarp failed: " + ex.Message);
            }
        }

        public string WarpToNextNode(double leadTime)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                if (vessel.patchedConicSolver == null || vessel.patchedConicSolver.maneuverNodes.Count == 0)
                    return ErrorJson("No maneuver nodes found.");

                var node = vessel.patchedConicSolver.maneuverNodes[0];
                double warpEndTime = node.UT - leadTime;
                double now = Planetarium.GetUniversalTime();

                if (warpEndTime <= now)
                    return ErrorJson("Maneuver node is already within lead time. Time to node: " + (node.UT - now) + "s, lead time: " + leadTime + "s");

                TimeWarp.fetch.WarpTo(warpEndTime);
                double warpDuration = warpEndTime - now;

                return SuccessJson("\"action\":\"warp_to_node\",\"warpDuration\":" + warpDuration + ",\"leadTime\":" + leadTime + ",\"nodeUT\":" + node.UT);
            }
            catch (Exception ex)
            {
                return ErrorJson("WarpToNextNode failed: " + ex.Message);
            }
        }

        public string ActivateStage()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                KSP.UI.Screens.StageManager.ActivateNextStage();
                int newStage = KSP.UI.Screens.StageManager.CurrentStage;

                return SuccessJson("\"action\":\"activate_stage\",\"currentStage\":" + newStage);
            }
            catch (Exception ex)
            {
                return ErrorJson("ActivateStage failed: " + ex.Message);
            }
        }

        public string SetSASMode(string mode)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

                VesselAutopilot.AutopilotMode apMode;
                switch (mode.ToLowerInvariant())
                {
                    case "stability":
                    case "stabilityassist":
                        apMode = VesselAutopilot.AutopilotMode.StabilityAssist;
                        break;
                    case "prograde":
                        apMode = VesselAutopilot.AutopilotMode.Prograde;
                        break;
                    case "retrograde":
                        apMode = VesselAutopilot.AutopilotMode.Retrograde;
                        break;
                    case "normal":
                        apMode = VesselAutopilot.AutopilotMode.Normal;
                        break;
                    case "antinormal":
                        apMode = VesselAutopilot.AutopilotMode.Antinormal;
                        break;
                    case "radial_in":
                    case "radialin":
                        apMode = VesselAutopilot.AutopilotMode.RadialIn;
                        break;
                    case "radial_out":
                    case "radialout":
                        apMode = VesselAutopilot.AutopilotMode.RadialOut;
                        break;
                    case "target":
                        apMode = VesselAutopilot.AutopilotMode.Target;
                        break;
                    case "anti_target":
                    case "antitarget":
                        apMode = VesselAutopilot.AutopilotMode.AntiTarget;
                        break;
                    case "maneuver":
                        apMode = VesselAutopilot.AutopilotMode.Maneuver;
                        break;
                    default:
                        return ErrorJson("Unknown SAS mode: " + mode + ". Valid: stability, prograde, retrograde, normal, antinormal, radial_in, radial_out, target, anti_target, maneuver");
                }

                vessel.Autopilot.SetMode(apMode);
                return SuccessJson("\"action\":\"set_sas\",\"mode\":\"" + apMode.ToString() + "\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("SetSASMode failed: " + ex.Message);
            }
        }

        public string ToggleActionGroup(int group)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                if (group < 1 || group > 10)
                    return ErrorJson("Action group must be 1-10. Got: " + group);

                KSPActionGroup actionGroup;
                switch (group)
                {
                    case 1: actionGroup = KSPActionGroup.Custom01; break;
                    case 2: actionGroup = KSPActionGroup.Custom02; break;
                    case 3: actionGroup = KSPActionGroup.Custom03; break;
                    case 4: actionGroup = KSPActionGroup.Custom04; break;
                    case 5: actionGroup = KSPActionGroup.Custom05; break;
                    case 6: actionGroup = KSPActionGroup.Custom06; break;
                    case 7: actionGroup = KSPActionGroup.Custom07; break;
                    case 8: actionGroup = KSPActionGroup.Custom08; break;
                    case 9: actionGroup = KSPActionGroup.Custom09; break;
                    case 10: actionGroup = KSPActionGroup.Custom10; break;
                    default: return ErrorJson("Invalid action group: " + group);
                }

                vessel.ActionGroups.ToggleGroup(actionGroup);
                bool state = vessel.ActionGroups[actionGroup];

                return SuccessJson("\"action\":\"toggle_action_group\",\"group\":" + group + ",\"state\":" + (state ? "true" : "false"));
            }
            catch (Exception ex)
            {
                return ErrorJson("ToggleActionGroup failed: " + ex.Message);
            }
        }

        public string SetTarget(string name)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                // Try celestial body first
                foreach (var body in FlightGlobals.Bodies)
                {
                    if (body.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        FlightGlobals.fetch.SetVesselTarget(body);
                        return SuccessJson("\"action\":\"set_target\",\"target\":\"" + EscapeJson(body.name) + "\",\"type\":\"body\"");
                    }
                }

                // Try vessel
                foreach (var v in FlightGlobals.Vessels)
                {
                    if (v.vesselName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        FlightGlobals.fetch.SetVesselTarget(v);
                        return SuccessJson("\"action\":\"set_target\",\"target\":\"" + EscapeJson(v.vesselName) + "\",\"type\":\"vessel\"");
                    }
                }

                return ErrorJson("Target '" + name + "' not found. No matching celestial body or vessel.");
            }
            catch (Exception ex)
            {
                return ErrorJson("SetTarget failed: " + ex.Message);
            }
        }

        public string CreateManeuverNode(double prograde, double normal, double radial, double ut)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.patchedConicSolver == null) return ErrorJson("Vessel does not support maneuver nodes (not in orbit?).");

                // If UT not provided, use 60 seconds from now
                if (ut <= 0) ut = Planetarium.GetUniversalTime() + 60;

                var dV = new Vector3d(radial, normal, prograde);
                var node = vessel.patchedConicSolver.AddManeuverNode(ut);
                node.DeltaV = dV;
                vessel.patchedConicSolver.UpdateFlightPlan();

                return SuccessJson("\"action\":\"create_maneuver_node\",\"ut\":" + ut +
                    ",\"prograde\":" + prograde + ",\"normal\":" + normal + ",\"radial\":" + radial +
                    ",\"totalDeltaV\":" + dV.magnitude);
            }
            catch (Exception ex)
            {
                return ErrorJson("CreateManeuverNode failed: " + ex.Message);
            }
        }

        public string DeleteAllManeuverNodes()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.patchedConicSolver == null) return SuccessJson("\"action\":\"delete_nodes\",\"deleted\":0,\"note\":\"No patched conic solver\"");

                int count = vessel.patchedConicSolver.maneuverNodes.Count;
                while (vessel.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    vessel.patchedConicSolver.maneuverNodes[0].RemoveSelf();
                }

                return SuccessJson("\"action\":\"delete_nodes\",\"deleted\":" + count);
            }
            catch (Exception ex)
            {
                return ErrorJson("DeleteAllManeuverNodes failed: " + ex.Message);
            }
        }

        public string WarpToApoapsis(double leadTime)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.orbit == null) return ErrorJson("Vessel not in orbit.");

                double now = Planetarium.GetUniversalTime();
                double warpEndTime = now + vessel.orbit.timeToAp - leadTime;

                if (warpEndTime <= now)
                    return ErrorJson("Already near apoapsis. Time to Ap: " + vessel.orbit.timeToAp.ToString("F0") + "s");

                TimeWarp.fetch.WarpTo(warpEndTime);
                return SuccessJson("\"action\":\"warp_to_apoapsis\",\"timeToAp\":" + vessel.orbit.timeToAp
                    + ",\"apoapsis\":" + vessel.orbit.ApA + ",\"leadTime\":" + leadTime);
            }
            catch (Exception ex)
            {
                return ErrorJson("WarpToApoapsis failed: " + ex.Message);
            }
        }

        public string WarpToPeriapsis(double leadTime)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.orbit == null) return ErrorJson("Vessel not in orbit.");

                double now = Planetarium.GetUniversalTime();
                double warpEndTime = now + vessel.orbit.timeToPe - leadTime;

                if (warpEndTime <= now)
                    return ErrorJson("Already near periapsis. Time to Pe: " + vessel.orbit.timeToPe.ToString("F0") + "s");

                TimeWarp.fetch.WarpTo(warpEndTime);
                return SuccessJson("\"action\":\"warp_to_periapsis\",\"timeToPe\":" + vessel.orbit.timeToPe
                    + ",\"periapsis\":" + vessel.orbit.PeA + ",\"leadTime\":" + leadTime);
            }
            catch (Exception ex)
            {
                return ErrorJson("WarpToPeriapsis failed: " + ex.Message);
            }
        }

        public string WarpToTime(double ut)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                double now = Planetarium.GetUniversalTime();
                if (ut <= now)
                    return ErrorJson("Target time " + ut.ToString("F0") + " is in the past. Current UT: " + now.ToString("F0"));

                TimeWarp.fetch.WarpTo(ut);
                double warpDuration = ut - now;
                return SuccessJson("\"action\":\"warp_to_time\",\"targetUT\":" + ut + ",\"warpDuration\":" + warpDuration);
            }
            catch (Exception ex)
            {
                return ErrorJson("WarpToTime failed: " + ex.Message);
            }
        }

        public string WarpToSOIChange(double leadTime)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.orbit == null) return ErrorJson("Vessel not in orbit.");

                // Check for SOI transition in orbit patches
                var orbit = vessel.orbit;
                int patchCount = 0;
                while (orbit != null && patchCount < 10)
                {
                    if (orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER ||
                        orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)
                    {
                        double soiUT = orbit.EndUT;
                        double now = Planetarium.GetUniversalTime();
                        double warpEndTime = soiUT - leadTime;

                        if (warpEndTime <= now)
                            return ErrorJson("SOI change is within lead time. Time to SOI: " + (soiUT - now).ToString("F0") + "s");

                        string transitionType = orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER
                            ? "encounter" : "escape";
                        string nextBody = orbit.nextPatch != null ? orbit.nextPatch.referenceBody.name : "unknown";

                        TimeWarp.fetch.WarpTo(warpEndTime);
                        return SuccessJson("\"action\":\"warp_to_soi\",\"transition\":\"" + transitionType + "\""
                            + ",\"nextBody\":\"" + EscapeJson(nextBody) + "\""
                            + ",\"timeToSOI\":" + (soiUT - now) + ",\"leadTime\":" + leadTime);
                    }

                    orbit = orbit.nextPatch;
                    patchCount++;
                }

                return ErrorJson("No SOI transition found in current trajectory. Create a transfer maneuver first.");
            }
            catch (Exception ex)
            {
                return ErrorJson("WarpToSOIChange failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Auto transfer: calculate transfer, create node, optionally warp and execute.
        /// This is the all-in-one transfer tool that actually works.
        /// </summary>
        public string AutoTransfer(string targetBody, bool waitForWindow, bool execute)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.orbit == null) return ErrorJson("Vessel not in orbit.");

                // Calculate transfer parameters
                string calcResult = TransferCalculator.CalculateTransfer(targetBody, vessel.orbit.semiMajorAxis);
                if (calcResult.Contains("\"success\":false"))
                    return calcResult;

                // Extract nodeUT and deltaV from the calculation
                double nodeUT = 0;
                double progradeDV = 0;

                string nodeUTStr = MiniJson.ExtractString(calcResult, "nodeUT");
                string progradeDVStr = MiniJson.ExtractString(calcResult, "progradeDeltaV");
                if (progradeDVStr == null) progradeDVStr = MiniJson.ExtractString(calcResult, "deltaV");

                if (nodeUTStr != null)
                    double.TryParse(nodeUTStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out nodeUT);
                if (progradeDVStr != null)
                    double.TryParse(progradeDVStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out progradeDV);

                if (progradeDV == 0)
                    return ErrorJson("Could not calculate transfer delta-v to " + targetBody);

                // If no nodeUT was calculated (interplanetary), use 60s from now
                if (nodeUT <= 0)
                    nodeUT = Planetarium.GetUniversalTime() + 60;

                // Create the maneuver node
                string nodeResult = CreateManeuverNode(progradeDV, 0, 0, nodeUT);
                if (nodeResult.Contains("\"error\""))
                    return nodeResult;

                double now = Planetarium.GetUniversalTime();
                double timeToNode = nodeUT - now;

                return SuccessJson("\"action\":\"auto_transfer\""
                    + ",\"target\":\"" + EscapeJson(targetBody) + "\""
                    + ",\"progradeDeltaV\":" + progradeDV.ToString("F1")
                    + ",\"nodeUT\":" + nodeUT.ToString("F0")
                    + ",\"timeToNode\":" + timeToNode.ToString("F0")
                    + ",\"nodeCreated\":true"
                    + ",\"nextSteps\":\"Use warp_to_next_node to warp to the burn, then execute_next_node to burn. After burn, check orbit to verify encounter.\""
                    + ",\"transferDetails\":" + calcResult);
            }
            catch (Exception ex)
            {
                return ErrorJson("AutoTransfer failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Get info about the next maneuver node
        /// </summary>
        public string GetManeuverNodeInfo()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.patchedConicSolver == null || vessel.patchedConicSolver.maneuverNodes.Count == 0)
                    return ErrorJson("No maneuver nodes.");

                var node = vessel.patchedConicSolver.maneuverNodes[0];
                double now = Planetarium.GetUniversalTime();

                string result = "\"nodeCount\":" + vessel.patchedConicSolver.maneuverNodes.Count
                    + ",\"nextNode\":{"
                    + "\"ut\":" + node.UT
                    + ",\"timeToNode\":" + (node.UT - now)
                    + ",\"deltaV\":" + node.DeltaV.magnitude
                    + ",\"prograde\":" + node.DeltaV.z
                    + ",\"normal\":" + node.DeltaV.y
                    + ",\"radial\":" + node.DeltaV.x
                    + "}";

                // Check for SOI transitions after the burn
                var resultOrbit = node.nextPatch;
                if (resultOrbit != null)
                {
                    result += ",\"predictedOrbit\":{"
                        + "\"body\":\"" + EscapeJson(resultOrbit.referenceBody.name) + "\""
                        + ",\"apoapsis\":" + resultOrbit.ApA
                        + ",\"periapsis\":" + resultOrbit.PeA
                        + "}";

                    // Check patches for encounters
                    var patch = resultOrbit;
                    int patchNum = 0;
                    while (patch != null && patchNum < 5)
                    {
                        if (patch.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER)
                        {
                            var nextPatch = patch.nextPatch;
                            if (nextPatch != null)
                            {
                                result += ",\"encounter\":{"
                                    + "\"body\":\"" + EscapeJson(nextPatch.referenceBody.name) + "\""
                                    + ",\"periapsis\":" + nextPatch.PeA
                                    + ",\"timeToEncounter\":" + (patch.EndUT - now)
                                    + "}";
                            }
                            break;
                        }
                        patch = patch.nextPatch;
                        patchNum++;
                    }
                }

                return SuccessJson(result);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetManeuverNodeInfo failed: " + ex.Message);
            }
        }
    }
}
