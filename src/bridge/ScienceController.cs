using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClaudePilot
{
    public class ScienceController
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

        // ─── Funds / Science / Reputation ───

        /// <summary>
        /// Get current funds, science points, and reputation
        /// </summary>
        public string GetGameEconomy()
        {
            try
            {
                string mode = "Unknown";
                if (HighLogic.CurrentGame != null)
                    mode = HighLogic.CurrentGame.Mode.ToString();

                double funds = 0;
                float science = 0;
                float reputation = 0;

                bool hasFunds = false;
                bool hasScience = false;
                bool hasReputation = false;

                try
                {
                    if (Funding.Instance != null)
                    {
                        funds = Funding.Instance.Funds;
                        hasFunds = true;
                    }
                }
                catch { }

                try
                {
                    if (ResearchAndDevelopment.Instance != null)
                    {
                        science = ResearchAndDevelopment.Instance.Science;
                        hasScience = true;
                    }
                }
                catch { }

                try
                {
                    if (Reputation.Instance != null)
                    {
                        reputation = Reputation.Instance.reputation;
                        hasReputation = true;
                    }
                }
                catch { }

                string json = "\"gameMode\":\"" + mode + "\"";
                if (hasFunds) json += ",\"funds\":" + funds;
                if (hasScience) json += ",\"science\":" + science;
                if (hasReputation) json += ",\"reputation\":" + reputation;

                if (mode == "SANDBOX")
                    json += ",\"note\":\"Sandbox mode — funds, science, and reputation are not tracked.\"";

                return SuccessJson(json);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetGameEconomy failed: " + ex.Message);
            }
        }

        // ─── Science Experiments ───

        /// <summary>
        /// List all science experiments available on the current vessel,
        /// with whether they have data and whether they can be run right now.
        /// </summary>
        public string GetScienceExperiments()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                var experiments = new List<string>();
                int idx = 0;

                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var sciExp = module as ModuleScienceExperiment;
                        if (sciExp != null)
                        {
                            bool hasData = sciExp.GetData().Length > 0;
                            bool canRun = !sciExp.Inoperable;
                            bool isRerunnable = sciExp.rerunnable;
                            float dataScale = sciExp.xmitDataScalar;

                            // Check what science value this would give
                            string biome = GetCurrentBiome(vessel);
                            float scienceValue = 0;
                            try
                            {
                                var situation = ScienceUtil.GetExperimentSituation(vessel);
                                string experimentId = sciExp.experiment.id;
                                var subjectId = experimentId + "@" + vessel.mainBody.name +
                                    situation.ToString();
                                var subject = ResearchAndDevelopment.GetExperimentSubject(
                                    sciExp.experiment, situation, vessel.mainBody, biome, null);
                                if (subject != null)
                                {
                                    scienceValue = ResearchAndDevelopment.GetScienceValue(
                                        sciExp.experiment.baseValue * sciExp.experiment.dataScale,
                                        subject, 1f);
                                }
                            }
                            catch { }

                            string expJson = "{\"index\":" + idx
                                + ",\"partName\":\"" + EscapeJson(part.partInfo.title) + "\""
                                + ",\"experimentId\":\"" + EscapeJson(sciExp.experiment != null ? sciExp.experiment.id : "unknown") + "\""
                                + ",\"experimentTitle\":\"" + EscapeJson(sciExp.experiment != null ? sciExp.experiment.experimentTitle : "Unknown") + "\""
                                + ",\"hasData\":" + (hasData ? "true" : "false")
                                + ",\"canRun\":" + (canRun ? "true" : "false")
                                + ",\"isRerunnable\":" + (isRerunnable ? "true" : "false")
                                + ",\"estimatedScience\":" + scienceValue
                                + "}";

                            experiments.Add(expJson);
                            idx++;
                        }

                        // Also check for science containers (like the command pod)
                        var sciContainer = module as ModuleScienceContainer;
                        if (sciContainer != null)
                        {
                            var storedData = sciContainer.GetData();
                            string contJson = "{\"index\":" + idx
                                + ",\"partName\":\"" + EscapeJson(part.partInfo.title) + "\""
                                + ",\"type\":\"container\""
                                + ",\"storedExperiments\":" + storedData.Length
                                + "}";
                            experiments.Add(contJson);
                            idx++;
                        }
                    }
                }

                // Current science context
                string situation2 = "";
                string biome2 = "";
                try
                {
                    situation2 = ScienceUtil.GetExperimentSituation(vessel).ToString();
                    biome2 = GetCurrentBiome(vessel);
                }
                catch { }

                string json = "[" + string.Join(",", experiments.ToArray()) + "]";
                return SuccessJson("\"count\":" + experiments.Count
                    + ",\"currentBody\":\"" + EscapeJson(vessel.mainBody.name) + "\""
                    + ",\"currentSituation\":\"" + EscapeJson(situation2) + "\""
                    + ",\"currentBiome\":\"" + EscapeJson(biome2) + "\""
                    + ",\"experiments\":" + json);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetScienceExperiments failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Run a science experiment by index (from GetScienceExperiments).
        /// </summary>
        public string RunExperiment(int experimentIndex)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                int idx = 0;
                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var sciExp = module as ModuleScienceExperiment;
                        if (sciExp != null)
                        {
                            if (idx == experimentIndex)
                            {
                                if (sciExp.Inoperable)
                                    return ErrorJson("Experiment is inoperable (already used and not rerunnable).");

                                // Deploy/run the experiment
                                sciExp.DeployExperiment();

                                return SuccessJson("\"action\":\"run_experiment\""
                                    + ",\"experiment\":\"" + EscapeJson(sciExp.experiment != null ? sciExp.experiment.experimentTitle : "Unknown") + "\""
                                    + ",\"partName\":\"" + EscapeJson(part.partInfo.title) + "\""
                                    + ",\"note\":\"Experiment deployed. Use collect_all_science to gather data into the command pod, then recover or transmit.\"");
                            }
                            idx++;
                        }

                        var sciCont = module as ModuleScienceContainer;
                        if (sciCont != null)
                        {
                            idx++; // skip containers in index
                        }
                    }
                }

                return ErrorJson("Experiment index " + experimentIndex + " not found. Use get_science_experiments to list available experiments.");
            }
            catch (Exception ex)
            {
                return ErrorJson("RunExperiment failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Run ALL available science experiments on the vessel at once.
        /// </summary>
        public string RunAllExperiments()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                int ran = 0;
                int skipped = 0;
                var ranNames = new List<string>();

                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var sciExp = module as ModuleScienceExperiment;
                        if (sciExp != null)
                        {
                            if (sciExp.Inoperable || sciExp.GetData().Length > 0)
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                sciExp.DeployExperiment();
                                ran++;
                                if (sciExp.experiment != null)
                                    ranNames.Add(sciExp.experiment.experimentTitle);
                            }
                            catch
                            {
                                skipped++;
                            }
                        }
                    }
                }

                string namesList = "[";
                for (int i = 0; i < ranNames.Count; i++)
                {
                    if (i > 0) namesList += ",";
                    namesList += "\"" + EscapeJson(ranNames[i]) + "\"";
                }
                namesList += "]";

                return SuccessJson("\"action\":\"run_all_experiments\""
                    + ",\"experimentsRun\":" + ran
                    + ",\"experimentsSkipped\":" + skipped
                    + ",\"experiments\":" + namesList
                    + ",\"note\":\"Use collect_all_science to gather into command pod, then transmit_all_science or recover vessel.\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("RunAllExperiments failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Collect all science data from experiments into science containers (command pods).
        /// Like EVA collect but done automatically.
        /// </summary>
        public string CollectAllScience()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                // Find science containers
                ModuleScienceContainer targetContainer = null;
                foreach (var part in vessel.Parts)
                {
                    var container = part.FindModuleImplementing<ModuleScienceContainer>();
                    if (container != null && container.allowRepeatedSubjects)
                    {
                        targetContainer = container;
                        break;
                    }
                }
                if (targetContainer == null)
                {
                    // Try any container
                    foreach (var part in vessel.Parts)
                    {
                        var container = part.FindModuleImplementing<ModuleScienceContainer>();
                        if (container != null)
                        {
                            targetContainer = container;
                            break;
                        }
                    }
                }

                if (targetContainer == null)
                    return ErrorJson("No science container found on vessel. Need a command pod or science storage unit.");

                int collected = 0;
                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var sciExp = module as ModuleScienceExperiment;
                        if (sciExp != null)
                        {
                            var data = sciExp.GetData();
                            foreach (var d in data)
                            {
                                if (targetContainer.HasData(d)) continue;
                                if (targetContainer.AddData(d))
                                {
                                    sciExp.DumpData(d);
                                    collected++;
                                }
                            }
                        }
                    }
                }

                int totalStored = targetContainer.GetData().Length;
                return SuccessJson("\"action\":\"collect_all_science\""
                    + ",\"collected\":" + collected
                    + ",\"totalStored\":" + totalStored
                    + ",\"containerPart\":\"" + EscapeJson(targetContainer.part.partInfo.title) + "\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("CollectAllScience failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Transmit all science data using available antennas.
        /// </summary>
        public string TransmitAllScience()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                // Find all data on the vessel
                var allData = new List<ScienceData>();
                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var sciExp = module as ModuleScienceExperiment;
                        if (sciExp != null)
                        {
                            allData.AddRange(sciExp.GetData());
                        }
                        var sciCont = module as ModuleScienceContainer;
                        if (sciCont != null)
                        {
                            allData.AddRange(sciCont.GetData());
                        }
                    }
                }

                if (allData.Count == 0)
                    return ErrorJson("No science data to transmit.");

                // Find a transmitter
                IScienceDataTransmitter transmitter = null;
                foreach (var part in vessel.Parts)
                {
                    foreach (var module in part.Modules)
                    {
                        var tx = module as IScienceDataTransmitter;
                        if (tx != null && tx.CanTransmit())
                        {
                            transmitter = tx;
                            break;
                        }
                    }
                    if (transmitter != null) break;
                }

                if (transmitter == null)
                    return ErrorJson("No working antenna/transmitter found. Deploy antennas or add one to the vessel.");

                // Estimate total science value
                float totalValue = 0;
                foreach (var data in allData)
                {
                    totalValue += data.dataAmount;
                }

                transmitter.TransmitData(allData);

                return SuccessJson("\"action\":\"transmit_all_science\""
                    + ",\"dataCount\":" + allData.Count
                    + ",\"estimatedScience\":" + totalValue
                    + ",\"note\":\"Transmission started. Some experiments lose value when transmitted vs recovered. High-value experiments should be physically recovered.\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("TransmitAllScience failed: " + ex.Message);
            }
        }

        // ─── Tech Tree ───

        /// <summary>
        /// Get the current tech tree state — what's researched and what's available to research next.
        /// Uses PartLoader's tech requirements + R&D snapshot for reliable cross-version compatibility.
        /// </summary>
        public string GetTechTree()
        {
            try
            {
                if (ResearchAndDevelopment.Instance == null)
                    return ErrorJson("R&D not available (sandbox mode or not loaded).");

                float availableScience = ResearchAndDevelopment.Instance.Science;

                // Build set of researched tech IDs from R&D snapshot
                var researchedIds = new HashSet<string>();
                try
                {
                    if (ResearchAndDevelopment.Instance.snapshot != null)
                    {
                        var data = ResearchAndDevelopment.Instance.snapshot.GetData();
                        if (data != null)
                        {
                            var techNodes = data.GetNodes("Tech");
                            if (techNodes != null)
                            {
                                foreach (var node in techNodes)
                                {
                                    string state = node.GetValue("state");
                                    string id = node.GetValue("id");
                                    if (id != null && state == "Available")
                                        researchedIds.Add(id);
                                }
                            }
                        }
                    }
                }
                catch { }

                // Alternate: use ResearchAndDevelopment.GetTechnologyState for known IDs
                // Scan parts to build tech node → parts mapping
                var techParts = new Dictionary<string, List<string>>();
                var techCosts = new Dictionary<string, float>();

                foreach (var ap in PartLoader.LoadedPartsList)
                {
                    if (ap.partPrefab == null || string.IsNullOrEmpty(ap.TechRequired)) continue;

                    string tid = ap.TechRequired;
                    if (!techParts.ContainsKey(tid))
                    {
                        techParts[tid] = new List<string>();
                        techCosts[tid] = 0;
                    }
                    techParts[tid].Add(ap.title ?? ap.name);

                    // Try to get cost from part entry cost
                    try
                    {
                        float cost = ap.entryCost;
                        if (cost > techCosts[tid]) techCosts[tid] = cost;
                    }
                    catch { }
                }

                // Also load tech tree config for costs and hierarchy
                var techTreeConfig = GameDatabase.Instance.GetConfigNodes("TechTree");
                var techTitles = new Dictionary<string, string>();
                var techParents = new Dictionary<string, List<string>>();

                if (techTreeConfig != null && techTreeConfig.Length > 0)
                {
                    var rdNodes = techTreeConfig[0].GetNodes("RDNode");
                    if (rdNodes != null)
                    {
                        foreach (var rdNode in rdNodes)
                        {
                            string id = rdNode.GetValue("id");
                            if (id == null) continue;

                            string title = rdNode.GetValue("title") ?? id;
                            techTitles[id] = title;

                            string costStr = rdNode.GetValue("cost");
                            if (costStr != null)
                            {
                                float cost;
                                if (float.TryParse(costStr, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out cost))
                                    techCosts[id] = cost;
                            }

                            // Parents
                            var parentNodes = rdNode.GetNodes("Parent");
                            if (parentNodes != null)
                            {
                                var parents = new List<string>();
                                foreach (var pn in parentNodes)
                                {
                                    string pid = pn.GetValue("parentID");
                                    if (pid != null) parents.Add(pid);
                                }
                                techParents[id] = parents;
                            }

                            // Ensure entry exists
                            if (!techParts.ContainsKey(id))
                                techParts[id] = new List<string>();
                        }
                    }
                }

                // Categorize nodes
                var researched = new List<string>();
                var availableList = new List<string>();
                int lockedCount = 0;

                // Collect all tech IDs
                var allTechIds = new HashSet<string>(techParts.Keys);
                foreach (var id in techTitles.Keys)
                    allTechIds.Add(id);

                foreach (string techId in allTechIds)
                {
                    string title = techTitles.ContainsKey(techId) ? techTitles[techId] : techId;
                    float cost = techCosts.ContainsKey(techId) ? techCosts[techId] : 0;
                    int partCount = techParts.ContainsKey(techId) ? techParts[techId].Count : 0;

                    bool isResearched = researchedIds.Contains(techId);
                    if (!isResearched)
                    {
                        // Also check via API
                        try
                        {
                            isResearched = ResearchAndDevelopment.GetTechnologyState(techId) == RDTech.State.Available;
                        }
                        catch { }
                    }

                    string nodeJson = "{\"id\":\"" + EscapeJson(techId) + "\""
                        + ",\"title\":\"" + EscapeJson(title) + "\""
                        + ",\"cost\":" + cost
                        + ",\"parts\":" + partCount + "}";

                    if (isResearched)
                    {
                        researched.Add(nodeJson);
                    }
                    else
                    {
                        // Check if parents are researched
                        bool parentsOk = true;
                        if (techParents.ContainsKey(techId))
                        {
                            foreach (var pid in techParents[techId])
                            {
                                if (!researchedIds.Contains(pid))
                                {
                                    try
                                    {
                                        if (ResearchAndDevelopment.GetTechnologyState(pid) != RDTech.State.Available)
                                        {
                                            parentsOk = false;
                                            break;
                                        }
                                    }
                                    catch
                                    {
                                        parentsOk = false;
                                        break;
                                    }
                                }
                            }
                        }

                        if (parentsOk)
                        {
                            bool canAfford = availableScience >= cost;
                            string partsList = "";
                            if (techParts.ContainsKey(techId) && techParts[techId].Count > 0)
                            {
                                partsList = ",\"unlocksPartNames\":[";
                                int max = Math.Min(techParts[techId].Count, 10);
                                for (int i = 0; i < max; i++)
                                {
                                    if (i > 0) partsList += ",";
                                    partsList += "\"" + EscapeJson(techParts[techId][i]) + "\"";
                                }
                                if (techParts[techId].Count > 10)
                                    partsList += ",\"...+" + (techParts[techId].Count - 10) + " more\"";
                                partsList += "]";
                            }

                            availableList.Add(nodeJson.Substring(0, nodeJson.Length - 1)
                                + ",\"canAfford\":" + (canAfford ? "true" : "false")
                                + partsList + "}");
                        }
                        else
                        {
                            lockedCount++;
                        }
                    }
                }

                return SuccessJson("\"availableScience\":" + availableScience
                    + ",\"researchedCount\":" + researched.Count
                    + ",\"availableToResearchCount\":" + availableList.Count
                    + ",\"lockedCount\":" + lockedCount
                    + ",\"availableToResearch\":[" + string.Join(",", availableList.ToArray()) + "]"
                    + ",\"researched\":[" + string.Join(",", researched.ToArray()) + "]");
            }
            catch (Exception ex)
            {
                return ErrorJson("GetTechTree failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Research/unlock a tech tree node by ID. Spends science points.
        /// </summary>
        public string ResearchTech(string techId)
        {
            try
            {
                if (ResearchAndDevelopment.Instance == null)
                    return ErrorJson("R&D not available (sandbox mode?).");

                if (string.IsNullOrEmpty(techId))
                    return ErrorJson("No tech ID specified.");

                // Check current state
                RDTech.State currentState;
                try
                {
                    currentState = ResearchAndDevelopment.GetTechnologyState(techId);
                }
                catch
                {
                    return ErrorJson("Tech node '" + techId + "' not found.");
                }

                if (currentState == RDTech.State.Available)
                    return ErrorJson("Tech '" + techId + "' is already researched.");

                // Get cost from tech tree config
                float cost = 0;
                string title = techId;
                var techTreeConfig = GameDatabase.Instance.GetConfigNodes("TechTree");
                if (techTreeConfig != null && techTreeConfig.Length > 0)
                {
                    var rdNodes = techTreeConfig[0].GetNodes("RDNode");
                    if (rdNodes != null)
                    {
                        foreach (var rdNode in rdNodes)
                        {
                            if (rdNode.GetValue("id") == techId)
                            {
                                title = rdNode.GetValue("title") ?? techId;
                                string costStr = rdNode.GetValue("cost");
                                if (costStr != null)
                                    float.TryParse(costStr, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out cost);

                                // Check prerequisites
                                var parentNodes = rdNode.GetNodes("Parent");
                                if (parentNodes != null)
                                {
                                    foreach (var pn in parentNodes)
                                    {
                                        string pid = pn.GetValue("parentID");
                                        if (pid != null)
                                        {
                                            try
                                            {
                                                if (ResearchAndDevelopment.GetTechnologyState(pid) != RDTech.State.Available)
                                                    return ErrorJson("Prerequisite '" + pid + "' not yet researched.");
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                float available = ResearchAndDevelopment.Instance.Science;
                if (available < cost)
                    return ErrorJson("Not enough science. Need " + cost + " but have " + available + ".");

                // Spend science
                ResearchAndDevelopment.Instance.AddScience(-cost, TransactionReasons.RnDTechResearch);

                // Unlock via ProtoTechNode
                var protoNode = new ProtoTechNode();
                protoNode.techID = techId;
                protoNode.state = RDTech.State.Available;
                protoNode.partsPurchased = new List<AvailablePart>();

                // Find parts for this tech
                foreach (var ap in PartLoader.LoadedPartsList)
                {
                    if (ap.TechRequired == techId)
                        protoNode.partsPurchased.Add(ap);
                }

                ResearchAndDevelopment.Instance.UnlockProtoTechNode(protoNode);

                float remaining = ResearchAndDevelopment.Instance.Science;

                string partNames = "";
                if (protoNode.partsPurchased.Count > 0)
                {
                    partNames = ",\"unlockedParts\":[";
                    for (int i = 0; i < protoNode.partsPurchased.Count; i++)
                    {
                        if (i > 0) partNames += ",";
                        partNames += "\"" + EscapeJson(protoNode.partsPurchased[i].title) + "\"";
                    }
                    partNames += "]";
                }

                return SuccessJson("\"action\":\"research_tech\""
                    + ",\"techId\":\"" + EscapeJson(techId) + "\""
                    + ",\"title\":\"" + EscapeJson(title) + "\""
                    + ",\"scienceSpent\":" + cost
                    + ",\"scienceRemaining\":" + remaining
                    + ",\"partsUnlocked\":" + protoNode.partsPurchased.Count
                    + partNames);
            }
            catch (Exception ex)
            {
                return ErrorJson("ResearchTech failed: " + ex.Message);
            }
        }

        // ─── Contracts ───

        /// <summary>
        /// Get active, available, and completed contracts
        /// </summary>
        public string GetContracts()
        {
            try
            {
                var cc = Contracts.ContractSystem.Instance;
                if (cc == null)
                    return ErrorJson("Contract system not available (sandbox mode?).");

                var active = new List<string>();
                var offered = new List<string>();

                foreach (var contract in cc.Contracts)
                {
                    string cJson = FormatContract(contract);
                    if (contract.ContractState == Contracts.Contract.State.Active)
                        active.Add(cJson);
                    else if (contract.ContractState == Contracts.Contract.State.Offered)
                        offered.Add(cJson);
                }

                return SuccessJson("\"activeContracts\":[" + string.Join(",", active.ToArray()) + "]"
                    + ",\"offeredContracts\":[" + string.Join(",", offered.ToArray()) + "]"
                    + ",\"activeCount\":" + active.Count
                    + ",\"offeredCount\":" + offered.Count);
            }
            catch (Exception ex)
            {
                return ErrorJson("GetContracts failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Accept an offered contract by index
        /// </summary>
        public string AcceptContract(int contractIndex)
        {
            try
            {
                var cc = Contracts.ContractSystem.Instance;
                if (cc == null)
                    return ErrorJson("Contract system not available.");

                int idx = 0;
                foreach (var contract in cc.Contracts)
                {
                    if (contract.ContractState == Contracts.Contract.State.Offered)
                    {
                        if (idx == contractIndex)
                        {
                            contract.Accept();
                            return SuccessJson("\"action\":\"accept_contract\""
                                + ",\"title\":\"" + EscapeJson(contract.Title) + "\""
                                + ",\"reward\":" + contract.FundsCompletion
                                + ",\"scienceReward\":" + contract.ScienceCompletion);
                        }
                        idx++;
                    }
                }

                return ErrorJson("Offered contract index " + contractIndex + " not found.");
            }
            catch (Exception ex)
            {
                return ErrorJson("AcceptContract failed: " + ex.Message);
            }
        }

        // ─── Crew ───

        /// <summary>
        /// Get crew info for the active vessel
        /// </summary>
        public string GetCrewInfo()
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                var crew = new List<string>();
                foreach (var kerbal in vessel.GetVesselCrew())
                {
                    crew.Add("{\"name\":\"" + EscapeJson(kerbal.name) + "\""
                        + ",\"type\":\"" + kerbal.type.ToString() + "\""
                        + ",\"trait\":\"" + EscapeJson(kerbal.trait) + "\""
                        + ",\"level\":" + kerbal.experienceLevel
                        + ",\"courage\":" + kerbal.courage
                        + ",\"stupidity\":" + kerbal.stupidity
                        + ",\"isBadass\":" + (kerbal.isBadass ? "true" : "false")
                        + "}");
                }

                int capacity = 0;
                foreach (var part in vessel.Parts)
                    capacity += part.CrewCapacity;

                return SuccessJson("\"crewCount\":" + crew.Count
                    + ",\"crewCapacity\":" + capacity
                    + ",\"crew\":[" + string.Join(",", crew.ToArray()) + "]");
            }
            catch (Exception ex)
            {
                return ErrorJson("GetCrewInfo failed: " + ex.Message);
            }
        }

        // ─── EVA ───

        /// <summary>
        /// Send a kerbal on EVA from the vessel
        /// </summary>
        public string GoEVA(int crewIndex)
        {
            try
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return ErrorJson("No active vessel.");

                var crewList = vessel.GetVesselCrew();
                if (crewIndex < 0 || crewIndex >= crewList.Count)
                    return ErrorJson("Crew index " + crewIndex + " out of range. Vessel has " + crewList.Count + " crew.");

                var kerbal = crewList[crewIndex];

                // Find the part this kerbal is in
                Part crewPart = null;
                foreach (var part in vessel.Parts)
                {
                    foreach (var pm in part.protoModuleCrew)
                    {
                        if (pm == kerbal)
                        {
                            crewPart = part;
                            break;
                        }
                    }
                    if (crewPart != null) break;
                }

                if (crewPart == null)
                    return ErrorJson("Could not find crew member's part.");

                FlightEVA.fetch.spawnEVA(kerbal, crewPart, crewPart.airlock ?? crewPart.transform);

                return SuccessJson("\"action\":\"go_eva\""
                    + ",\"kerbal\":\"" + EscapeJson(kerbal.name) + "\""
                    + ",\"note\":\"Kerbal is now on EVA. They can collect science from experiments, plant flags, etc.\"");
            }
            catch (Exception ex)
            {
                return ErrorJson("GoEVA failed: " + ex.Message);
            }
        }

        // ─── Helpers ───

        private static string GetCurrentBiome(Vessel vessel)
        {
            try
            {
                if (vessel.mainBody.BiomeMap == null) return "";
                double lat = vessel.latitude * Math.PI / 180.0;
                double lon = vessel.longitude * Math.PI / 180.0;
                var biome = vessel.mainBody.BiomeMap.GetAtt(lat, lon);
                return biome != null ? biome.name : "";
            }
            catch
            {
                return "";
            }
        }

        private static string FormatContract(Contracts.Contract contract)
        {
            string json = "{\"title\":\"" + EscapeJson(contract.Title) + "\""
                + ",\"state\":\"" + contract.ContractState.ToString() + "\""
                + ",\"prestige\":\"" + contract.Prestige.ToString() + "\"";

            try
            {
                json += ",\"fundsAdvance\":" + contract.FundsAdvance
                    + ",\"fundsCompletion\":" + contract.FundsCompletion
                    + ",\"fundsFailure\":" + contract.FundsFailure
                    + ",\"scienceCompletion\":" + contract.ScienceCompletion
                    + ",\"reputationCompletion\":" + contract.ReputationCompletion;
            }
            catch { }

            // Contract parameters (objectives)
            try
            {
                json += ",\"objectives\":[";
                bool first = true;
                for (int i = 0; i < contract.ParameterCount; i++)
                {
                    var param = contract.GetParameter(i);
                    if (param == null) continue;
                    if (!first) json += ",";
                    first = false;
                    json += "{\"title\":\"" + EscapeJson(param.Title) + "\""
                        + ",\"complete\":" + (param.State == Contracts.ParameterState.Complete ? "true" : "false")
                        + "}";
                }
                json += "]";
            }
            catch { }

            json += "}";
            return json;
        }
    }
}
