# ClaudePilot — AI Copilot for Kerbal Space Program

ClaudePilot adds a fully autonomous AI copilot to KSP. Chat with Claude in-game to fly missions, design rockets, do science, manage your career, and explore the Kerbol system — all through natural language.

## Features

### Flight & Navigation
- **Natural Language Flight** — "launch to 80km orbit", "fly me to the Mun and back"
- **Autonomous Transfers** — Calculates phase angles, creates maneuver nodes, warps, and burns automatically
- **MechJeb Integration** — Controls ascent, landing, rendezvous, docking via reflection
- **Full Warp Control** — Warp to apoapsis, periapsis, maneuver nodes, SOI changes, or any specific time
- **Maneuver Node Management** — Create, inspect (with encounter prediction), and execute nodes

### Science & Career
- **Run Science** — Deploy all experiments, collect data, transmit or recover
- **Tech Tree** — View researched/available nodes, spend science to unlock new parts
- **Contracts** — View active/offered contracts, accept profitable ones
- **Economy** — Track funds, science points, and reputation
- **Crew Management** — View crew stats, send kerbals on EVA

### Rocket Design
- **Part Browser** — Search the full part database filtered by category, size, mod, or keyword
- **Tech Tree Aware** — Only shows parts the player has actually unlocked (Career/Science mode)
- **Mod Support** — Handles thousands of modded parts with pagination and mod filtering
- **Craft Creation** — Build craft files from part lists, analyze mass/delta-v/TWR
- **Craft Modification** — Swap, add, or remove parts with automatic backups

### Mission Planning
- **Delta-V Maps** — Full Kerbol system delta-v reference
- **Mission Suggestions** — Auto-generate step-by-step plans for any destination
- **Mission Tracking** — Create plans, track progress, auto-execute sequences
- **Scene Switching** — Move between VAB, SPH, Tracking Station, and Flight

### Aircraft
- **Kramax Autopilot** — Set heading, altitude, speed, cruise to coordinates, autoland

## Requirements

- **KSP 1.12.x**
- **MechJeb2** (optional — required for autopilot features like ascent/landing)
- **KramaxAutoPilot** (optional — required for aircraft autopilot)
- An **OpenAI-compatible API endpoint** (Claude, local LLM, etc.)

## Installation

1. Build the DLL (see Building) or grab from Releases
2. Copy `GameData/ClaudePilot/` to your KSP `GameData/`:
   ```
   KSP/GameData/ClaudePilot/
   ├── ClaudePilot.dll
   └── config.cfg
   ```
3. Launch KSP, enter any scene
4. Press **Alt+P** or click the toolbar button
5. Enter your API key/endpoint in settings

## Configuration

Edit `GameData/ClaudePilot/config.cfg` or use the in-game settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `apiKey` | (empty) | API key for your LLM endpoint |
| `apiBaseUrl` | `http://127.0.0.1:9212` | API base URL (OpenAI-compatible) |
| `model` | `claude-sonnet-4-6` | Model name |
| `keybind` | `LeftAlt+C` | Toggle chat window |
| `maxHistoryMessages` | `20` | Conversation history length |
| `enableTelemetryHUD` | `true` | Show telemetry overlay |

## Usage Examples

### Missions
- "Fly me to the Mun and land"
- "Plan a round trip to Duna with enough delta-v"
- "Transfer to Minmus, do science, and come home"

### Science & Career
- "Run all science experiments"
- "How much science do I have? What can I research?"
- "Unlock the next propulsion tech"
- "What contracts are available?"

### Rocket Design
- "Search for high-ISP vacuum engines"
- "Show me 2.5m fuel tanks"
- "Design a rocket with 5000 m/s delta-v for a Mun mission"
- "What parts does NearFuture add?"

### Flight Control
- "Set SAS to prograde and warp to apoapsis"
- "Create a 860 m/s prograde node at apoapsis"
- "Check if my maneuver gets a Mun encounter"

## All Tools (80+)

| Category | Tools |
|----------|-------|
| **Telemetry** | `get_vessel_telemetry`, `get_orbit_info`, `get_delta_v`, `get_resources`, `get_available_bodies` |
| **Flight** | `set_target`, `activate_stage`, `set_sas_mode`, `toggle_action_group`, `create_maneuver_node`, `delete_all_maneuver_nodes`, `get_maneuver_node_info` |
| **Time Warp** | `set_time_warp`, `stop_time_warp`, `warp_to_next_node`, `warp_to_apoapsis`, `warp_to_periapsis`, `warp_to_time`, `warp_to_soi_change` |
| **Transfers** | `auto_transfer`, `calculate_transfer`, `plan_transfer` |
| **MechJeb** | `launch_to_orbit(_wait)`, `circularize(_wait)`, `execute_next_node(_wait)`, `land(_wait)`, `start_rendezvous(_wait)`, `start_docking(_wait)`, `wait_for_autopilot` |
| **Science** | `get_science_experiments`, `run_experiment`, `run_all_experiments`, `collect_all_science`, `transmit_all_science` |
| **Tech Tree** | `get_tech_tree`, `research_tech` |
| **Economy** | `get_game_economy`, `get_contracts`, `accept_contract` |
| **Crew** | `get_crew_info`, `go_eva` |
| **Parts** | `get_available_parts`, `get_part_categories` |
| **Craft Files** | `list_craft_files`, `read_craft_file`, `analyze_craft`, `modify_craft_part`, `create_craft` |
| **Missions** | `suggest_mission`, `create_mission_plan`, `get_mission_plan`, `update_mission_step`, `get_next_mission_step`, `execute_mission_plan`, `cancel_mission_plan`, `get_delta_v_map`, `get_full_delta_v_map` |
| **Scenes** | `go_to_vab`, `go_to_sph`, `go_to_tracking_station`, `go_to_space_center`, `get_current_scene`, `load_craft_in_editor`, `launch_vessel`, `quick_launch`, `recover_vessel` |
| **Landing** | `land_at`, `get_landing_locations` |
| **Aircraft** | `plane_cruise`, `plane_set_heading`, `plane_set_altitude`, `plane_set_speed`, `plane_fly_to`, `plane_auto_land`, `plane_disengage` |

## Building from Source

1. Install Visual Studio or `dotnet` CLI with .NET Framework 4.7.2
2. Set `KSPDir` environment variable to your KSP install, or edit reference paths in `ClaudePilot.csproj`
3. Build:
   ```
   dotnet build ClaudePilot.csproj
   ```
4. Copy `bin/Debug/ClaudePilot.dll` to `GameData/ClaudePilot/`

## Architecture

```
src/
├── ClaudePilotAddon.cs          # KSPAddon entry point, lifecycle, autopilot monitoring
├── ChatWindow.cs                # IMGUI chat interface
├── TelemetryHUD.cs              # Compact telemetry overlay
├── claude/
│   ├── ClaudeClient.cs          # HTTP client, conversation loop, blocking tool support
│   ├── ToolDefinitions.cs       # 80+ tool JSON schemas (OpenAI format)
│   ├── ToolExecutor.cs          # Routes tool calls to bridge handlers
│   └── SystemPrompt.cs          # KSP copilot system prompt with workflows
├── bridge/
│   ├── MechJebBridge.cs         # Reflection-based MechJeb2 integration
│   ├── TelemetryProvider.cs     # Vessel stats, orbit, delta-v, resources
│   ├── FlightController.cs      # Warp, staging, SAS, maneuver nodes, auto-transfer
│   ├── TransferCalculator.cs    # Hohmann transfer math (phase angles, timing)
│   ├── CraftFileManager.cs      # Part browser, craft files, craft creation
│   ├── MissionPlanner.cs        # Delta-v maps, mission plans, step execution
│   ├── SceneController.cs       # Scene switching, quick launch, recovery
│   ├── ScienceController.cs     # Science, tech tree, contracts, crew, EVA
│   └── KramaxBridge.cs          # KramaxAutoPilot aircraft integration
└── config/
    └── Settings.cs              # Configuration management
```

## How It Works

- Talks to any **OpenAI-compatible API** (Claude via proxy, local LLMs, etc.)
- MechJeb accessed via **reflection** — no compile-time dependency
- Conversation persists across **scene changes** (VAB -> Flight -> back)
- **Blocking tools** (`_wait` variants) poll MechJeb status until autopilot completes
- **Autopilot monitoring** detects when MechJeb finishes and auto-notifies Claude

## License

MIT
