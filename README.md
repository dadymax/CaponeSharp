# Capone Reborn — C# version (sharptry)

C# port of the GDScript project in `../godottry/GodotCapone` (Godot 4.7, GL Compatibility, 800×600).

## Requirements

- **Godot 4.7.2 .NET edition** ("mono" build). The Steam build in `Q:\SteamLibrary\...\Godot Engine` is the plain build and **cannot run C#**.
- .NET SDK 8 or newer (installed: 8, 9, 10).
- `originalData/original.cfg` must point to the original game folder. It is ignored by git: copy `originalData/original.cfg.example` to `original.cfg` and set the path. The original files are never copied.

Build without the editor: `dotnet build CaponeSharp.csproj`
Run headless: `<Godot .NET exe> --headless --path . --quit-after 300`
Start a given episode: `<Godot .NET exe> --path . -- --episode=4` (default 1)
Check all original data (EXE tables, 29 maps, 21 episodes, sprites): `<Godot .NET exe> --headless --path . -- --smoke-test` (exit code 0 = ok)

## Original data only

The goal is a 1:1 recreation that runs on the original game files, so the repository contains no game content.
Everything is read at runtime from the folder in `original.cfg`:

| Data | Source | Reader |
|---|---|---|
| Executable tables and texts | `Capone.EXE` (by virtual address, see `disassembled/Capone.EXE.lst`) | `OriginalExe` |
| City-block layouts (38) | EXE table 0x47C830 | `OriginalBlocks` |
| Unit stats and names (13 types) | EXE table 0x484030, name tables 0x494EEC / 0x4952F4 | `OriginalUnitTable` |
| Tiles, sprites, portraits | `Chicago.tile` + `PICS/*.bmp` (XOR 0xCD) | `OriginalTiles`, `OriginalBmps` |
| Maps | `Maps/*.map` (VER01/VER02) | `OriginalMaps` |
| Campaign | `episode/ep1..ep21.epi` (CP1251) | `EpiParser`, `CampaignManager` |

`units.json` / `companies*.json` only hold what isn't located in the EXE yet (sprite names as Chicago.tile references, army favour, attack rate, company incomes).
Unit type numbers are the original ones (`UnitCommon.UnitType`: 0 Thug … 12 Godfather), the same numbers .epi UNIT lines use.

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
- Selection: all previously selected objects are deselected; rectangle selection uses global coordinates and the rectangle centre (TODO.md "group select not working").
- Sniper: the invented ×2 distance damage multiplier is gone; the sniper does the EXE's flat 200–250 damage.
- Combat uses the EXE table: damage ranges against units and against buildings, hit chance instead of evasion, fractional attack range.
- BMP transparency keys are replaced on the raw RGBA buffer (much faster than per-pixel calls).
- Companies dropped by the 3-in-4 random roll are freed instead of leaking.

## Tuning unit animations

The playtests show that some animations need hand tuning. Three places control them:

1. **Which pictures** — `Units/units.json`, `Sprites` of each unit type. For each owner (`Player`, `Enemy`,
   `Teammate`; `"Neutral": "Enemy"` copies another owner) there is an array of 11 layers, in this order:
   `idle`, `move SE`, `move NE`, `move NW`, `move SW`, `attack SE`, `attack NE`, `attack NW`, `attack SW`,
   `killed`, `killed2`. Each entry names a Chicago.tile record:
   - `"Own Thompson man attack SE"` — the record with that comment (case does not matter);
   - `"Own Thompson man idle@2"` — the third record with that comment (Chicago.tile repeats some names);
   - `"#2621"` — a record by index (here the same "Own Thompson man attack SE") (counting from the first line after the tile count, as the EXE does);
   - `""` — none: a move layer falls back to idle, an attack layer to idle / move, `killed2` to `killed`.

   The frames of a layer are the coordinate pairs of that record in Chicago.tile. The EXE's own choice per unit
   type, owner and activity is the picture method sub_42D8D0 (see `TODO.md`: e.g. the Thief walks with the Thug's
   pictures and attacks with the Hit man's, the Terrorist's attack uses his walk pictures). `-- --smoke-test`
   reports names that don't resolve. To list the record names:
   `python -c "L=open('Chicago.tile',encoding='cp1251').read().splitlines(); n=int(L[0]); t=L[n+2:n+2+int(L[n+1])]; [print(i, r.split(';',1)[1].strip()) for i,r in enumerate(t) if ';' in r]"`
   (run in `godottry/original_game`).
2. **How fast** — `game.cfg`, section `[animation]`: `idle_fps`, `move_fps`, `attack_fps`, `killed_fps` for every
   unit, and `<Type>.<kind>_fps` for one type (`Thompson.attack_fps=8`). Default 5 frames per second.
   `attack_hold_minutes` sets how long a unit keeps its attack picture between an order's hits (extortion);
   one game hour is one real second.
3. **Where** — the picture's offset from the unit's cell is the `Animation` node position in `Units/Unit.tscn`
   (0, −14). The facing (SE / NE / NW / SW) comes from the move or target direction on screen
   (`MapTileMath.GetDirection`). Death pictures play once and hold the last frame.

`game.cfg` and `units.json` are read when the game starts; restart it after a change.

## Unit and business parameters

`Data/unit_stats.json` (every unit type: cost, hit points, damage, hit chance, range, rate of fire, reload, speed,
training time, sight) and `Data/businesses.json` (the business catalogue: name, type, grade, price, weekly income,
hit points, build time, district, support kind) hold the original game's values. Edit them and restart the game; a
missing file, unit, row or field keeps the original value. Each file starts with a `_fields` list explaining the
fields. To get fresh files with the original values back, delete them and run
`godot --headless --path . -- --export-params`. The smoke test reports files that don't load or contain unknown
fields, and warns when unit costs differ from the original.

## Computer players' brownouts

The original's computer players never use the authorities' services in its code, but in play they were seen cutting
the power of the human's blocks. The remake adds that as an option in `game.cfg`:

```ini
[ai]
brownouts=true        ; false = as in the original's code
politician_share=20   ; % of a computer player's profit moved from savings to politician bribes
brownout_days=7       ; at most one brownout per computer player per this many game days
```

A computer player then blacks out the block where its main enemy (you, unless allied) earns the most, paying for it
like you would (politician favour points and bribe money). Like you, it can only target businesses it has seen with
its own or its allies' units and buildings.

## Path search

By default units find their way like the 1997 game: step by step toward the target, with a detour when blocked
(`MapWorks/MoveMap.cs`, from the EXE). `game.cfg` can switch to a planned route instead:

```ini
[movement]
pathfinding="astar"   ; or "original" (the default); text values need quotes
```

`astar` (`MapWorks/PathSearch.cs`) plans the cheapest whole path around buildings each step, with the same walking
rules (cars stay on roads, units may share cells, a building target is reached by standing next to it) and the same
speeds. With no path at all the unit falls back to the original walk. For a test run without editing the file, pass
`-- --pathfinding=astar` (e.g. with `--scenario-test`).
