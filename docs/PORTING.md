# Porting notes: GDScript to C#

Capone Reborn started as a GDScript prototype (`GodotCapone`); this C# project began as a port of it in October 2026.
These notes record how the files map and which bugs of the prototype the port fixed. They matter only when
comparing the two versions; everything since then was written in C#.

## Layout (GDScript → C#)

| GDScript | C# |
|---|---|
| `node_2d.gd` | `Main.cs` |
| `camera_2d.gd` | `CameraController.cs` |
| `MapWorks/createTileMap.gd`, `maptilemath.gd` | `MapWorks/TileMapGenerator.cs`, `MapTileMath.cs` |
| `originalData/*.gd` | `originalData/Original*.cs` |
| `Buildings/company.gd`, `companyCommon.gd`, `companyEconomics.gd`, `companyResistance.gd` | `Buildings/Company.cs`, `CompanyCommon.cs`, `CompanyEconomics.cs`, `CompanyResistance.cs` |
| `Units/unit.gd`, `unitCommon.gd`, abilities, `collectorAI.gd` | `Units/Unit.cs`, `UnitCommon.cs`, `*Ability.cs`, `HitmanStealth.cs`, `CollectorAI.cs` |
| `Scripts/can_attack.gd`, `can_be_attacked.gd`, `canHoldMoney.gd` | `Scripts/CanAttack.cs`, `CanBeAttacked.cs`, `CanHoldMoney.cs` |
| `Scripts/teams.gd` | `Scripts/Teams.cs` (`Teams` + `Team`) |
| scenario / campaign / epi / victory scripts | `Scripts/Scenario.cs`, `Campaign.cs`, `CampaignManager.cs`, `EpiParser.cs`, `VictoryCondition.cs`, `VictoryManager.cs` |
| GUI scripts | `Scripts/GuiGameback.cs`, `GuiMenu.cs`, `SelectionIcon.cs`, `CenterButtons.cs` |
| script embedded in `Nodes/Selection.tscn` | `Nodes/Selection.cs` |
| — | `Scripts/GameInterfaces.cs`: `ISelectable`, `IGameEntity`, `IJsonConfigurable`, `JsonUtil` |

All C# types live in namespace `Capone` and use PascalCase. JSON keys (`units.json`, `companies*.json`) are unchanged; each component maps them explicitly in `ApplyJsonValue` instead of GDScript's dynamic `set()`.

Kept as is: `Scripts/mcp_interaction_server.gd` (the autoload used by the godot MCP tools; GDScript runs fine inside a C# project), data files, scenes.
Not ported: `Buildings/buildingTypes.gd` + `BuildingTypes.cfg` (marked deprecated), `Nodes/animation_player.gd` (empty, unused), `export_presets.cfg`, the duplicate `mcp_interaction_server.gd` in the project root.

## Behaviour changes vs the GDScript version

These were bugs in the GDScript code that C#'s static typing either exposed or made trivial to fix:

- **EpiParser**: PLAYER/VCOND/ALLIANCE/UNIT no longer read the keyword as their first value; tabs are separators; names with spaces are joined and quotes stripped.
- **VCOND numbers** (1–8 from .epi) are mapped to the right `ConditionType`; GDScript cast them straight into an enum with a different order.
- **units.json loading** no longer stops at `CollectorWithMoney` (which has no record), so the Cadillac now loads.
- `"Neutral": "Enemy"` sprite aliases now copy the Enemy animation set.
- `attackDamage` values written as strings (`"5"`) are parsed; GDScript silently kept the default 1.
- `Cost`, `ArmyFavor`, `visionRadius` are stored on `UnitCommon`; capture resistance now uses the attacker's cost (GDScript always got 0).
- Company income actually accrues: GDScript's `CompanyEconomics` proxy was always null.
- After a company is captured its new health component is connected to `OnHPisZero`.
- Every unit/company gets its own `SpriteFrames` (and unit collision shape); the scene sub-resources were shared, so animations overwrote each other.
- Abilities find the `Unit` (three parents up); GDScript pointed them at `UnitCommon`. `ThompsonAbility` now has its attacker reference.
- Units and companies join the groups `units` / `companies`, which Raid area damage and the collector AI query.
- Collector AI walks to companies/HQ, collects from player companies and pays into the player's account (GDScript referenced non-existent members).
- Selection: all previously selected objects are deselected; rectangle selection uses global coordinates and the rectangle centre ([TODO.md](../TODO.md) "group select not working").
- Sniper: the invented ×2 distance damage multiplier is gone; the sniper does the EXE's flat 200–250 damage.
- Combat uses the EXE table: damage ranges against units and against buildings, hit chance instead of evasion, fractional attack range.
- BMP transparency keys are replaced on the raw RGBA buffer (much faster than per-pixel calls).
- Companies dropped by the 3-in-4 random roll are freed instead of leaking.
