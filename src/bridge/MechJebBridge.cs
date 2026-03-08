using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ClaudePilot
{
    public class MechJebBridge
    {
        private Type mechJebCoreType;
        private object mechJebCore;
        private bool mechJebAvailable;
        private Guid lastVesselId;

        public void Initialize()
        {
            mechJebAvailable = false;
            foreach (var loaded in AssemblyLoader.loadedAssemblies)
            {
                if (loaded.assembly.GetName().Name == "MechJeb2")
                {
                    mechJebCoreType = loaded.assembly.GetType("MuMech.MechJebCore");
                    if (mechJebCoreType != null)
                    {
                        mechJebAvailable = true;
                        Debug.Log("[ClaudePilot] MechJeb2 found via reflection.");
                    }
                    break;
                }
            }

            if (!mechJebAvailable)
                Debug.Log("[ClaudePilot] MechJeb2 not found. MechJeb features disabled.");
        }

        public bool IsAvailable => mechJebAvailable;

        private bool FindMechJebCore()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                mechJebCore = null;
                return false;
            }

            if (mechJebCore != null && vessel.id == lastVesselId)
                return true;

            mechJebCore = null;
            lastVesselId = vessel.id;

            foreach (var part in vessel.Parts)
            {
                foreach (var module in part.Modules)
                {
                    if (mechJebCoreType.IsInstanceOfType(module))
                    {
                        mechJebCore = module;
                        Debug.Log("[ClaudePilot] Found MechJebCore on part: " + part.partInfo.title);
                        return true;
                    }
                }
            }

            Debug.LogWarning("[ClaudePilot] MechJebCore not found on any part of vessel: " + vessel.vesselName);
            return false;
        }

        /// <summary>
        /// Get a computer module by Type via GetComputerModule(Type), falling back to string
        /// </summary>
        private object GetComputerModule(string moduleName)
        {
            if (mechJebCore == null) return null;

            // Try by Type first (more reliable) — look up the type from MechJeb2 assembly
            Type moduleType = null;
            foreach (var loaded in AssemblyLoader.loadedAssemblies)
            {
                if (loaded.assembly.GetName().Name == "MechJeb2")
                {
                    moduleType = loaded.assembly.GetType("MuMech." + moduleName);
                    break;
                }
            }

            if (moduleType != null)
            {
                // Use GetComputerModule(Type) overload
                var method = mechJebCoreType.GetMethod("GetComputerModule",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(Type) }, null);
                if (method != null)
                {
                    var result = method.Invoke(mechJebCore, new object[] { moduleType });
                    if (result != null)
                    {
                        Debug.Log("[ClaudePilot] GetComputerModule(Type:" + moduleName + ") = " + result.GetType().Name);
                        return result;
                    }
                }
            }

            // Fallback: try the string overload
            var strMethod = mechJebCoreType.GetMethod("GetComputerModule",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);

            if (strMethod != null)
            {
                var result = strMethod.Invoke(mechJebCore, new object[] { moduleName });
                if (result != null)
                {
                    Debug.Log("[ClaudePilot] GetComputerModule(\"" + moduleName + "\") = " + result.GetType().Name);
                    return result;
                }
            }

            Debug.LogWarning("[ClaudePilot] GetComputerModule failed for: " + moduleName);
            return null;
        }

        /// <summary>
        /// Get a module via property on MechJebCore (e.g. core.AscentAutopilot)
        /// </summary>
        private object GetCoreProperty(string propertyName)
        {
            if (mechJebCore == null) return null;
            var prop = mechJebCoreType.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var result = prop.GetValue(mechJebCore);
                if (result != null)
                    Debug.Log("[ClaudePilot] core." + propertyName + " = " + result.GetType().Name);
                else
                    Debug.LogWarning("[ClaudePilot] core." + propertyName + " is null");
                return result;
            }
            Debug.LogWarning("[ClaudePilot] Property " + propertyName + " not found on MechJebCore");
            return null;
        }

        private void SetField(object module, string fieldName, object value)
        {
            if (module == null) return;
            var type = module.GetType();

            // Try field first
            var field = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                // Handle EditableDouble/EditableDoubleMult - MechJeb wraps doubles
                if (field.FieldType != typeof(double) && field.FieldType != typeof(float)
                    && field.FieldType != typeof(int) && value is double dval)
                {
                    var editableVal = field.GetValue(module);
                    if (editableVal != null)
                    {
                        var valField = editableVal.GetType().GetField("val",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (valField != null)
                        {
                            valField.SetValue(editableVal, dval);
                            Debug.Log("[ClaudePilot] Set " + fieldName + ".val = " + dval);
                            return;
                        }
                    }
                }
                field.SetValue(module, value);
                Debug.Log("[ClaudePilot] Set field " + fieldName + " = " + value);
                return;
            }

            // Try property
            var prop = type.GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(module, value);
                Debug.Log("[ClaudePilot] Set property " + fieldName + " = " + value);
            }
            else
            {
                Debug.LogWarning("[ClaudePilot] Could not find field/property: " + fieldName + " on " + type.Name);
            }
        }

        private object GetField(object module, string fieldName)
        {
            if (module == null) return null;
            var type = module.GetType();
            var field = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(module);
            var prop = type.GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(module);
            return null;
        }

        private object InvokeMethod(object module, string methodName, params object[] args)
        {
            if (module == null) return null;
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                types[i] = args[i]?.GetType() ?? typeof(object);

            var method = module.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, types, null);
            if (method == null)
                method = module.GetType().GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Debug.LogWarning("[ClaudePilot] Method not found: " + methodName);
                return null;
            }
            return method.Invoke(module, args);
        }

        private void EnableModule(object module)
        {
            if (module == null || mechJebCore == null) return;

            // MechJeb modules have a "users" UserPool — add the core as user to enable
            var usersField = module.GetType().GetField("users",
                BindingFlags.Public | BindingFlags.Instance);
            if (usersField != null)
            {
                var users = usersField.GetValue(module);
                if (users != null)
                {
                    var addMethod = users.GetType().GetMethod("Add", new[] { mechJebCoreType });
                    if (addMethod != null)
                    {
                        addMethod.Invoke(users, new[] { mechJebCore });
                        Debug.Log("[ClaudePilot] Enabled module: " + module.GetType().Name);
                        return;
                    }

                    // Try base type
                    var baseType = mechJebCoreType.BaseType;
                    while (baseType != null)
                    {
                        addMethod = users.GetType().GetMethod("Add", new[] { baseType });
                        if (addMethod != null)
                        {
                            addMethod.Invoke(users, new[] { mechJebCore });
                            Debug.Log("[ClaudePilot] Enabled module via base type: " + module.GetType().Name);
                            return;
                        }
                        baseType = baseType.BaseType;
                    }
                }
            }

            // Fallback: try setting 'enabled' property
            var enabledProp = module.GetType().GetProperty("Enabled",
                BindingFlags.Public | BindingFlags.Instance);
            if (enabledProp != null && enabledProp.CanWrite)
            {
                enabledProp.SetValue(module, true);
                Debug.Log("[ClaudePilot] Set Enabled=true on " + module.GetType().Name);
            }
            else
            {
                Debug.LogWarning("[ClaudePilot] Could not enable module: " + module.GetType().Name);
            }
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
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string PreCheck()
        {
            if (!mechJebAvailable)
                return ErrorJson("MechJeb is not installed. Install MechJeb2 to use autopilot features.");
            if (!FindMechJebCore())
                return ErrorJson("MechJeb core not responding on active vessel. MechJeb is auto-injected into all command pods - try switching to a vessel with a command pod, or retry after the vessel fully loads.");
            return null;
        }

        public string LaunchToOrbit(double altitude, double inclination)
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                // Use the AscentAutopilot property on core
                var ascent = GetCoreProperty("AscentAutopilot");
                if (ascent == null)
                {
                    // Fallback: try string lookup with various names
                    ascent = GetComputerModule("MechJebModuleAscentBaseAutopilot");
                    if (ascent == null)
                        ascent = GetComputerModule("MechJebModuleAscentClassicAutopilot");
                    if (ascent == null)
                        ascent = GetComputerModule("MechJebModuleAscentAutopilot");
                }
                if (ascent == null) return ErrorJson("Could not get ascent autopilot module. Available modules logged.");

                // Get AscentSettings to set orbit parameters
                var settings = GetCoreProperty("AscentSettings");
                if (settings != null)
                {
                    SetField(settings, "desiredOrbitAltitude", altitude);
                    SetField(settings, "desiredInclination", inclination);
                }
                else
                {
                    // Try directly on the autopilot
                    SetField(ascent, "desiredOrbitAltitude", altitude);
                    SetField(ascent, "desiredInclination", inclination);
                }

                // Enable autostaging
                var autostage = GetComputerModule("MechJebModuleStageStats");
                if (autostage != null)
                    SetField(ascent, "autostage", true);

                // Start the autopilot
                EnableModule(ascent);

                // Also try calling StartCountdown or Engage directly
                InvokeMethod(ascent, "StartCountdown", Planetarium.GetUniversalTime() + 3.0);

                return SuccessJson("\"action\":\"launch_to_orbit\",\"altitude\":" + altitude + ",\"inclination\":" + inclination);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ClaudePilot] LaunchToOrbit exception: " + ex);
                return ErrorJson("LaunchToOrbit failed: " + ex.Message);
            }
        }

        public string PlanTransfer(string targetBody)
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                CelestialBody body = null;
                foreach (var b in FlightGlobals.Bodies)
                {
                    if (b.name.Equals(targetBody, StringComparison.OrdinalIgnoreCase))
                    {
                        body = b;
                        break;
                    }
                }
                if (body == null)
                    return ErrorJson("Celestial body '" + targetBody + "' not found.");

                var vessel = FlightGlobals.ActiveVessel;
                int nodeCountBefore = 0;
                if (vessel.patchedConicSolver != null)
                    nodeCountBefore = vessel.patchedConicSolver.maneuverNodes.Count;

                // Method 1: Try MechJeb's maneuver planner
                bool success = false;
                var planner = GetComputerModule("MechJebModuleManeuverPlanner");
                if (planner != null)
                {
                    try
                    {
                        // Try the Hohmann transfer method
                        InvokeMethod(planner, "MakeNodeForHohmannTransfer", body);
                        success = true;
                    }
                    catch { }
                }

                // Check if MechJeb created a node
                if (success && vessel.patchedConicSolver != null)
                {
                    int nodeCountAfter = vessel.patchedConicSolver.maneuverNodes.Count;
                    if (nodeCountAfter > nodeCountBefore)
                    {
                        var node = vessel.patchedConicSolver.maneuverNodes[nodeCountAfter - 1];
                        return SuccessJson("\"action\":\"plan_transfer\",\"target\":\"" + EscapeJson(body.name) +
                            "\",\"deltaV\":" + node.DeltaV.magnitude +
                            ",\"timeToNode\":" + (node.UT - Planetarium.GetUniversalTime()) +
                            ",\"method\":\"mechjeb\"");
                    }
                }

                // Method 2: Fall back to TransferCalculator + manual node creation
                string calcResult = TransferCalculator.CalculateTransfer(targetBody, vessel.orbit.semiMajorAxis);
                if (calcResult.Contains("\"success\":true"))
                {
                    // Extract values and create manual node
                    string nodeUTStr = MiniJson.ExtractString(calcResult, "nodeUT");
                    string dvStr = MiniJson.ExtractString(calcResult, "progradeDeltaV");
                    if (dvStr == null) dvStr = MiniJson.ExtractString(calcResult, "deltaV");

                    double nodeUT = 0, dv = 0;
                    if (nodeUTStr != null)
                        double.TryParse(nodeUTStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out nodeUT);
                    if (dvStr != null)
                        double.TryParse(dvStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dv);

                    if (dv > 0 && nodeUT > 0 && vessel.patchedConicSolver != null)
                    {
                        var dvVec = new Vector3d(0, 0, dv); // prograde
                        var node = vessel.patchedConicSolver.AddManeuverNode(nodeUT);
                        node.DeltaV = dvVec;
                        vessel.patchedConicSolver.UpdateFlightPlan();

                        return SuccessJson("\"action\":\"plan_transfer\",\"target\":\"" + EscapeJson(body.name) +
                            "\",\"deltaV\":" + dv +
                            ",\"timeToNode\":" + (nodeUT - Planetarium.GetUniversalTime()) +
                            ",\"method\":\"calculated\"" +
                            ",\"note\":\"Node created from Hohmann calculation. Check get_maneuver_node_info for encounter prediction. If no encounter, try auto_transfer instead.\"");
                    }
                }

                return ErrorJson("Could not create transfer node to " + body.name +
                    ". Use auto_transfer instead — it handles the full transfer calculation and node creation.");
            }
            catch (Exception ex)
            {
                return ErrorJson("PlanTransfer failed: " + ex.Message);
            }
        }

        public string Circularize(bool atApoapsis)
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                var planner = GetComputerModule("MechJebModuleManeuverPlanner");
                if (planner == null) return ErrorJson("Could not get maneuver planner module.");

                var vessel = FlightGlobals.ActiveVessel;
                string timeRef = atApoapsis ? "apoapsis" : "periapsis";

                InvokeMethod(planner, "MakeNodeCircularize", atApoapsis);

                double targetAlt = atApoapsis ? vessel.orbit.ApA : vessel.orbit.PeA;
                return SuccessJson("\"action\":\"circularize\",\"at\":\"" + timeRef + "\",\"targetAltitude\":" + targetAlt);
            }
            catch (Exception ex)
            {
                return ErrorJson("Circularize failed: " + ex.Message);
            }
        }

        public string ExecuteNextNode()
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel.patchedConicSolver == null || vessel.patchedConicSolver.maneuverNodes.Count == 0)
                    return ErrorJson("No maneuver nodes to execute.");

                var executor = GetComputerModule("MechJebModuleNodeExecutor");
                if (executor == null) return ErrorJson("Could not get node executor module.");

                EnableModule(executor);
                InvokeMethod(executor, "ExecuteOneNode", mechJebCore);

                var node = vessel.patchedConicSolver.maneuverNodes[0];
                double burnTime = node.UT - Planetarium.GetUniversalTime();

                return SuccessJson("\"action\":\"execute_node\",\"timeToNode\":" + burnTime + ",\"deltaV\":" + node.DeltaV.magnitude);
            }
            catch (Exception ex)
            {
                return ErrorJson("ExecuteNextNode failed: " + ex.Message);
            }
        }

        public string Land(double? latitude, double? longitude)
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                var landing = GetComputerModule("MechJebModuleLandingAutopilot");
                if (landing == null) return ErrorJson("Could not get landing autopilot module.");

                if (latitude.HasValue && longitude.HasValue)
                {
                    SetField(landing, "targetLatitude", latitude.Value);
                    SetField(landing, "targetLongitude", longitude.Value);
                }

                EnableModule(landing);

                string coordInfo = "";
                if (latitude.HasValue && longitude.HasValue)
                    coordInfo = ",\"latitude\":" + latitude.Value + ",\"longitude\":" + longitude.Value;

                return SuccessJson("\"action\":\"land\"" + coordInfo);
            }
            catch (Exception ex)
            {
                return ErrorJson("Land failed: " + ex.Message);
            }
        }

        // Known landing locations on Kerbin
        private static readonly Dictionary<string, double[]> landingLocations = new Dictionary<string, double[]>
        {
            {"KSC", new double[] {-0.1028, 285.4422}},
            {"VAB", new double[] {-0.0969, 285.4493}},
            {"SPH", new double[] {-0.0486, 285.4325}},
            {"runway", new double[] {-0.0486, 285.4325}},
            {"launchpad", new double[] {-0.0969, 285.4493}},
            {"island_runway", new double[] {-1.5174, 287.8667}},
            {"island", new double[] {-1.5174, 287.8667}},
            {"dessert", new double[] {-6.5, 287.5}},
            {"wolves", new double[] {55.0, 280.0}}
        };

        public string GetLandingLocations()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"locations\":{");
            bool first = true;
            foreach (var loc in landingLocations)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(loc.Key).Append("\":{\"latitude\":").Append(loc.Value[0])
                  .Append(",\"longitude\":").Append(loc.Value[1]).Append("}");
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public string LandAt(string location)
        {
            string locUpper = location.ToUpperInvariant().Replace(" ", "_").Replace("-", "_");
            if (!landingLocations.ContainsKey(locUpper))
            {
                // Try partial match
                foreach (var key in landingLocations.Keys)
                {
                    if (key.Contains(locUpper) || locUpper.Contains(key))
                    {
                        locUpper = key;
                        break;
                    }
                }
            }

            if (!landingLocations.ContainsKey(locUpper))
            {
                return ErrorJson("Unknown landing location: " + location + ". Use get_landing_locations to see available locations.");
            }

            var coords = landingLocations[locUpper];
            return Land(coords[0], coords[1]);
        }

        public string StartRendezvous()
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel.targetObject == null)
                    return ErrorJson("No target selected. Set a target first.");

                var rendezvous = GetComputerModule("MechJebModuleRendezvousAutopilot");
                if (rendezvous == null) return ErrorJson("Could not get rendezvous autopilot module.");

                EnableModule(rendezvous);

                return SuccessJson("\"action\":\"rendezvous\",\"target\":\"" + EscapeJson(vessel.targetObject.GetName()) + "\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("StartRendezvous failed: " + ex.Message);
            }
        }

        public string StartDocking()
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel.targetObject == null)
                    return ErrorJson("No target selected. Set a docking target first.");

                var docking = GetComputerModule("MechJebModuleDockingAutopilot");
                if (docking == null) return ErrorJson("Could not get docking autopilot module.");

                EnableModule(docking);

                return SuccessJson("\"action\":\"docking\",\"target\":\"" + EscapeJson(vessel.targetObject.GetName()) + "\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("StartDocking failed: " + ex.Message);
            }
        }

        public string GetAutopilotStatus()
        {
            var err = PreCheck();
            if (err != null) return err;
            try
            {
                // Check ascent via property
                var ascent = GetCoreProperty("AscentAutopilot");
                string ascentStatus = GetModuleStatus(ascent);

                string[] moduleNames = {
                    "MechJebModuleLandingAutopilot",
                    "MechJebModuleNodeExecutor",
                    "MechJebModuleRendezvousAutopilot",
                    "MechJebModuleDockingAutopilot"
                };
                string[] shortNames = { "landing", "nodeExecutor", "rendezvous", "docking" };

                string items = "\"ascent\":\"" + ascentStatus + "\"";
                for (int i = 0; i < moduleNames.Length; i++)
                {
                    var module = GetComputerModule(moduleNames[i]);
                    items += ",\"" + shortNames[i] + "\":\"" + GetModuleStatus(module) + "\"";
                }

                return SuccessJson("\"autopilots\":{" + items + "}");
            }
            catch (Exception ex)
            {
                return ErrorJson("GetAutopilotStatus failed: " + ex.Message);
            }
        }

        private string GetModuleStatus(object module)
        {
            if (module == null) return "unavailable";

            var enabled = GetField(module, "enabled");
            if (enabled is bool b)
                return b ? "running" : "idle";

            var users = GetField(module, "users");
            if (users != null)
            {
                var countProp = users.GetType().GetProperty("Count");
                if (countProp != null)
                {
                    int count = (int)countProp.GetValue(users);
                    return count > 0 ? "running" : "idle";
                }
            }
            return "unknown";
        }
    }
}
