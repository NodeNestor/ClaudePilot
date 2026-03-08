using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ClaudePilot
{
    /// <summary>
    /// Bridge to KramaxAutoPilot for atmospheric/plane flight control.
    /// </summary>
    public class KramaxBridge
    {
        private bool kramaxAvailable = false;
        private Type kramaxPilotType;
        private object kramaxPilot;

        private static void Log(string msg)
        {
            Debug.Log("[ClaudePilot-Kramax] " + msg);
        }

        public void Initialize()
        {
            try
            {
                // Look for KramaxAutoPilot assembly
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.FullName.Contains("KramaxAutoPilot"))
                    {
                        kramaxPilotType = assembly.GetType("KramaxAutoPilot.KramaxAutoPilot");
                        if (kramaxPilotType != null)
                        {
                            kramaxAvailable = true;
                            Log("KramaxAutoPilot found and initialized.");
                            return;
                        }
                    }
                }
                Log("KramaxAutoPilot not found - plane autopilot tools unavailable.");
            }
            catch (Exception ex)
            {
                Log("Error initializing KramaxAutoPilot: " + ex.Message);
            }
        }

        private bool FindKramaxPilot()
        {
            if (!kramaxAvailable) return false;
            if (kramaxPilot != null) return true;

            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return false;

                // Find Kramax module on vessel
                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        if (module.GetType().Name.Contains("Kramax"))
                        {
                            kramaxPilot = module;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public bool IsAvailable => kramaxAvailable;

        public string SetHeading(double heading)
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                // Set heading hold mode
                heading = heading % 360;
                if (heading < 0) heading += 360;

                SetField(kramaxPilot, "HeadingHold", true);
                SetField(kramaxPilot, "TargetHeading", heading);

                return SuccessJson("\"action\":\"set_heading\",\"heading\":" + heading);
            }
            catch (Exception ex)
            {
                return ErrorJson("SetHeading failed: " + ex.Message);
            }
        }

        public string SetAltitude(double altitude)
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                SetField(kramaxPilot, "AltitudeHold", true);
                SetField(kramaxPilot, "TargetAltitude", altitude);

                return SuccessJson("\"action\":\"set_altitude\",\"altitude\":" + altitude);
            }
            catch (Exception ex)
            {
                return ErrorJson("SetAltitude failed: " + ex.Message);
            }
        }

        public string SetSpeed(double speed)
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                SetField(kramaxPilot, "SpeedHold", true);
                SetField(kramaxPilot, "TargetSpeed", speed);

                return SuccessJson("\"action\":\"set_speed\",\"speed\":" + speed);
            }
            catch (Exception ex)
            {
                return ErrorJson("SetSpeed failed: " + ex.Message);
            }
        }

        public string SetCruise(double heading, double altitude, double speed)
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                heading = heading % 360;
                if (heading < 0) heading += 360;

                SetField(kramaxPilot, "HeadingHold", true);
                SetField(kramaxPilot, "TargetHeading", heading);
                SetField(kramaxPilot, "AltitudeHold", true);
                SetField(kramaxPilot, "TargetAltitude", altitude);
                SetField(kramaxPilot, "SpeedHold", true);
                SetField(kramaxPilot, "TargetSpeed", speed);

                return SuccessJson("\"action\":\"set_cruise\",\"heading\":" + heading + ",\"altitude\":" + altitude + ",\"speed\":" + speed);
            }
            catch (Exception ex)
            {
                return ErrorJson("SetCruise failed: " + ex.Message);
            }
        }

        public string FlyTo(double latitude, double longitude, double altitude)
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                // Set waypoint target
                SetField(kramaxPilot, "WaypointMode", true);
                SetField(kramaxPilot, "TargetLat", latitude);
                SetField(kramaxPilot, "TargetLon", longitude);
                if (altitude > 0)
                {
                    SetField(kramaxPilot, "AltitudeHold", true);
                    SetField(kramaxPilot, "TargetAltitude", altitude);
                }

                return SuccessJson("\"action\":\"fly_to\",\"latitude\":" + latitude + ",\"longitude\":" + longitude + ",\"altitude\":" + altitude);
            }
            catch (Exception ex)
            {
                return ErrorJson("FlyTo failed: " + ex.Message);
            }
        }

        public string AutoLand()
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                // Enable autoland
                SetField(kramaxPilot, "AutoLand", true);

                return SuccessJson("\"action\":\"auto_land\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("AutoLand failed: " + ex.Message);
            }
        }

        public string Disengage()
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return ErrorJson("KramaxAutoPilot module not found on vessel.");

            try
            {
                SetField(kramaxPilot, "HeadingHold", false);
                SetField(kramaxPilot, "AltitudeHold", false);
                SetField(kramaxPilot, "SpeedHold", false);
                SetField(kramaxPilot, "WaypointMode", false);
                SetField(kramaxPilot, "AutoLand", false);

                return SuccessJson("\"action\":\"disengage\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("Disengage failed: " + ex.Message);
            }
        }

        public string GetStatus()
        {
            if (!kramaxAvailable) return ErrorJson("KramaxAutoPilot not installed.");
            if (!FindKramaxPilot()) return "{\"available\":false}";

            try
            {
                bool headingHold = GetBoolField(kramaxPilot, "HeadingHold");
                bool altHold = GetBoolField(kramaxPilot, "AltitudeHold");
                bool speedHold = GetBoolField(kramaxPilot, "SpeedHold");
                bool waypoint = GetBoolField(kramaxPilot, "WaypointMode");
                bool autoland = GetBoolField(kramaxPilot, "AutoLand");

                double heading = GetDoubleField(kramaxPilot, "TargetHeading");
                double alt = GetDoubleField(kramaxPilot, "TargetAltitude");
                double spd = GetDoubleField(kramaxPilot, "TargetSpeed");

                return SuccessJson(
                    "\"engaged\":" + (headingHold || altHold || speedHold || waypoint || autoland).ToString().ToLower() +
                    ",\"headingHold\":" + headingHold.ToString().ToLower() +
                    ",\"altitudeHold\":" + altHold.ToString().ToLower() +
                    ",\"speedHold\":" + speedHold.ToString().ToLower() +
                    ",\"waypointMode\":" + waypoint.ToString().ToLower() +
                    ",\"autoLand\":" + autoland.ToString().ToLower() +
                    ",\"targetHeading\":" + heading.ToString("F1") +
                    ",\"targetAltitude\":" + alt.ToString("F0") +
                    ",\"targetSpeed\":" + spd.ToString("F1")
                );
            }
            catch (Exception ex)
            {
                return ErrorJson("GetStatus failed: " + ex.Message);
            }
        }

        private void SetField(object obj, string fieldName, object value)
        {
            if (obj == null) return;
            var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                field.SetValue(obj, value);
            else
            {
                var prop = obj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(obj, value);
            }
        }

        private bool GetBoolField(object obj, string fieldName)
        {
            if (obj == null) return false;
            var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var val = field.GetValue(obj);
                if (val is bool b) return b;
            }
            var prop = obj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var val = prop.GetValue(obj);
                if (val is bool b) return b;
            }
            return false;
        }

        private double GetDoubleField(object obj, string fieldName)
        {
            if (obj == null) return 0;
            var field = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var val = field.GetValue(obj);
                if (val is double d) return d;
                if (val is float f) return f;
            }
            var prop = obj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var val = prop.GetValue(obj);
                if (val is double d) return d;
                if (val is float f) return f;
            }
            return 0;
        }

        private string ErrorJson(string message)
        {
            return "{\"success\":false,\"error\":\"" + EscapeJson(message) + "\"}";
        }

        private string SuccessJson(string details)
        {
            return "{\"success\":true," + details + "}";
        }

        private string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
