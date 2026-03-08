using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClaudePilot
{
    public class TelemetryData
    {
        public string vesselName;
        public string situation;
        public string altitudeFormatted;
        public string speedFormatted;
        public string surfaceSpeedFormatted;
        public string orbitalSpeedFormatted;
        public string apoapsisFormatted;
        public string periapsisFormatted;
        public double fuelPercent;
        public string targetName;
    }

    public class TelemetryProvider
    {
        public TelemetryData GetTelemetry()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return null;

            try
            {
                double fuelAmount = 0, fuelMax = 0;
                foreach (var part in vessel.Parts)
                {
                    foreach (var res in part.Resources)
                    {
                        if (res.resourceName == "LiquidFuel")
                        {
                            fuelAmount += res.amount;
                            fuelMax += res.maxAmount;
                        }
                    }
                }

                return new TelemetryData
                {
                    vesselName = vessel.vesselName,
                    situation = vessel.situation.ToString(),
                    altitudeFormatted = FormatDistance(vessel.altitude),
                    speedFormatted = FormatSpeed(vessel.srfSpeed),
                    surfaceSpeedFormatted = FormatSpeed(vessel.srfSpeed),
                    orbitalSpeedFormatted = FormatSpeed(vessel.obt_speed),
                    apoapsisFormatted = FormatDistance(vessel.orbit.ApA),
                    periapsisFormatted = FormatDistance(vessel.orbit.PeA),
                    fuelPercent = fuelMax > 0 ? (fuelAmount / fuelMax) * 100.0 : 0,
                    targetName = GetCurrentTarget()
                };
            }
            catch
            {
                return null;
            }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public string GetTelemetryJson()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
                return "{\"error\":\"No active vessel\"}";

            try
            {
                string targetName = GetCurrentTarget();
                string targetField = targetName != null
                    ? "\"" + EscapeJson(targetName) + "\""
                    : "null";

                return "{"
                    + "\"name\":\"" + EscapeJson(vessel.vesselName) + "\""
                    + ",\"situation\":\"" + vessel.situation.ToString() + "\""
                    + ",\"landed\":" + (vessel.Landed ? "true" : "false")
                    + ",\"altitude\":" + vessel.altitude
                    + ",\"radarAltitude\":" + vessel.radarAltitude
                    + ",\"surfaceSpeed\":" + vessel.srfSpeed
                    + ",\"orbitalSpeed\":" + vessel.obt_speed
                    + ",\"verticalSpeed\":" + vessel.verticalSpeed
                    + ",\"geeForce\":" + vessel.geeForce
                    + ",\"mass\":" + vessel.totalMass
                    + ",\"orbit\":" + GetOrbitJson()
                    + ",\"resources\":" + GetResourcesJson()
                    + ",\"target\":" + targetField
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        public string GetOrbitJson()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
                return "{\"error\":\"No active vessel\"}";

            try
            {
                var orbit = vessel.orbit;
                return "{"
                    + "\"body\":\"" + EscapeJson(orbit.referenceBody.name) + "\""
                    + ",\"apoapsis\":" + orbit.ApA
                    + ",\"periapsis\":" + orbit.PeA
                    + ",\"inclination\":" + orbit.inclination
                    + ",\"eccentricity\":" + orbit.eccentricity
                    + ",\"period\":" + orbit.period
                    + ",\"timeToApoapsis\":" + orbit.timeToAp
                    + ",\"timeToPeriapsis\":" + orbit.timeToPe
                    + ",\"semiMajorAxis\":" + orbit.semiMajorAxis
                    + ",\"argumentOfPeriapsis\":" + orbit.argumentOfPeriapsis
                    + ",\"longitudeOfAscendingNode\":" + orbit.LAN
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        public string GetDeltaVJson()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
                return "{\"error\":\"No active vessel\"}";

            try
            {
                // Use KSP 1.12 built-in VesselDeltaV if available
                var vesselDeltaV = vessel.VesselDeltaV;
                if (vesselDeltaV != null && vesselDeltaV.IsReady)
                {
                    string stages = "[";
                    var stageInfo = vesselDeltaV.OperatingStageInfo;
                    for (int i = 0; i < stageInfo.Count; i++)
                    {
                        var stage = stageInfo[i];
                        if (i > 0) stages += ",";
                        stages += "{"
                            + "\"stage\":" + stage.stage
                            + ",\"deltaV\":" + stage.deltaVActual
                            + ",\"deltaVVac\":" + stage.deltaVinVac
                            + ",\"twr\":" + stage.TWRActual
                            + ",\"startMass\":" + stage.startMass
                            + ",\"endMass\":" + stage.endMass
                            + ",\"isp\":" + stage.ispActual
                            + ",\"burnTime\":" + stage.stageBurnTime
                            + "}";
                    }
                    stages += "]";

                    double totalDv = vesselDeltaV.TotalDeltaVActual;
                    return "{\"totalDeltaV\":" + totalDv + ",\"stages\":" + stages + "}";
                }

                // Fallback: simplified Tsiolkovsky calculation
                return GetDeltaVFallback(vessel);
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        private string GetDeltaVFallback(Vessel vessel)
        {
            const double g0 = 9.80665;
            string stages = "[";
            bool first = true;

            // Simple single-stage estimate using all active engines
            double totalThrust = 0;
            double weightedIsp = 0;
            double thrustSum = 0;

            foreach (var part in vessel.Parts)
            {
                foreach (var module in part.Modules)
                {
                    var engine = module as ModuleEngines;
                    if (engine != null && engine.isOperational)
                    {
                        double thrust = engine.maxThrust;
                        totalThrust += thrust;
                        weightedIsp += thrust;
                        thrustSum += thrust / engine.atmosphereCurve.Evaluate(0f);
                    }
                }
            }

            double isp = thrustSum > 0 ? weightedIsp / thrustSum : 0;
            double totalMass = vessel.totalMass;

            // Estimate dry mass as 40% of total (rough approximation)
            double dryMass = totalMass * 0.4;
            double dv = isp > 0 ? isp * g0 * Math.Log(totalMass / dryMass) : 0;
            double twr = totalThrust / (totalMass * g0);

            if (!first) stages += ",";
            stages += "{"
                + "\"stage\":0"
                + ",\"deltaV\":" + dv
                + ",\"twr\":" + twr
                + ",\"isp\":" + isp
                + ",\"note\":\"Simplified estimate - install VesselDeltaV for accurate staging\""
                + "}";

            stages += "]";
            return "{\"totalDeltaV\":" + dv + ",\"stages\":" + stages + ",\"approximate\":true}";
        }

        public string GetResourcesJson()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
                return "{\"error\":\"No active vessel\"}";

            try
            {
                var resources = new Dictionary<string, double[]>();

                foreach (var part in vessel.Parts)
                {
                    foreach (var res in part.Resources)
                    {
                        if (!resources.ContainsKey(res.resourceName))
                            resources[res.resourceName] = new double[] { 0, 0 };

                        resources[res.resourceName][0] += res.amount;
                        resources[res.resourceName][1] += res.maxAmount;
                    }
                }

                string json = "[";
                bool first = true;
                foreach (var kvp in resources)
                {
                    if (!first) json += ",";
                    first = false;

                    double amount = kvp.Value[0];
                    double max = kvp.Value[1];
                    double pct = max > 0 ? (amount / max) * 100.0 : 0;

                    json += "{"
                        + "\"name\":\"" + EscapeJson(kvp.Key) + "\""
                        + ",\"amount\":" + amount
                        + ",\"maxAmount\":" + max
                        + ",\"percentage\":" + Math.Round(pct, 1)
                        + "}";
                }
                json += "]";
                return json;
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        public string GetBodiesJson()
        {
            try
            {
                string json = "[";
                var bodies = FlightGlobals.Bodies;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var body = bodies[i];
                    if (i > 0) json += ",";
                    json += "{"
                        + "\"name\":\"" + EscapeJson(body.name) + "\""
                        + ",\"radius\":" + body.Radius
                        + ",\"mass\":" + body.Mass
                        + ",\"gravParameter\":" + body.gravParameter
                        + ",\"hasAtmosphere\":" + (body.atmosphere ? "true" : "false")
                        + ",\"atmosphereDepth\":" + body.atmosphereDepth
                        + ",\"sphereOfInfluence\":" + body.sphereOfInfluence
                        + ",\"hasSolidSurface\":" + (body.hasSolidSurface ? "true" : "false");

                    if (body.scienceValues != null)
                    {
                        json += ",\"scienceValues\":{"
                            + "\"landedDataValue\":" + body.scienceValues.LandedDataValue
                            + ",\"flyingLowDataValue\":" + body.scienceValues.FlyingLowDataValue
                            + ",\"flyingHighDataValue\":" + body.scienceValues.FlyingHighDataValue
                            + ",\"inSpaceLowDataValue\":" + body.scienceValues.InSpaceLowDataValue
                            + ",\"inSpaceHighDataValue\":" + body.scienceValues.InSpaceHighDataValue
                            + ",\"recoveryValue\":" + body.scienceValues.RecoveryValue
                            + "}";
                    }

                    json += "}";
                }
                json += "]";
                return json;
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            }
        }

        public static string FormatDistance(double meters)
        {
            double abs = Math.Abs(meters);
            if (abs < 1000)
                return Math.Round(meters, 1) + " m";
            if (abs < 1000000)
                return Math.Round(meters / 1000, 2) + " km";
            if (abs < 1000000000)
                return Math.Round(meters / 1000000, 2) + " Mm";
            return Math.Round(meters / 1000000000, 2) + " Gm";
        }

        public static string FormatSpeed(double ms)
        {
            double abs = Math.Abs(ms);
            if (abs < 1000)
                return Math.Round(ms, 1) + " m/s";
            return Math.Round(ms / 1000, 2) + " km/s";
        }

        public string GetCurrentTarget()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return null;
            var target = vessel.targetObject;
            if (target == null) return null;
            return target.GetName();
        }
    }
}
