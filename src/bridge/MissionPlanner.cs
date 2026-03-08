using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ─── Dynamic Delta-V Calculations ───
        // All values computed from FlightGlobals.Bodies so modded solar systems (RSS, OPM, etc.) work automatically.

        /// <summary>
        /// Estimate delta-v to reach low orbit from the surface of a body.
        /// Uses the rocket equation approximation: dv ≈ √(μ/r) * factor + drag losses.
        /// </summary>
        private static double EstimateLaunchDv(CelestialBody body)
        {
            double mu = body.gravParameter;
            double r = body.Radius;
            double orbitalV = Math.Sqrt(mu / (r + SafeLowOrbitAlt(body)));
            double surfaceG = mu / (r * r);
            double gravityLoss = surfaceG * 120; // ~120s of gravity loss

            double dragLoss = 0;
            if (body.atmosphere)
                dragLoss = body.atmosphereDepth * 0.6; // rough drag penalty

            return orbitalV + gravityLoss + dragLoss;
        }

        /// <summary>
        /// Estimate delta-v to land from low orbit (reverse of launch minus aerobrake savings).
        /// </summary>
        private static double EstimateLandingDv(CelestialBody body)
        {
            if (body.atmosphere)
                return 0; // Aerobraking handles it
            double mu = body.gravParameter;
            double r = body.Radius;
            return Math.Sqrt(mu / (r + SafeLowOrbitAlt(body)));
        }

        /// <summary>
        /// Hohmann transfer delta-v from circular orbit r1 to circular orbit r2 around the same parent.
        /// Returns [ejection_dv, insertion_dv].
        /// </summary>
        private static double[] HohmannDv(double mu, double r1, double r2)
        {
            double a = (r1 + r2) / 2.0;
            double v1 = Math.Sqrt(mu / r1);
            double vt1 = Math.Sqrt(mu * (2.0 / r1 - 1.0 / a));
            double v2 = Math.Sqrt(mu / r2);
            double vt2 = Math.Sqrt(mu * (2.0 / r2 - 1.0 / a));
            return new double[] { Math.Abs(vt1 - v1), Math.Abs(v2 - vt2) };
        }

        /// <summary>
        /// Safe low orbit altitude: above atmosphere if present, otherwise ~10% of radius (min 10km).
        /// </summary>
        private static double SafeLowOrbitAlt(CelestialBody body)
        {
            if (body.atmosphere)
                return body.atmosphereDepth + 10000;
            return Math.Max(10000, body.Radius * 0.1);
        }

        /// <summary>
        /// Find a body by name from FlightGlobals.Bodies.
        /// </summary>
        private static CelestialBody FindBody(string name)
        {
            if (FlightGlobals.Bodies == null) return null;
            foreach (var b in FlightGlobals.Bodies)
            {
                if (string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase))
                    return b;
            }
            return null;
        }

        /// <summary>
        /// Find the "home" body (Kerbin in stock, Earth in RSS, etc.)
        /// </summary>
        private static CelestialBody GetHomeBody()
        {
            return FlightGlobals.GetHomeBody();
        }

        /// <summary>
        /// Calculate transfer delta-v between two bodies dynamically.
        /// Returns {transferDv, arrivalDv, launchDv (if from surface)}.
        /// </summary>
        private static double[] CalculateRouteDv(CelestialBody from, CelestialBody to, bool fromSurface)
        {
            double launchDv = 0;
            if (fromSurface)
                launchDv = EstimateLaunchDv(from);

            double transferDv = 0;
            double arrivalDv = 0;

            // Same body — just orbit
            if (from == to)
                return new double[] { launchDv, 0, 0 };

            // Moon of same planet (e.g., Mun -> Minmus)
            if (from.orbit != null && to.orbit != null &&
                from.orbit.referenceBody == to.orbit.referenceBody &&
                from.orbit.referenceBody != null)
            {
                var parent = from.orbit.referenceBody;
                double mu = parent.gravParameter;
                double r1 = from.orbit.semiMajorAxis;
                double r2 = to.orbit.semiMajorAxis;
                var hoh = HohmannDv(mu, r1, r2);
                transferDv = hoh[0];
                arrivalDv = hoh[1] + EstimateLandingDv(to);
                return new double[] { launchDv, transferDv, arrivalDv };
            }

            // Planet to own moon (e.g., Kerbin -> Mun)
            if (to.orbit != null && to.orbit.referenceBody == from)
            {
                double mu = from.gravParameter;
                double r1 = SafeLowOrbitAlt(from) + from.Radius;
                double r2 = to.orbit.semiMajorAxis;
                var hoh = HohmannDv(mu, r1, r2);
                transferDv = hoh[0];
                arrivalDv = hoh[1] + EstimateLandingDv(to);
                return new double[] { launchDv, transferDv, arrivalDv };
            }

            // Moon back to parent planet (e.g., Mun -> Kerbin)
            if (from.orbit != null && from.orbit.referenceBody != null &&
                to == from.orbit.referenceBody)
            {
                double mu = to.gravParameter;
                double r1 = from.orbit.semiMajorAxis;
                double r2 = SafeLowOrbitAlt(to) + to.Radius;
                var hoh = HohmannDv(mu, r1, r2);
                transferDv = hoh[0];
                arrivalDv = to.atmosphere ? 0 : hoh[1]; // Aerobrake if atmosphere
                return new double[] { launchDv, transferDv, arrivalDv };
            }

            // Interplanetary: both orbit the same star
            CelestialBody fromParent = from.orbit != null ? from.orbit.referenceBody : null;
            CelestialBody toParent = to.orbit != null ? to.orbit.referenceBody : null;

            // If from is a moon, use its planet for the interplanetary leg
            CelestialBody fromPlanet = from;
            if (fromParent != null && fromParent.orbit != null && fromParent.orbit.referenceBody != null)
            {
                // from is a moon — eject from moon first
                fromPlanet = fromParent;
            }

            // If to is a moon, target its planet first
            CelestialBody toPlanet = to;
            if (toParent != null && toParent.orbit != null && toParent.orbit.referenceBody != null &&
                toParent != fromPlanet.orbit?.referenceBody)
            {
                toPlanet = toParent;
            }

            // Get the star (common parent)
            CelestialBody star = null;
            if (fromPlanet.orbit != null)
            {
                star = fromPlanet.orbit.referenceBody;
                if (star != null && star.orbit != null && star.orbit.referenceBody != null)
                    star = star.orbit.referenceBody; // go up if needed
            }

            if (star != null && fromPlanet.orbit != null && toPlanet.orbit != null &&
                fromPlanet.orbit.referenceBody == star && toPlanet.orbit.referenceBody == star)
            {
                // Interplanetary Hohmann
                double mu = star.gravParameter;
                double r1 = fromPlanet.orbit.semiMajorAxis;
                double r2 = toPlanet.orbit.semiMajorAxis;
                var hoh = HohmannDv(mu, r1, r2);

                // Ejection from from-planet's SOI
                if (fromPlanet.gravParameter > 0)
                {
                    double parkingR = SafeLowOrbitAlt(fromPlanet) + fromPlanet.Radius;
                    double vInf = hoh[0];
                    double vPark = Math.Sqrt(fromPlanet.gravParameter / parkingR);
                    double vEject = Math.Sqrt(vInf * vInf + 2 * fromPlanet.gravParameter / parkingR);
                    transferDv = vEject - vPark;
                }
                else
                {
                    transferDv = hoh[0];
                }

                // Insertion at target planet
                if (toPlanet.gravParameter > 0)
                {
                    double captureR = SafeLowOrbitAlt(toPlanet) + toPlanet.Radius;
                    double vInf = hoh[1];
                    double vCapture = Math.Sqrt(vInf * vInf + 2 * toPlanet.gravParameter / captureR);
                    double vOrbit = Math.Sqrt(toPlanet.gravParameter / captureR);
                    arrivalDv = vCapture - vOrbit;
                    if (toPlanet.atmosphere)
                        arrivalDv = 0; // Can aerobrake
                }
                else
                {
                    arrivalDv = hoh[1];
                }

                // If target is a moon of the target planet, add moon insertion
                if (to != toPlanet)
                {
                    arrivalDv += EstimateLandingDv(to);
                    if (to.orbit != null)
                    {
                        double moonMu = toPlanet.gravParameter;
                        double moonR = to.orbit.semiMajorAxis;
                        double captureR = SafeLowOrbitAlt(toPlanet) + toPlanet.Radius;
                        var moonHoh = HohmannDv(moonMu, captureR, moonR);
                        arrivalDv += moonHoh[0] + moonHoh[1];
                    }
                }
                else
                {
                    arrivalDv += EstimateLandingDv(to);
                }

                return new double[] { launchDv, transferDv, arrivalDv };
            }

            // Fallback: can't compute
            return null;
        }

        // Get delta-v requirements for a specific route (dynamically calculated)
        public string GetDeltaVMap(string from, string to)
        {
            try
            {
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                    return "{\"error\":\"FlightGlobals.Bodies not available yet\"}";

                CelestialBody fromBody = FindBody(from);
                CelestialBody toBody = FindBody(to);

                // Handle "LKO" / "_LKO" suffix as low orbit of the body
                bool fromLKO = from.EndsWith("_LKO", StringComparison.OrdinalIgnoreCase);
                if (fromLKO)
                {
                    string bodyName = from.Substring(0, from.Length - 4);
                    fromBody = FindBody(bodyName);
                }

                if (fromBody == null)
                    return "{\"error\":\"Unknown body: " + EscapeJson(from) + "\",\"available\":[" + GetAllBodyNames() + "]}";
                if (toBody == null)
                    return "{\"error\":\"Unknown body: " + EscapeJson(to) + "\",\"available\":[" + GetAllBodyNames() + "]}";

                var dv = CalculateRouteDv(fromBody, toBody, !fromLKO);
                if (dv == null)
                    return "{\"error\":\"Cannot compute route from " + EscapeJson(from) + " to " + EscapeJson(to) + "\"}";

                double total = dv[0] + dv[1] + dv[2];
                return "{\"from\":\"" + EscapeJson(from) + "\""
                    + ",\"to\":\"" + EscapeJson(toBody.name) + "\""
                    + ",\"launchDeltaV\":" + Math.Round(dv[0]).ToString(CultureInfo.InvariantCulture)
                    + ",\"transferDeltaV\":" + Math.Round(dv[1]).ToString(CultureInfo.InvariantCulture)
                    + ",\"arrivalDeltaV\":" + Math.Round(dv[2]).ToString(CultureInfo.InvariantCulture)
                    + ",\"totalOneWay\":" + Math.Round(total).ToString(CultureInfo.InvariantCulture)
                    + ",\"totalRoundTrip\":" + Math.Round(total * 2).ToString(CultureInfo.InvariantCulture)
                    + ",\"toHasAtmosphere\":" + (toBody.atmosphere ? "true" : "false")
                    + ",\"note\":\"Dynamically calculated from game data. Aerobraking can save significant dv at bodies with atmospheres.\""
                    + ",\"source\":\"dynamic\""
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        // Get full delta-v map for ALL bodies in the current solar system (dynamic)
        public string GetFullDeltaVMap()
        {
            try
            {
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                    return "{\"error\":\"FlightGlobals.Bodies not available yet\"}";

                CelestialBody home = GetHomeBody();
                if (home == null)
                    return "{\"error\":\"Cannot find home body\"}";

                string json = "{\"solarSystem\":\"" + EscapeJson(FlightGlobals.Bodies[0].name) + "\"";
                json += ",\"homeBody\":\"" + EscapeJson(home.name) + "\"";
                json += ",\"bodies\":{";

                bool firstBody = true;
                foreach (var body in FlightGlobals.Bodies)
                {
                    if (body == FlightGlobals.Bodies[0]) continue; // Skip the star itself

                    if (!firstBody) json += ",";
                    firstBody = false;

                    json += "\"" + EscapeJson(body.name) + "\":{";
                    json += "\"radius\":" + Math.Round(body.Radius).ToString(CultureInfo.InvariantCulture);
                    json += ",\"gravity\":" + Math.Round(body.GeeASL, 3).ToString(CultureInfo.InvariantCulture);
                    json += ",\"atmosphere\":" + (body.atmosphere ? "true" : "false");
                    if (body.atmosphere)
                        json += ",\"atmoHeight\":" + Math.Round(body.atmosphereDepth).ToString(CultureInfo.InvariantCulture);
                    json += ",\"lowOrbitAlt\":" + Math.Round(SafeLowOrbitAlt(body)).ToString(CultureInfo.InvariantCulture);
                    json += ",\"launchToOrbit\":" + Math.Round(EstimateLaunchDv(body)).ToString(CultureInfo.InvariantCulture);
                    json += ",\"landingDv\":" + Math.Round(EstimateLandingDv(body)).ToString(CultureInfo.InvariantCulture);

                    if (body.orbit != null && body.orbit.referenceBody != null)
                        json += ",\"parent\":\"" + EscapeJson(body.orbit.referenceBody.name) + "\"";

                    json += "}";
                }

                json += "}";

                // Add routes from home body
                json += ",\"fromHome\":{";
                bool firstRoute = true;
                foreach (var body in FlightGlobals.Bodies)
                {
                    if (body == home || body == FlightGlobals.Bodies[0]) continue;

                    var dv = CalculateRouteDv(home, body, true);
                    if (dv == null) continue;

                    if (!firstRoute) json += ",";
                    firstRoute = false;

                    double total = dv[0] + dv[1] + dv[2];
                    json += "\"" + EscapeJson(body.name) + "\":{";
                    json += "\"launch\":" + Math.Round(dv[0]).ToString(CultureInfo.InvariantCulture);
                    json += ",\"transfer\":" + Math.Round(dv[1]).ToString(CultureInfo.InvariantCulture);
                    json += ",\"arrival\":" + Math.Round(dv[2]).ToString(CultureInfo.InvariantCulture);
                    json += ",\"total\":" + Math.Round(total).ToString(CultureInfo.InvariantCulture);
                    json += "}";
                }
                json += "}";

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

                CelestialBody home = GetHomeBody();
                CelestialBody dest = FindBody(destination);

                double launchDv = home != null ? EstimateLaunchDv(home) : 3400;
                double transferDv = 0;
                double arrivalDv = 0;

                if (home != null && dest != null)
                {
                    var dv = CalculateRouteDv(home, dest, false);
                    if (dv != null)
                    {
                        transferDv = dv[1];
                        arrivalDv = dv[2];
                    }
                }

                // Build suggested steps
                int idx = 0;
                steps.Add(StepSuggestion(idx++, "Launch", "Launch to 80km Kerbin orbit", launchDv, "altitude", "80000"));
                totalDv += launchDv;

                steps.Add(StepSuggestion(idx++, "Transfer", "Hohmann transfer to " + destination, transferDv, "target", destination));
                totalDv += transferDv;

                // Check if destination has atmosphere (for aerobrake)
                bool hasAtmo = dest != null && dest.atmosphere;

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

        private string GetAllBodyNames()
        {
            if (FlightGlobals.Bodies == null) return "";
            string result = "";
            bool first = true;
            foreach (var body in FlightGlobals.Bodies)
            {
                if (!first) result += ",";
                first = false;
                result += "\"" + EscapeJson(body.name) + "\"";
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
