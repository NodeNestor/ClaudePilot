using System;
using UnityEngine;

namespace ClaudePilot
{
    /// <summary>
    /// Calculates Hohmann transfer parameters between orbits.
    /// </summary>
    public static class TransferCalculator
    {
        // Standard gravitational parameter (mu) for Kerbol system bodies in m^3/s^2
        private static readonly double G = 6.674e-11;
        private static readonly double KERBIN_MU = 3.5316e12;
        private static readonly double MUN_MU = 3.5316e10;
        private static readonly double MINMUS_MU = 1.7658e9;
        private static readonly double KERBIN_RADIUS = 600000;
        private static readonly double MUN_RADIUS = 200000;
        private static readonly double MINMUS_RADIUS = 60000;

        /// <summary>
        /// Calculate a Hohmann transfer to another body from Kerbin orbit.
        /// Returns JSON with delta-v and phase angle info.
        /// </summary>
        public static string CalculateTransfer(string targetBody, double currentAltitude)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");
                if (vessel.orbit == null) return ErrorJson("Vessel not in orbit.");

                var orbit = vessel.orbit;
                double r1 = orbit.semiMajorAxis; // Current orbit radius
                CelestialBody parent = orbit.referenceBody;

                // Find target body
                CelestialBody target = null;
                foreach (var body in FlightGlobals.Bodies)
                {
                    if (body.name.Equals(targetBody, StringComparison.OrdinalIgnoreCase))
                    {
                        target = body;
                        break;
                    }
                }
                if (target == null) return ErrorJson("Body '" + targetBody + "' not found.");

                double now = Planetarium.GetUniversalTime();

                // Different cases based on relationship
                if (target == parent)
                {
                    // Already orbiting the target - no transfer needed
                    return ErrorJson("Already orbiting " + target.name);
                }
                else if (target.orbit != null && target.orbit.referenceBody == parent)
                {
                    // Target is a sibling (moon of same parent) - Hohmann transfer
                    return CalculateSiblingTransfer(orbit, target, now);
                }
                else if (parent.orbit != null && parent.orbit.referenceBody == FlightGlobals.Bodies[0]
                         && target.orbit != null && target.orbit.referenceBody == FlightGlobals.Bodies[0])
                {
                    // We're orbiting a planet, target is another planet - interplanetary from planet orbit
                    return CalculateInterplanetaryFromPlanet(orbit, parent, target, now);
                }
                else if (target.orbit != null && target.orbit.referenceBody == FlightGlobals.Bodies[0])
                {
                    // Target is a planet, we're orbiting a moon - need to escape then transfer
                    return CalculateInterplanetaryTransfer(orbit, target, now);
                }
                else if (target.orbit != null && target.orbit.referenceBody != parent
                         && target.orbit.referenceBody.orbit != null
                         && target.orbit.referenceBody.orbit.referenceBody == parent)
                {
                    // Target is a moon of a moon-sibling body (e.g., from Kerbin orbit to Ike around Duna)
                    // Treat as interplanetary to the parent of target
                    return CalculateInterplanetaryFromPlanet(orbit, parent, target.orbit.referenceBody, now);
                }
                else
                {
                    return ErrorJson("Cannot calculate transfer to " + target.name + " from current orbit around " + parent.name + ".");
                }
            }
            catch (Exception ex)
            {
                return ErrorJson("Transfer calculation failed: " + ex.Message);
            }
        }

        private static string CalculateSiblingTransfer(Orbit vesselOrbit, CelestialBody target, double now)
        {
            double mu = vesselOrbit.referenceBody.gravParameter;
            double r1 = vesselOrbit.semiMajorAxis;
            double r2 = target.orbit.semiMajorAxis;

            // Hohmann transfer calculation
            double a_transfer = (r1 + r2) / 2.0;

            // Velocity at periapsis of transfer orbit
            double v1 = Math.Sqrt(mu * (2.0 / r1 - 1.0 / a_transfer));
            // Current circular velocity
            double v_circular = Math.Sqrt(mu / r1);
            // Delta-v needed
            double deltaV = v1 - v_circular;

            // Transfer time (half the orbital period of transfer orbit)
            double transferTime = Math.PI * Math.Sqrt(Math.Pow(a_transfer, 3) / mu);

            // Phase angle calculation
            double targetOrbitalPeriod = 2 * Math.PI * Math.Sqrt(Math.Pow(r2, 3) / mu);
            double targetAngularVelocity = 2 * Math.PI / targetOrbitalPeriod;
            double phaseAngle = Math.PI - targetAngularVelocity * transferTime;

            // Current phase angle
            double currentPhaseAngle = GetPhaseAngle(vesselOrbit, target);

            // Time until optimal window
            double synodicPeriod = CalculateSynodicPeriod(vesselOrbit, target.orbit);
            double phaseAngleError = NormalizeAngle(phaseAngle - currentPhaseAngle);
            double timeToWindow = (phaseAngleError / (2 * Math.PI)) * synodicPeriod;
            if (timeToWindow < 0) timeToWindow += synodicPeriod;

            // Best time to create node
            double nodeUT = now + timeToWindow;

            return SuccessJson(
                "\"target\":\"" + target.name + "\"," +
                "\"deltaV\":" + deltaV.ToString("F1") + "," +
                "\"progradeDeltaV\":" + deltaV.ToString("F1") + "," +
                "\"transferTime\":" + transferTime.ToString("F0") + "," +
                "\"requiredPhaseAngle\":" + (phaseAngle * 180 / Math.PI).ToString("F1") + "," +
                "\"currentPhaseAngle\":" + (currentPhaseAngle * 180 / Math.PI).ToString("F1") + "," +
                "\"timeToWindow\":" + timeToWindow.ToString("F0") + "," +
                "\"nodeUT\":" + nodeUT.ToString("F0") + "," +
                "\"arrivalAltitude\":" + (r2 / 1000).ToString("F0")
            );
        }

        /// <summary>
        /// Interplanetary transfer from orbit around a planet (e.g. Kerbin orbit → Duna)
        /// </summary>
        private static string CalculateInterplanetaryFromPlanet(Orbit vesselOrbit, CelestialBody parent, CelestialBody target, double now)
        {
            double muLocal = parent.gravParameter;
            double r_vessel = vesselOrbit.semiMajorAxis;

            // Interplanetary Hohmann transfer
            double r_parent = parent.orbit.semiMajorAxis;
            double r_target = target.orbit.semiMajorAxis;
            double mu_sun = FlightGlobals.Bodies[0].gravParameter;

            double a_transfer = (r_parent + r_target) / 2.0;
            double v_parent = Math.Sqrt(mu_sun / r_parent);
            double v_transfer_helio = Math.Sqrt(mu_sun * (2.0 / r_parent - 1.0 / a_transfer));
            double v_excess = Math.Abs(v_transfer_helio - v_parent);

            // Ejection burn from parking orbit (vis-viva)
            double v_parking = Math.Sqrt(muLocal / r_vessel);
            double v_ejection = Math.Sqrt(v_excess * v_excess + 2.0 * muLocal / r_vessel);
            double ejectionDV = v_ejection - v_parking;

            double transferTime = Math.PI * Math.Sqrt(Math.Pow(a_transfer, 3) / mu_sun);

            // Phase angle
            double targetOrbitalPeriod = target.orbit.period;
            double targetAngularVelocity = 2 * Math.PI / targetOrbitalPeriod;
            double phaseAngle = Math.PI - targetAngularVelocity * transferTime;

            // Current phase angle between parent and target
            double parentAngle = parent.orbit.trueAnomaly * Mathf.Deg2Rad + parent.orbit.argumentOfPeriapsis * Mathf.Deg2Rad;
            double targetAngle = target.orbit.trueAnomaly * Mathf.Deg2Rad + target.orbit.argumentOfPeriapsis * Mathf.Deg2Rad;
            double currentPhaseAngle = NormalizeAngle(targetAngle - parentAngle);

            // Time to transfer window
            double parentPeriod = parent.orbit.period;
            double synodicPeriod = Math.Abs(parentPeriod * targetOrbitalPeriod / (parentPeriod - targetOrbitalPeriod));
            double phaseAngleError = NormalizeAngle(phaseAngle - currentPhaseAngle);
            double timeToWindow = (phaseAngleError / (2 * Math.PI)) * synodicPeriod;
            if (timeToWindow < 100) timeToWindow += synodicPeriod; // Don't burn immediately

            double nodeUT = now + timeToWindow;

            return SuccessJson(
                "\"target\":\"" + target.name + "\"," +
                "\"deltaV\":" + ejectionDV.ToString("F1") + "," +
                "\"progradeDeltaV\":" + ejectionDV.ToString("F1") + "," +
                "\"transferTime\":" + transferTime.ToString("F0") + "," +
                "\"requiredPhaseAngle\":" + (phaseAngle * 180 / Math.PI).ToString("F1") + "," +
                "\"currentPhaseAngle\":" + (currentPhaseAngle * 180 / Math.PI).ToString("F1") + "," +
                "\"timeToWindow\":" + timeToWindow.ToString("F0") + "," +
                "\"nodeUT\":" + nodeUT.ToString("F0") + "," +
                "\"note\":\"Ejection burn from " + parent.name + " orbit. Phase angle timing is approximate.\""
            );
        }

        private static string CalculateInterplanetaryTransfer(Orbit vesselOrbit, CelestialBody target, double now)
        {
            // Simplified: escape current body then Hohmann to target
            double muLocal = vesselOrbit.referenceBody.gravParameter;
            double r1 = vesselOrbit.semiMajorAxis;

            // Escape velocity
            double v_escape_local = Math.Sqrt(2 * muLocal / r1);
            double v_circular = Math.Sqrt(muLocal / r1);
            double escapeDV = v_escape_local - v_circular;

            // Approximate interplanetary transfer (very simplified)
            double r_kerbin = 13599840256; // Kerbin orbit radius around Kerbol
            double r_target = target.orbit.semiMajorAxis;
            double mu_kerbol = FlightGlobals.Bodies[0].gravParameter;

            double a_transfer = (r_kerbin + r_target) / 2.0;
            double v_kerbin = Math.Sqrt(mu_kerbol / r_kerbin);
            double v_transfer = Math.Sqrt(mu_kerbol * (2.0 / r_kerbin - 1.0 / a_transfer));
            double transferDV = Math.Abs(v_transfer - v_kerbin);

            double totalDV = escapeDV + transferDV;
            double transferTime = Math.PI * Math.Sqrt(Math.Pow(a_transfer, 3) / mu_kerbol);

            return SuccessJson(
                "\"target\":\"" + target.name + "\"," +
                "\"deltaV\":" + totalDV.ToString("F1") + "," +
                "\"escapeDeltaV\":" + escapeDV.ToString("F1") + "," +
                "\"transferDeltaV\":" + transferDV.ToString("F1") + "," +
                "\"transferTime\":" + transferTime.ToString("F0") + "," +
                "\"note\":\"Interplanetary transfer - ejection angle and window timing are approximate\""
            );
        }

        private static double GetPhaseAngle(Orbit vesselOrbit, CelestialBody target)
        {
            // KSP's trueAnomaly is in degrees, convert to radians
            double vesselAngle = vesselOrbit.trueAnomaly * Math.PI / 180.0 + vesselOrbit.argumentOfPeriapsis * Math.PI / 180.0;
            double targetAngle = target.orbit.trueAnomaly * Math.PI / 180.0 + target.orbit.argumentOfPeriapsis * Math.PI / 180.0;
            return NormalizeAngle(targetAngle - vesselAngle);
        }

        private static double CalculateSynodicPeriod(Orbit orbit1, Orbit orbit2)
        {
            double T1 = orbit1.period;
            double T2 = orbit2.period;
            return Math.Abs(T1 * T2 / (T1 - T2));
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle < 0) angle += 2 * Math.PI;
            while (angle > 2 * Math.PI) angle -= 2 * Math.PI;
            return angle;
        }

        private static string ErrorJson(string message)
        {
            return "{\"success\":false,\"error\":\"" + EscapeJson(message) + "\"}";
        }

        private static string SuccessJson(string details)
        {
            return "{\"success\":true," + details + "}";
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
