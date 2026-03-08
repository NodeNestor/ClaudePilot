using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClaudePilot
{
    public enum MissionStepType
    {
        Launch,
        SetOrbit,
        Transfer,
        Circularize,
        InclinationChange,
        Aerobrake,
        Land,
        Ascend,
        Rendezvous,
        Dock,
        Undock,
        WarpTo,
        CheckStatus,
        Recover
    }

    public enum MissionStepStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    public class MissionStep
    {
        public int index;
        public MissionStepType type;
        public MissionStepStatus status;
        public string description;
        public double estimatedDeltaV;
        public Dictionary<string, string> parameters;
        public string result;

        public MissionStep()
        {
            parameters = new Dictionary<string, string>();
            status = MissionStepStatus.Pending;
        }
    }

    public class MissionPlan
    {
        public string name;
        public string destination;
        public List<MissionStep> steps;
        public double totalDeltaVRequired;
        public int currentStepIndex;
        public bool isActive;

        public MissionPlan()
        {
            steps = new List<MissionStep>();
            currentStepIndex = 0;
            isActive = false;
        }
    }

    public class MissionPlanner
    {
        private MissionPlan activePlan;
        public MissionPlan ActivePlan => activePlan;

        // Delta-V map: stock KSP values in m/s
        // Format: from -> to -> delta-v needed (one way)
        // These are approximate values for Hohmann transfers
        private static readonly Dictionary<string, Dictionary<string, double[]>> deltaVMap = BuildDeltaVMap();

        private static Dictionary<string, Dictionary<string, double[]>> BuildDeltaVMap()
        {
            var map = new Dictionary<string, Dictionary<string, double[]>>();

            // [0] = delta-v to get there, [1] = delta-v to orbit/land there
            // Values are approximate for stock KSP

            // From Kerbin surface
            var kerbin = new Dictionary<string, double[]>();
            kerbin["Kerbin_LKO"] = new double[] { 3400, 0 };        // 80km orbit
            kerbin["Mun"] = new double[] { 3400 + 860, 310 + 580 };  // LKO + transfer + capture + land
            kerbin["Minmus"] = new double[] { 3400 + 930, 160 + 180 }; // LKO + transfer + capture + land
            kerbin["Duna"] = new double[] { 3400 + 1080, 250 + 360 }; // LKO + transfer + capture + land (aerobrake helps)
            kerbin["Ike"] = new double[] { 3400 + 1080 + 30, 180 + 390 };
            kerbin["Eve"] = new double[] { 3400 + 1033, 80 + 0 };    // Aerobrake to orbit, landing is free (atmo)
            kerbin["Gilly"] = new double[] { 3400 + 1033 + 60, 30 + 30 };
            kerbin["Jool"] = new double[] { 3400 + 1915, 0 };        // Aerobrake possible
            kerbin["Laythe"] = new double[] { 3400 + 1915, 930 + 2900 }; // Atmo landing
            kerbin["Vall"] = new double[] { 3400 + 1915 + 620, 910 + 860 };
            kerbin["Tylo"] = new double[] { 3400 + 1915 + 400, 1100 + 2270 }; // Hardest landing
            kerbin["Bop"] = new double[] { 3400 + 1915 + 220, 900 + 230 };
            kerbin["Pol"] = new double[] { 3400 + 1915 + 160, 820 + 130 };
            kerbin["Eeloo"] = new double[] { 3400 + 2100, 620 + 420 };
            kerbin["Moho"] = new double[] { 3400 + 2520, 2410 + 870 };
            kerbin["Dres"] = new double[] { 3400 + 1520, 340 + 430 };
            map["Kerbin"] = kerbin;

            // From LKO (80km Kerbin orbit)
            var lko = new Dictionary<string, double[]>();
            lko["Mun"] = new double[] { 860, 310 + 580 };
            lko["Minmus"] = new double[] { 930, 160 + 180 };
            lko["Duna"] = new double[] { 1080, 250 + 360 };
            lko["Ike"] = new double[] { 1080, 180 + 30 + 390 };
            lko["Eve"] = new double[] { 1033, 80 };
            lko["Gilly"] = new double[] { 1033, 60 + 30 + 30 };
            lko["Jool"] = new double[] { 1915, 160 };
            lko["Laythe"] = new double[] { 1915, 930 + 2900 };
            lko["Vall"] = new double[] { 1915, 620 + 910 + 860 };
            lko["Tylo"] = new double[] { 1915, 400 + 1100 + 2270 };
            lko["Bop"] = new double[] { 1915, 220 + 900 + 230 };
            lko["Pol"] = new double[] { 1915, 160 + 820 + 130 };
            lko["Eeloo"] = new double[] { 2100, 620 + 420 };
            lko["Moho"] = new double[] { 2520, 2410 + 870 };
            lko["Dres"] = new double[] { 1520, 340 + 430 };
            map["Kerbin_LKO"] = lko;

            // From Mun surface
            var mun = new Dictionary<string, double[]>();
            mun["Mun_Orbit"] = new double[] { 580, 0 };
            mun["Kerbin"] = new double[] { 580 + 310, 0 }; // Aerobrake return
            mun["Minmus"] = new double[] { 580 + 310 + 930, 160 + 180 };
            map["Mun"] = mun;

            // From Minmus surface
            var minmus = new Dictionary<string, double[]>();
            minmus["Minmus_Orbit"] = new double[] { 180, 0 };
            minmus["Kerbin"] = new double[] { 180 + 160, 0 }; // Aerobrake return
            map["Minmus"] = minmus;

            // From Duna surface
            var duna = new Dictionary<string, double[]>();
            duna["Duna_Orbit"] = new double[] { 1450, 0 };
            duna["Kerbin"] = new double[] { 1450 + 610, 0 }; // Aerobrake return
            duna["Ike"] = new double[] { 1450 + 30, 180 + 390 };
            map["Duna"] = duna;

            return map;
        }

        // Get delta-v requirements for a mission
        public string GetDeltaVMap(string from, string to)
        {
            try
            {
                if (deltaVMap.ContainsKey(from) && deltaVMap[from].ContainsKey(to))
                {
                    var dv = deltaVMap[from][to];
                    double total = dv[0] + dv[1];
                    return "{\"from\":\"" + EscapeJson(from) + "\""
                        + ",\"to\":\"" + EscapeJson(to) + "\""
                        + ",\"transferDeltaV\":" + dv[0]
                        + ",\"arrivalDeltaV\":" + dv[1]
                        + ",\"totalOneWay\":" + total
                        + ",\"totalRoundTrip\":" + (total * 2)
                        + ",\"note\":\"Approximate values. Aerobraking can save significant dv at bodies with atmospheres.\""
                        + "}";
                }

                // Try to find any route
                return "{\"error\":\"No delta-v data for route " + EscapeJson(from) + " -> " + EscapeJson(to) + "\""
                    + ",\"available_origins\":[" + GetAvailableOrigins() + "]"
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Get full delta-v map as JSON
        public string GetFullDeltaVMap()
        {
            try
            {
                string json = "{";
                bool firstOrigin = true;
                foreach (var origin in deltaVMap)
                {
                    if (!firstOrigin) json += ",";
                    firstOrigin = false;
                    json += "\"" + EscapeJson(origin.Key) + "\":{";

                    bool firstDest = true;
                    foreach (var dest in origin.Value)
                    {
                        if (!firstDest) json += ",";
                        firstDest = false;
                        json += "\"" + EscapeJson(dest.Key) + "\":{\"transfer\":" + dest.Value[0]
                            + ",\"arrival\":" + dest.Value[1]
                            + ",\"total\":" + (dest.Value[0] + dest.Value[1]) + "}";
                    }
                    json += "}";
                }
                json += "}";
                return json;
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Create a new mission plan
        public string CreateMissionPlan(string name, string destination, string stepsJson)
        {
            try
            {
                activePlan = new MissionPlan();
                activePlan.name = name;
                activePlan.destination = destination;
                activePlan.isActive = true;
                activePlan.currentStepIndex = 0;

                // Parse steps from JSON array
                // Expected format: [{"type":"Launch","description":"...","deltaV":3400,"params":{"altitude":"80000"}}, ...]
                var steps = ParseStepsJson(stepsJson);
                activePlan.steps = steps;

                double totalDv = 0;
                foreach (var step in steps)
                    totalDv += step.estimatedDeltaV;
                activePlan.totalDeltaVRequired = totalDv;

                return "{\"status\":\"plan_created\""
                    + ",\"name\":\"" + EscapeJson(name) + "\""
                    + ",\"destination\":\"" + EscapeJson(destination) + "\""
                    + ",\"steps\":" + steps.Count
                    + ",\"totalDeltaVRequired\":" + totalDv
                    + ",\"plan\":" + GetPlanJson()
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Get current mission plan status
        public string GetMissionPlan()
        {
            if (activePlan == null)
                return "{\"status\":\"no_active_plan\"}";

            return "{\"name\":\"" + EscapeJson(activePlan.name) + "\""
                + ",\"destination\":\"" + EscapeJson(activePlan.destination) + "\""
                + ",\"isActive\":" + (activePlan.isActive ? "true" : "false")
                + ",\"currentStep\":" + activePlan.currentStepIndex
                + ",\"totalSteps\":" + activePlan.steps.Count
                + ",\"totalDeltaVRequired\":" + activePlan.totalDeltaVRequired
                + ",\"plan\":" + GetPlanJson()
                + "}";
        }

        // Advance to the next step / mark current step status
        public string UpdateMissionStep(int stepIndex, string status, string result)
        {
            if (activePlan == null)
                return "{\"error\":\"No active mission plan\"}";
            if (stepIndex < 0 || stepIndex >= activePlan.steps.Count)
                return "{\"error\":\"Invalid step index\"}";

            var step = activePlan.steps[stepIndex];

            MissionStepStatus newStatus;
            switch (status.ToLower())
            {
                case "in_progress": newStatus = MissionStepStatus.InProgress; break;
                case "completed": newStatus = MissionStepStatus.Completed; break;
                case "failed": newStatus = MissionStepStatus.Failed; break;
                case "skipped": newStatus = MissionStepStatus.Skipped; break;
                default: newStatus = MissionStepStatus.Pending; break;
            }

            step.status = newStatus;
            if (result != null) step.result = result;

            // Auto-advance current step index
            if (newStatus == MissionStepStatus.Completed || newStatus == MissionStepStatus.Skipped)
            {
                if (stepIndex == activePlan.currentStepIndex &&
                    activePlan.currentStepIndex < activePlan.steps.Count - 1)
                {
                    activePlan.currentStepIndex++;
                }
            }

            // Check if mission is complete
            bool allDone = true;
            foreach (var s in activePlan.steps)
            {
                if (s.status == MissionStepStatus.Pending || s.status == MissionStepStatus.InProgress)
                {
                    allDone = false;
                    break;
                }
            }
            if (allDone) activePlan.isActive = false;

            return "{\"status\":\"updated\""
                + ",\"stepIndex\":" + stepIndex
                + ",\"stepStatus\":\"" + newStatus.ToString() + "\""
                + ",\"currentStep\":" + activePlan.currentStepIndex
                + ",\"missionComplete\":" + (!activePlan.isActive ? "true" : "false")
                + "}";
        }

        // Get the next pending step
        public string GetNextStep()
        {
            if (activePlan == null)
                return "{\"error\":\"No active mission plan\"}";

            for (int i = activePlan.currentStepIndex; i < activePlan.steps.Count; i++)
            {
                var step = activePlan.steps[i];
                if (step.status == MissionStepStatus.Pending)
                {
                    return StepToJson(step);
                }
            }
            return "{\"status\":\"all_steps_complete\"}";
        }

        // Cancel/clear the active plan
        public string CancelMissionPlan()
        {
            if (activePlan == null)
                return "{\"status\":\"no_active_plan\"}";

            string name = activePlan.name;
            activePlan = null;
            return "{\"status\":\"cancelled\",\"name\":\"" + EscapeJson(name) + "\"}";
        }

        // Suggest mission steps for a destination (helper for Claude)
        public string SuggestMissionSteps(string destination, bool roundTrip)
        {
            try
            {
                var steps = new List<string>();
                double totalDv = 0;

                // Get delta-v data
                double launchDv = 3400;
                double transferDv = 0;
                double arrivalDv = 0;

                if (deltaVMap.ContainsKey("Kerbin_LKO") && deltaVMap["Kerbin_LKO"].ContainsKey(destination))
                {
                    var dv = deltaVMap["Kerbin_LKO"][destination];
                    transferDv = dv[0];
                    arrivalDv = dv[1];
                }

                // Build suggested steps
                int idx = 0;
                steps.Add(StepSuggestion(idx++, "Launch", "Launch to 80km Kerbin orbit", launchDv, "altitude", "80000"));
                totalDv += launchDv;

                steps.Add(StepSuggestion(idx++, "Transfer", "Hohmann transfer to " + destination, transferDv, "target", destination));
                totalDv += transferDv;

                // Check if destination has atmosphere (for aerobrake)
                bool hasAtmo = destination == "Eve" || destination == "Duna" || destination == "Laythe" || destination == "Jool" || destination == "Kerbin";

                if (hasAtmo && arrivalDv > 500)
                {
                    steps.Add(StepSuggestion(idx++, "Aerobrake", "Aerobrake into " + destination + " orbit (saves dv)", 0, "target", destination));
                    steps.Add(StepSuggestion(idx++, "Circularize", "Circularize orbit around " + destination, arrivalDv * 0.3, "target", destination));
                    totalDv += arrivalDv * 0.3;
                }
                else
                {
                    steps.Add(StepSuggestion(idx++, "Circularize", "Capture and circularize at " + destination, arrivalDv * 0.5, "target", destination));
                    totalDv += arrivalDv * 0.5;
                }

                steps.Add(StepSuggestion(idx++, "Land", "Land on " + destination, arrivalDv * 0.5, "target", destination));
                totalDv += arrivalDv * 0.5;

                if (roundTrip)
                {
                    // Return journey
                    double returnLaunchDv = arrivalDv; // Roughly same to get back to orbit
                    double returnTransferDv = transferDv * 0.9; // Roughly similar

                    steps.Add(StepSuggestion(idx++, "Ascend", "Launch from " + destination + " surface to orbit", returnLaunchDv, "target", destination));
                    totalDv += returnLaunchDv;

                    steps.Add(StepSuggestion(idx++, "Transfer", "Transfer back to Kerbin", returnTransferDv, "target", "Kerbin"));
                    totalDv += returnTransferDv;

                    steps.Add(StepSuggestion(idx++, "Aerobrake", "Aerobrake into Kerbin atmosphere", 0, "target", "Kerbin"));
                    steps.Add(StepSuggestion(idx++, "Land", "Land/recover at Kerbin", 0, "target", "Kerbin"));
                    steps.Add(StepSuggestion(idx++, "Recover", "Recover vessel", 0, "", ""));
                }

                string stepsArray = "[" + string.Join(",", steps.ToArray()) + "]";

                return "{\"destination\":\"" + EscapeJson(destination) + "\""
                    + ",\"roundTrip\":" + (roundTrip ? "true" : "false")
                    + ",\"totalDeltaVRequired\":" + Math.Round(totalDv)
                    + ",\"safetyMargin\":" + Math.Round(totalDv * 1.15) // 15% safety margin
                    + ",\"steps\":" + stepsArray
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // --- Private helpers ---

        private string GetPlanJson()
        {
            string json = "[";
            for (int i = 0; i < activePlan.steps.Count; i++)
            {
                if (i > 0) json += ",";
                json += StepToJson(activePlan.steps[i]);
            }
            json += "]";
            return json;
        }

        private string StepToJson(MissionStep step)
        {
            string paramsJson = "{";
            bool first = true;
            foreach (var kvp in step.parameters)
            {
                if (!first) paramsJson += ",";
                first = false;
                paramsJson += "\"" + EscapeJson(kvp.Key) + "\":\"" + EscapeJson(kvp.Value) + "\"";
            }
            paramsJson += "}";

            return "{\"index\":" + step.index
                + ",\"type\":\"" + step.type.ToString() + "\""
                + ",\"status\":\"" + step.status.ToString() + "\""
                + ",\"description\":\"" + EscapeJson(step.description) + "\""
                + ",\"estimatedDeltaV\":" + step.estimatedDeltaV
                + ",\"parameters\":" + paramsJson
                + (step.result != null ? ",\"result\":\"" + EscapeJson(step.result) + "\"" : "")
                + "}";
        }

        private string StepSuggestion(int index, string type, string description, double deltaV, string paramKey, string paramValue)
        {
            string paramsJson = "{}";
            if (!string.IsNullOrEmpty(paramKey))
                paramsJson = "{\"" + EscapeJson(paramKey) + "\":\"" + EscapeJson(paramValue) + "\"}";

            return "{\"index\":" + index
                + ",\"type\":\"" + type + "\""
                + ",\"description\":\"" + EscapeJson(description) + "\""
                + ",\"estimatedDeltaV\":" + Math.Round(deltaV)
                + ",\"parameters\":" + paramsJson
                + "}";
        }

        private List<MissionStep> ParseStepsJson(string stepsJson)
        {
            var steps = new List<MissionStep>();
            if (string.IsNullOrEmpty(stepsJson)) return steps;

            // Simple parser: split by },{ pattern
            string trimmed = stepsJson.Trim();
            if (trimmed.StartsWith("[")) trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("]")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

            // Split on },{ boundaries
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '{') depth++;
                else if (trimmed[i] == '}') depth--;
                else if (trimmed[i] == ',' && depth == 0)
                {
                    parts.Add(trimmed.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < trimmed.Length)
                parts.Add(trimmed.Substring(start).Trim());

            int idx = 0;
            foreach (string part in parts)
            {
                var step = new MissionStep();
                step.index = idx++;

                string typeStr = MiniJson.ExtractString(part, "type");
                step.type = ParseStepType(typeStr);
                step.description = MiniJson.ExtractString(part, "description") ?? "";

                string dvStr = MiniJson.ExtractString(part, "deltaV");
                if (dvStr == null) dvStr = MiniJson.ExtractString(part, "estimatedDeltaV");
                double.TryParse(dvStr, out step.estimatedDeltaV);

                steps.Add(step);
            }

            return steps;
        }

        private MissionStepType ParseStepType(string type)
        {
            if (type == null) return MissionStepType.CheckStatus;
            switch (type.ToLower())
            {
                case "launch": return MissionStepType.Launch;
                case "setorbit": case "set_orbit": return MissionStepType.SetOrbit;
                case "transfer": return MissionStepType.Transfer;
                case "circularize": return MissionStepType.Circularize;
                case "inclinationchange": case "inclination_change": return MissionStepType.InclinationChange;
                case "aerobrake": return MissionStepType.Aerobrake;
                case "land": return MissionStepType.Land;
                case "ascend": return MissionStepType.Ascend;
                case "rendezvous": return MissionStepType.Rendezvous;
                case "dock": return MissionStepType.Dock;
                case "undock": return MissionStepType.Undock;
                case "warpto": case "warp_to": return MissionStepType.WarpTo;
                case "checkstatus": case "check_status": return MissionStepType.CheckStatus;
                case "recover": return MissionStepType.Recover;
                default: return MissionStepType.CheckStatus;
            }
        }

        private string GetAvailableOrigins()
        {
            string result = "";
            bool first = true;
            foreach (var key in deltaVMap.Keys)
            {
                if (!first) result += ",";
                first = false;
                result += "\"" + EscapeJson(key) + "\"";
            }
            return result;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
