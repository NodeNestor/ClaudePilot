using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClaudePilot
{
    public class CraftFileManager
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

        public string GetCraftDirectory(string facility)
        {
            return KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder + "/Ships/" + facility + "/";
        }

        public string ListCraftFiles(string facility)
        {
            try
            {
                string dir = GetCraftDirectory(facility);
                if (!Directory.Exists(dir))
                    return ErrorJson("Craft directory not found: " + facility);

                string[] files = Directory.GetFiles(dir, "*.craft");
                string json = "[";
                for (int i = 0; i < files.Length; i++)
                {
                    if (i > 0) json += ",";
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    json += "\"" + EscapeJson(name) + "\"";
                }
                json += "]";

                return SuccessJson("\"facility\":\"" + EscapeJson(facility) + "\",\"crafts\":" + json + ",\"count\":" + files.Length);
            }
            catch (Exception ex)
            {
                return ErrorJson("ListCraftFiles failed: " + ex.Message);
            }
        }

        public string ReadCraftFile(string name, string facility)
        {
            try
            {
                string path = GetCraftDirectory(facility) + name + ".craft";
                if (!File.Exists(path))
                    return ErrorJson("Craft file not found: " + name + " in " + facility);

                ConfigNode root = ConfigNode.Load(path);
                if (root == null)
                    return ErrorJson("Failed to parse craft file: " + name);

                string shipName = root.GetValue("ship") ?? name;
                string description = root.GetValue("description") ?? "";
                string version = root.GetValue("version") ?? "unknown";

                ConfigNode[] partNodes = root.GetNodes("PART");
                int partCount = partNodes.Length;

                // Count unique part types
                var partCounts = new Dictionary<string, int>();
                foreach (var partNode in partNodes)
                {
                    string partName = partNode.GetValue("part");
                    if (partName != null)
                    {
                        partName = ParsePartName(partName);
                        if (partCounts.ContainsKey(partName))
                            partCounts[partName]++;
                        else
                            partCounts[partName] = 1;
                    }
                }

                // Build parts summary
                string partsList = "[";
                bool first = true;
                foreach (var kvp in partCounts)
                {
                    if (!first) partsList += ",";
                    first = false;
                    partsList += "{\"name\":\"" + EscapeJson(kvp.Key) + "\",\"count\":" + kvp.Value + "}";
                }
                partsList += "]";

                // Build summary string
                string summary = shipName + ": " + partCount + " parts";

                return SuccessJson(
                    "\"name\":\"" + EscapeJson(shipName) + "\""
                    + ",\"description\":\"" + EscapeJson(description) + "\""
                    + ",\"version\":\"" + EscapeJson(version) + "\""
                    + ",\"totalParts\":" + partCount
                    + ",\"uniquePartTypes\":" + partCounts.Count
                    + ",\"parts\":" + partsList
                    + ",\"summary\":\"" + EscapeJson(summary) + "\""
                );
            }
            catch (Exception ex)
            {
                return ErrorJson("ReadCraftFile failed: " + ex.Message);
            }
        }

        public string AnalyzeCraft(string name, string facility)
        {
            try
            {
                string path = GetCraftDirectory(facility) + name + ".craft";
                if (!File.Exists(path))
                    return ErrorJson("Craft file not found: " + name + " in " + facility);

                ConfigNode root = ConfigNode.Load(path);
                if (root == null)
                    return ErrorJson("Failed to parse craft file: " + name);

                string shipName = root.GetValue("ship") ?? name;
                ConfigNode[] partNodes = root.GetNodes("PART");

                double totalMass = 0;
                double dryMass = 0;
                double totalThrust = 0;
                double totalCost = 0;
                double weightedIsp = 0;
                double thrustForIsp = 0;

                foreach (var partNode in partNodes)
                {
                    string partName = partNode.GetValue("part");
                    if (partName == null) continue;
                    partName = ParsePartName(partName);

                    AvailablePart partInfo = GetPartInfo(partName);
                    if (partInfo != null)
                    {
                        double partMass = partInfo.partPrefab.mass;
                        totalMass += partMass;
                        dryMass += partMass;
                        totalCost += partInfo.cost;

                        // Check for resources (fuel)
                        foreach (var res in partInfo.partPrefab.Resources)
                        {
                            var resDef = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
                            if (resDef != null)
                            {
                                double resMass = res.maxAmount * resDef.density;
                                totalMass += resMass;
                            }
                        }

                        // Check for engines
                        var engine = partInfo.partPrefab.FindModuleImplementing<ModuleEngines>();
                        if (engine != null)
                        {
                            totalThrust += engine.maxThrust;
                            float isp = engine.atmosphereCurve.Evaluate(0f);
                            weightedIsp += engine.maxThrust;
                            thrustForIsp += engine.maxThrust / isp;
                        }
                    }
                }

                double fuelMass = totalMass - dryMass;
                double avgIsp = thrustForIsp > 0 ? weightedIsp / thrustForIsp : 0;
                double g0 = 9.80665;
                double deltaV = avgIsp > 0 && dryMass > 0 ? avgIsp * g0 * Math.Log(totalMass / dryMass) : 0;
                double twr = totalMass > 0 ? totalThrust / (totalMass * g0) : 0;

                return SuccessJson(
                    "\"name\":\"" + EscapeJson(shipName) + "\""
                    + ",\"totalParts\":" + partNodes.Length
                    + ",\"totalMass\":" + Math.Round(totalMass, 3)
                    + ",\"dryMass\":" + Math.Round(dryMass, 3)
                    + ",\"fuelMass\":" + Math.Round(fuelMass, 3)
                    + ",\"totalThrust\":" + Math.Round(totalThrust, 2)
                    + ",\"estimatedDeltaV\":" + Math.Round(deltaV, 1)
                    + ",\"launchTWR\":" + Math.Round(twr, 2)
                    + ",\"averageIsp\":" + Math.Round(avgIsp, 1)
                    + ",\"estimatedCost\":" + Math.Round(totalCost, 0)
                    + ",\"note\":\"Mass and delta-v are estimates from part database. Actual values may vary with fuel load and staging.\""
                );
            }
            catch (Exception ex)
            {
                return ErrorJson("AnalyzeCraft failed: " + ex.Message);
            }
        }

        public string ModifyCraftPart(string name, string facility, string action, int partIndex, string newPart)
        {
            try
            {
                string path = GetCraftDirectory(facility) + name + ".craft";
                if (!File.Exists(path))
                    return ErrorJson("Craft file not found: " + name + " in " + facility);

                // Create backup first
                string backupPath = path + ".backup";
                File.Copy(path, backupPath, true);

                ConfigNode root = ConfigNode.Load(path);
                if (root == null)
                    return ErrorJson("Failed to parse craft file: " + name);

                ConfigNode[] partNodes = root.GetNodes("PART");
                if (partIndex < 0 || partIndex >= partNodes.Length)
                    return ErrorJson("Part index " + partIndex + " out of range. Craft has " + partNodes.Length + " parts.");

                string result;
                switch (action.ToLowerInvariant())
                {
                    case "swap":
                        if (string.IsNullOrEmpty(newPart))
                            return ErrorJson("newPart required for swap action.");

                        string oldPartName = partNodes[partIndex].GetValue("part");
                        partNodes[partIndex].SetValue("part", newPart + "_" + Guid.NewGuid().ToString().Substring(0, 8));
                        root.Save(path);
                        result = "\"action\":\"swap\",\"index\":" + partIndex
                            + ",\"oldPart\":\"" + EscapeJson(ParsePartName(oldPartName)) + "\""
                            + ",\"newPart\":\"" + EscapeJson(newPart) + "\"";
                        break;

                    case "remove":
                        string removedName = ParsePartName(partNodes[partIndex].GetValue("part"));
                        root.RemoveNode(partNodes[partIndex]);
                        root.Save(path);
                        result = "\"action\":\"remove\",\"index\":" + partIndex
                            + ",\"removedPart\":\"" + EscapeJson(removedName) + "\""
                            + ",\"warning\":\"Removing parts may break attachment nodes. Review craft in editor.\"";
                        break;

                    case "add":
                        if (string.IsNullOrEmpty(newPart))
                            return ErrorJson("newPart required for add action.");

                        ConfigNode newNode = new ConfigNode("PART");
                        newNode.AddValue("part", newPart + "_" + Guid.NewGuid().ToString().Substring(0, 8));
                        newNode.AddValue("partName", newPart);

                        // Insert after partIndex
                        // ConfigNode doesn't have insert, so rebuild
                        ConfigNode newRoot = new ConfigNode();
                        for (int v = 0; v < root.values.Count; v++)
                            newRoot.AddValue(root.values[v].name, root.values[v].value);

                        int insertIdx = 0;
                        foreach (var node in root.GetNodes())
                        {
                            newRoot.AddNode(node);
                            if (node.name == "PART")
                            {
                                if (insertIdx == partIndex)
                                    newRoot.AddNode(newNode);
                                insertIdx++;
                            }
                        }

                        newRoot.Save(path);
                        result = "\"action\":\"add\",\"afterIndex\":" + partIndex
                            + ",\"addedPart\":\"" + EscapeJson(newPart) + "\""
                            + ",\"warning\":\"Added part has no attachment. Edit in SPH/VAB to properly attach.\"";
                        break;

                    default:
                        return ErrorJson("Unknown action: " + action + ". Valid: swap, remove, add");
                }

                return SuccessJson(result + ",\"backup\":\"" + EscapeJson(backupPath) + "\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("ModifyCraftPart failed: " + ex.Message);
            }
        }

        public static string ParsePartName(string kspPartName)
        {
            if (kspPartName == null) return "unknown";
            // KSP part names in craft files are formatted as "partName_uniqueId"
            int underscoreIdx = kspPartName.LastIndexOf('_');
            if (underscoreIdx > 0)
                return kspPartName.Substring(0, underscoreIdx);
            return kspPartName;
        }

        public static AvailablePart GetPartInfo(string partName)
        {
            if (PartLoader.LoadedPartsList == null) return null;
            foreach (var ap in PartLoader.LoadedPartsList)
            {
                if (ap.name == partName)
                    return ap;
            }
            return null;
        }

        /// <summary>
        /// List available parts filtered by unlock status, category, search, size, and mod.
        /// Respects tech tree — only shows parts the player has actually unlocked (or all in sandbox).
        /// Handles modded games with thousands of parts via pagination.
        /// </summary>
        public string GetAvailableParts(string category, string search, string size, string mod,
            bool includeLockedParts, int page)
        {
            try
            {
                if (PartLoader.LoadedPartsList == null)
                    return ErrorJson("Part database not loaded.");

                bool isSandbox = HighLogic.CurrentGame != null
                    && HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;

                // Check if R&D is available (career/science mode)
                bool hasRnD = false;
                try
                {
                    hasRnD = ResearchAndDevelopment.Instance != null;
                }
                catch { }

                string searchLower = string.IsNullOrEmpty(search) ? null : search.ToLowerInvariant();
                PartCategories? filterCat = ParseCategory(category);

                // Parse size filter (diameter class)
                float? sizeMin = null, sizeMax = null;
                if (!string.IsNullOrEmpty(size))
                {
                    switch (size.ToLowerInvariant())
                    {
                        case "0.625": case "tiny": sizeMin = 0; sizeMax = 0; break;
                        case "1.25": case "small": sizeMin = 1; sizeMax = 1; break;
                        case "1.875": case "medium": sizeMin = 1; sizeMax = 2; break;
                        case "2.5": case "large": sizeMin = 2; sizeMax = 2; break;
                        case "3.75": case "extra-large": case "extralarge": sizeMin = 3; sizeMax = 3; break;
                        case "5": case "huge": sizeMin = 4; sizeMax = 4; break;
                    }
                }

                string modLower = string.IsNullOrEmpty(mod) ? null : mod.ToLowerInvariant();

                var matchingParts = new List<AvailablePart>();
                int totalLoaded = 0;
                int unlockedCount = 0;
                int lockedCount = 0;
                var modNames = new Dictionary<string, int>(); // track mods for summary

                foreach (var ap in PartLoader.LoadedPartsList)
                {
                    if (ap.partPrefab == null) continue;
                    if (ap.category == PartCategories.none) continue; // skip hidden parts
                    totalLoaded++;

                    // Check unlock status
                    bool isUnlocked = isSandbox;
                    if (!isSandbox && hasRnD)
                    {
                        try
                        {
                            isUnlocked = ResearchAndDevelopment.PartModelPurchased(ap);
                        }
                        catch
                        {
                            // If R&D check fails, assume available
                            isUnlocked = true;
                        }
                    }
                    else if (!isSandbox && !hasRnD)
                    {
                        // Science mode without R&D instance loaded, assume available
                        isUnlocked = true;
                    }

                    if (isUnlocked) unlockedCount++;
                    else lockedCount++;

                    // Track mod source
                    string partMod = "Squad"; // default stock
                    if (ap.partUrl != null)
                    {
                        // Part URL format: ModName/Parts/...
                        string url = ap.partUrl;
                        int slashIdx = url.IndexOf('/');
                        if (slashIdx > 0)
                            partMod = url.Substring(0, slashIdx);
                    }
                    if (!modNames.ContainsKey(partMod)) modNames[partMod] = 0;
                    modNames[partMod]++;

                    // Filter: unlock status
                    if (!isUnlocked && !includeLockedParts) continue;

                    // Filter: category
                    if (filterCat.HasValue && ap.category != filterCat.Value) continue;

                    // Filter: search (matches name, title, description, manufacturer, tags)
                    if (searchLower != null)
                    {
                        bool matches = ap.name.ToLowerInvariant().Contains(searchLower)
                            || (ap.title != null && ap.title.ToLowerInvariant().Contains(searchLower))
                            || (ap.description != null && ap.description.ToLowerInvariant().Contains(searchLower))
                            || (ap.manufacturer != null && ap.manufacturer.ToLowerInvariant().Contains(searchLower))
                            || (ap.tags != null && ap.tags.ToLowerInvariant().Contains(searchLower));
                        if (!matches) continue;
                    }

                    // Filter: size class via bulkheadProfiles
                    if (sizeMin.HasValue)
                    {
                        string profiles = ap.bulkheadProfiles ?? "";
                        bool sizeMatch = false;
                        if (profiles.Contains("size" + ((int)sizeMin.Value).ToString()))
                            sizeMatch = true;
                        if (sizeMax.HasValue && sizeMax.Value != sizeMin.Value
                            && profiles.Contains("size" + ((int)sizeMax.Value).ToString()))
                            sizeMatch = true;
                        if (!sizeMatch) continue;
                    }

                    // Filter: mod
                    if (modLower != null && !partMod.ToLowerInvariant().Contains(modLower)) continue;

                    matchingParts.Add(ap);
                }

                // Pagination
                int perPage = 30;
                int totalPages = (matchingParts.Count + perPage - 1) / perPage;
                if (page < 1) page = 1;
                if (page > totalPages && totalPages > 0) page = totalPages;

                int startIdx = (page - 1) * perPage;
                int endIdx = Math.Min(startIdx + perPage, matchingParts.Count);

                var results = new List<string>();
                for (int i = startIdx; i < endIdx; i++)
                {
                    results.Add(FormatPartJson(matchingParts[i], isSandbox, hasRnD));
                }

                string partsArray = "[" + string.Join(",", results.ToArray()) + "]";

                // Build mod summary (top 10)
                string modSummary = "";
                if (modNames.Count > 1)
                {
                    var modList = new List<KeyValuePair<string, int>>(modNames);
                    modList.Sort((a, b) => b.Value.CompareTo(a.Value));
                    modSummary = ",\"installedMods\":[";
                    int modCount = Math.Min(modList.Count, 10);
                    for (int i = 0; i < modCount; i++)
                    {
                        if (i > 0) modSummary += ",";
                        modSummary += "{\"name\":\"" + EscapeJson(modList[i].Key) + "\",\"parts\":" + modList[i].Value + "}";
                    }
                    if (modList.Count > 10) modSummary += ",{\"name\":\"...and " + (modList.Count - 10) + " more\",\"parts\":0}";
                    modSummary += "]";
                }

                return SuccessJson("\"matchCount\":" + matchingParts.Count
                    + ",\"page\":" + page
                    + ",\"totalPages\":" + totalPages
                    + ",\"perPage\":" + perPage
                    + ",\"totalLoaded\":" + totalLoaded
                    + ",\"unlockedParts\":" + unlockedCount
                    + ",\"lockedParts\":" + lockedCount
                    + ",\"gameMode\":\"" + (isSandbox ? "Sandbox" : (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Mode.ToString() : "Unknown")) + "\""
                    + (filterCat.HasValue ? ",\"category\":\"" + filterCat.Value.ToString() + "\"" : "")
                    + (searchLower != null ? ",\"search\":\"" + EscapeJson(search) + "\"" : "")
                    + modSummary
                    + ",\"parts\":" + partsArray);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetAvailableParts failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Format a single part as JSON with all relevant stats
        /// </summary>
        private string FormatPartJson(AvailablePart ap, bool isSandbox, bool hasRnD)
        {
            string partJson = "{\"name\":\"" + EscapeJson(ap.name) + "\""
                + ",\"title\":\"" + EscapeJson(ap.title) + "\""
                + ",\"category\":\"" + ap.category.ToString() + "\""
                + ",\"mass\":" + ap.partPrefab.mass
                + ",\"cost\":" + ap.cost;

            // Manufacturer
            if (!string.IsNullOrEmpty(ap.manufacturer))
                partJson += ",\"manufacturer\":\"" + EscapeJson(ap.manufacturer) + "\"";

            // Size profile
            if (!string.IsNullOrEmpty(ap.bulkheadProfiles))
                partJson += ",\"sizeProfile\":\"" + EscapeJson(ap.bulkheadProfiles) + "\"";

            // Tech tree node
            if (!string.IsNullOrEmpty(ap.TechRequired))
                partJson += ",\"techRequired\":\"" + EscapeJson(ap.TechRequired) + "\"";

            // Unlock status in career/science
            if (!isSandbox && hasRnD)
            {
                try
                {
                    bool unlocked = ResearchAndDevelopment.PartModelPurchased(ap);
                    partJson += ",\"unlocked\":" + (unlocked ? "true" : "false");
                }
                catch { }
            }

            // Mod source
            if (ap.partUrl != null)
            {
                int slashIdx = ap.partUrl.IndexOf('/');
                if (slashIdx > 0)
                {
                    string partMod = ap.partUrl.Substring(0, slashIdx);
                    if (partMod != "Squad")
                        partJson += ",\"mod\":\"" + EscapeJson(partMod) + "\"";
                }
            }

            // Engine stats
            var engine = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
            if (engine != null)
            {
                float ispVac = engine.atmosphereCurve.Evaluate(0f);
                float ispASL = engine.atmosphereCurve.Evaluate(1f);
                partJson += ",\"maxThrust\":" + engine.maxThrust
                    + ",\"ispVacuum\":" + ispVac
                    + ",\"ispASL\":" + ispASL;
            }

            // Fuel/resource capacity
            var resSummary = new System.Text.StringBuilder();
            bool firstRes = true;
            foreach (var res in ap.partPrefab.Resources)
            {
                if (!firstRes) resSummary.Append(",");
                firstRes = false;
                resSummary.Append("\"").Append(EscapeJson(res.resourceName)).Append("\":")
                    .Append(res.maxAmount);
            }
            if (!firstRes)
                partJson += ",\"resources\":{" + resSummary.ToString() + "}";

            // Module flags for quick filtering
            if (ap.partPrefab.FindModuleImplementing<ModuleCommand>() != null)
                partJson += ",\"isCommand\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleDecouple>() != null)
                partJson += ",\"isDecoupler\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleParachute>() != null)
                partJson += ",\"isParachute\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleDeployableSolarPanel>() != null)
            {
                var solar = ap.partPrefab.FindModuleImplementing<ModuleDeployableSolarPanel>();
                partJson += ",\"isSolarPanel\":true,\"chargeRate\":" + solar.chargeRate;
            }
            if (ap.partPrefab.FindModuleImplementing<ModuleScienceExperiment>() != null)
                partJson += ",\"isScienceExperiment\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleScienceContainer>() != null)
                partJson += ",\"isScienceContainer\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleReactionWheel>() != null)
            {
                var wheel = ap.partPrefab.FindModuleImplementing<ModuleReactionWheel>();
                partJson += ",\"isReactionWheel\":true,\"torque\":" + wheel.PitchTorque;
            }
            if (ap.partPrefab.FindModuleImplementing<ModuleRCS>() != null)
            {
                var rcs = ap.partPrefab.FindModuleImplementing<ModuleRCS>();
                partJson += ",\"isRCS\":true,\"thrusterPower\":" + rcs.thrusterPower;
            }
            if (ap.partPrefab.FindModuleImplementing<ModuleDataTransmitter>() != null)
            {
                var antenna = ap.partPrefab.FindModuleImplementing<ModuleDataTransmitter>();
                partJson += ",\"isAntenna\":true,\"antennaPower\":" + antenna.antennaPower;
            }
            if (ap.partPrefab.FindModuleImplementing<ModuleLight>() != null)
                partJson += ",\"isLight\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleWheelBase>() != null)
                partJson += ",\"isWheel\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleDockingNode>() != null)
                partJson += ",\"isDockingPort\":true";
            if (ap.partPrefab.FindModuleImplementing<ModuleGenerator>() != null)
            {
                var gen = ap.partPrefab.FindModuleImplementing<ModuleGenerator>();
                partJson += ",\"isGenerator\":true";
            }

            // Crew capacity
            if (ap.partPrefab.CrewCapacity > 0)
                partJson += ",\"crewCapacity\":" + ap.partPrefab.CrewCapacity;

            partJson += "}";
            return partJson;
        }

        /// <summary>
        /// Get part categories with counts of unlocked parts in each
        /// </summary>
        public string GetPartCategories()
        {
            try
            {
                bool isSandbox = HighLogic.CurrentGame != null
                    && HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
                bool hasRnD = false;
                try { hasRnD = ResearchAndDevelopment.Instance != null; } catch { }

                var catCounts = new Dictionary<string, int>();
                var catNames = new string[] { "Pods", "FuelTank", "Engine", "Control", "Structural",
                    "Coupling", "Payload", "Aero", "Ground", "Thermal", "Electrical",
                    "Communication", "Science", "Cargo", "Robotics", "Utility" };

                foreach (var name in catNames)
                    catCounts[name] = 0;

                foreach (var ap in PartLoader.LoadedPartsList)
                {
                    if (ap.partPrefab == null || ap.category == PartCategories.none) continue;

                    bool unlocked = isSandbox;
                    if (!isSandbox && hasRnD)
                    {
                        try { unlocked = ResearchAndDevelopment.PartModelPurchased(ap); } catch { unlocked = true; }
                    }
                    else if (!isSandbox) unlocked = true;

                    if (!unlocked) continue;

                    string catName = ap.category.ToString();
                    if (catCounts.ContainsKey(catName))
                        catCounts[catName]++;
                    else
                        catCounts[catName] = 1;
                }

                string json = "[";
                bool first = true;
                foreach (var kvp in catCounts)
                {
                    if (!first) json += ",";
                    first = false;
                    json += "{\"category\":\"" + kvp.Key + "\",\"unlockedParts\":" + kvp.Value + "}";
                }
                // Add any extra categories from mods
                foreach (var kvp in catCounts)
                {
                    if (!System.Array.Exists(catNames, c => c == kvp.Key))
                    {
                        json += ",{\"category\":\"" + EscapeJson(kvp.Key) + "\",\"unlockedParts\":" + kvp.Value + "}";
                    }
                }
                json += "]";

                return SuccessJson("\"gameMode\":\"" + (isSandbox ? "Sandbox" : (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Mode.ToString() : "Unknown")) + "\""
                    + ",\"categories\":" + json);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetPartCategories failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Create a new craft file from a template.
        /// Claude specifies a list of parts and basic structure.
        /// </summary>
        public string CreateCraft(string name, string facility, string partsJson)
        {
            try
            {
                string dir = GetCraftDirectory(facility);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = dir + name + ".craft";

                // Parse parts list - expected format: [{"name":"mk1pod_v2","position":[0,0,0]}, ...]
                // We'll build a proper KSP craft file

                var root = new ConfigNode();
                root.AddValue("ship", name);
                root.AddValue("version", "1.12.5");
                root.AddValue("description", "Designed by ClaudePilot AI");
                root.AddValue("type", facility == "SPH" ? "SPH" : "VAB");

                // Parse parts from JSON
                var parts = ParsePartsJson(partsJson);
                if (parts.Count == 0)
                    return ErrorJson("No parts specified. Provide a JSON array of parts.");

                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var partNode = new ConfigNode("PART");

                    string partId = part.name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    partNode.AddValue("part", partId);
                    partNode.AddValue("partName", part.name);

                    // Position
                    partNode.AddValue("pos", part.posX + "," + part.posY + "," + part.posZ);
                    partNode.AddValue("rot", "0,0,0,1");
                    partNode.AddValue("mir", "1,1,1");

                    // Attachment: first part is root, others attach to parent
                    if (i == 0)
                    {
                        partNode.AddValue("istg", "0");
                        partNode.AddValue("dstg", "0");
                    }
                    else
                    {
                        partNode.AddValue("istg", part.stage.ToString());
                        partNode.AddValue("dstg", part.stage.ToString());

                        // Link to parent part
                        if (part.parentIndex >= 0 && part.parentIndex < parts.Count)
                        {
                            string parentId = parts[part.parentIndex].name + "_"
                                + parts[part.parentIndex].assignedId;
                            var linkNode = new ConfigNode("LINK");
                            linkNode.AddValue("prt", parentId);
                            partNode.AddNode(linkNode);
                        }

                        // Attach nodes
                        var attNode = new ConfigNode("ATTNODE");
                        attNode.AddValue("%.%.%", "0,0,0,0,1");
                        partNode.AddNode(attNode);
                    }

                    // Store assigned ID for linking
                    part.assignedId = partId.Substring(part.name.Length + 1);

                    root.AddNode(partNode);
                }

                root.Save(path);

                return SuccessJson("\"action\":\"create_craft\""
                    + ",\"name\":\"" + EscapeJson(name) + "\""
                    + ",\"facility\":\"" + EscapeJson(facility) + "\""
                    + ",\"parts\":" + parts.Count
                    + ",\"path\":\"" + EscapeJson(path) + "\""
                    + ",\"note\":\"Craft created. Load it in the editor with load_craft_in_editor to verify and fix attachments before launching. Auto-generated attachment nodes may need manual adjustment.\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("CreateCraft failed: " + ex.Message);
            }
        }

        private PartCategories? ParseCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return null;
            switch (category.ToLowerInvariant())
            {
                case "pods": case "command": return PartCategories.Pods;
                case "fueltank": case "fuel": case "tanks": return PartCategories.FuelTank;
                case "engine": case "engines": case "propulsion": return PartCategories.Engine;
                case "control": return PartCategories.Control;
                case "structural": case "structure": return PartCategories.Structural;
                case "coupling": case "decoupler": case "decouplers": return PartCategories.Coupling;
                case "payload": return PartCategories.Payload;
                case "aero": case "aerodynamics": return PartCategories.Aero;
                case "ground": return PartCategories.Ground;
                case "thermal": return PartCategories.Thermal;
                case "electrical": case "electric": case "power": return PartCategories.Electrical;
                case "communication": case "comms": return PartCategories.Communication;
                case "science": return PartCategories.Science;
                case "cargo": return PartCategories.Cargo;
                case "robotics": return PartCategories.Robotics;
                case "utility": return PartCategories.Utility;
                default: return null;
            }
        }

        private class CraftPart
        {
            public string name;
            public float posX, posY, posZ;
            public int stage;
            public int parentIndex;
            public string assignedId;
        }

        private List<CraftPart> ParsePartsJson(string json)
        {
            var parts = new List<CraftPart>();
            if (string.IsNullOrEmpty(json)) return parts;

            string trimmed = json.Trim();
            if (trimmed.StartsWith("[")) trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("]")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

            // Split on },{ boundaries
            var chunks = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '{') depth++;
                else if (trimmed[i] == '}') depth--;
                else if (trimmed[i] == ',' && depth == 0)
                {
                    chunks.Add(trimmed.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < trimmed.Length)
                chunks.Add(trimmed.Substring(start).Trim());

            float yOffset = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                string chunk = chunks[i];
                var part = new CraftPart();

                part.name = MiniJson.ExtractString(chunk, "name") ?? "mk1pod_v2";

                string stageStr = MiniJson.ExtractString(chunk, "stage");
                if (stageStr != null) int.TryParse(stageStr, out part.stage);
                else part.stage = i == 0 ? 0 : parts.Count > 0 ? parts[parts.Count - 1].stage : 0;

                string parentStr = MiniJson.ExtractString(chunk, "parent");
                if (parentStr != null) int.TryParse(parentStr, out part.parentIndex);
                else part.parentIndex = i > 0 ? i - 1 : -1;

                // Auto-stack vertically if no position given
                string posYStr = MiniJson.ExtractString(chunk, "y");
                if (posYStr != null)
                {
                    float.TryParse(posYStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out part.posY);
                }
                else
                {
                    part.posY = yOffset;
                    // Estimate part height (rough)
                    AvailablePart info = GetPartInfo(part.name);
                    float height = 1.0f;
                    if (info != null && info.partPrefab != null)
                    {
                        var bounds = info.partPrefab.GetComponent<MeshFilter>();
                        if (bounds != null)
                            height = bounds.sharedMesh.bounds.size.y;
                    }
                    yOffset -= height;
                }

                parts.Add(part);
            }

            return parts;
        }
    }
}
