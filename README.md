# Capone Reborn

A remake of **Capone** (1997), a real-time strategy game about gangs in Prohibition-era Chicago, made with Godot 4
and C#. Capone is the Russian edition of *Legal Crime* by Byte Enchanters.

The remake plays on the files of the original game, which you need to own. Maps, pictures, sounds, texts and the
21-episode campaign are read from your copy when the game starts. Nothing from the original is stored in this
repository. The rules come from reverse-engineering `Capone.EXE`, so gangsters, businesses, money, police and
computer players work the way they did in 1997. The few changes for modern play are optional, each with its own
setting in `game.cfg`.

Most of the code was written by Claude, Anthropic's AI model; see [How it was made](#how-it-was-made).

Current version: **1.0.0** ([CHANGELOG.md](CHANGELOG.md)). Windows builds are on the
[Releases](../../releases) page. Licence: [GPL-3.0-or-later](#license).

## The game

You run a gang in a 1920s city. Your gangsters lean on shops and bars until they pay for protection, and
collectors carry the money back to your headquarters. You turn the companies you control into speakeasies,
gambling dens and bootlegging operations. Part of the income goes to bribes, and bribes buy favours from the
police, the FBI, the politicians and the army: raids on rival businesses, blackouts of their blocks, lawyers,
informers, permits to hire heavier gunmen. The rival bosses play the same game, and sooner or later it comes to
Tommy guns. Each episode has its own map, rivals and goal: an income to reach, businesses to own, a boss to kill,
or being the last gang standing.

## Status

Playable and in active development. All 21 campaign episodes load and play, and they are being play-tested against
the original.

Working:

- main menu, mission briefings, win and defeat screens, campaign progress
- maps built from the original city blocks, fog of war, minimap, the original cursors, sounds and voices
- every gangster type with its original stats and pictures: Thug, Pistol man, Thompson man, Terrorist, Sniper,
  Shotgun man, Hit man (hidden while he holds his fire), Thief, Cadillac (carries four gangsters), collectors and
  the Godfather
- the original orders: go, attack, force go, force attack, extort, disguise, patrol
- extortion, income and market share, collectors, business conversion, selling, repair, training centres
- bribes, favour points and the authorities' services: police raids, demolition, blackouts, FBI visits and
  informers, lawyers, legalisation
- police arrests, and gangsters who desert when they aren't paid
- computer players with the original's strategies
- victory conditions, elimination, last player standing
- the money transfer, alliances and statistics windows
- quick save and load

Not done:

- the network game, and the main menu's Settings and Developers screens
- the intro and ending videos (the original refers to them but doesn't include them)

[TODO.md](TODO.md) lists the open items and the reverse-engineering findings. [PLAN.md](PLAN.md) is the route the
work followed.

## Requirements

- **The original game**: a Capone folder with `Capone.EXE`, `Chicago.tile`, `Maps/`, `PICS/`, `Sound/` and
  `episode/`. The remake is built against the Russian release; the English *Legal Crime* hasn't been tried.
- **Godot 4.7.2, .NET edition**. The standard edition can't run C#.
- **.NET SDK 8** or newer.

The remake is developed and tested on Windows. Godot and .NET also run on Linux and macOS, but the game hasn't
been tried there.

## Getting started

1. Clone the repository.
2. Tell the game where your Capone folder is. On the first start it asks for the folder and saves it to
   `originalData/original.cfg`. You can also copy `originalData/original.cfg.example` to that name and set the
   path yourself, with forward slashes:

   ```ini
   [directory]
   originalGameDirectory="C:/Games/Capone"
   ```

   The game only reads that folder, it never writes to it. `original.cfg` is ignored by git.
3. Build:

   ```sh
   dotnet build CaponeSharp.csproj
   ```

4. Run:

   ```sh
   godot --path .
   ```

   Here and below, `godot` is the Godot .NET executable (on Windows `Godot_v4.7.2-stable_mono_win64_console.exe`).
   You can also open the folder in the Godot editor and press F5.

Options go after `--`, for example `godot --path . -- --episode=4 --skip-briefing`:

| Option | Effect |
|---|---|
| `--episode=N` | start episode N (1–21) |
| `--skip-briefing` | go straight into the episode, without the menu and the briefing |
| `--speed=N` | run the game N times faster |
| `--pathfinding=astar` | use the A* path search for this run (see [Settings](#settings-and-differences-from-the-original)) |

## Playing

Episode 1 teaches the basics. Select a gangster and right-click a company showing the "Открыто" (open) sign to
extort it, then watch your income grow as collectors bring the money home. The chart on the right splits the
income between savings and bribes. The authorities' services show up on the bottom panel once you have bribed
enough and earned their favour points.

| Input | Action |
|---|---|
| Left click | select a gangster, a building or a headquarters |
| Left drag | select several gangsters |
| Right click | order: go there, attack what you clicked, or go to a business |
| Arrow keys, or the mouse at a window edge | scroll the map |
| Mouse wheel | zoom |
| `+` / `-` | game speed ×2 / ÷2 (at normal speed a game hour takes one second) |
| `Pause` | pause the game |
| `F5` / `F8` | quick save / quick load |
| `H` | show your headquarters |
| `G` `A` `E` `D` `O` | orders: Go, Attack, Extort, Disguise, Patrol |
| `T` `P` `M` `B` `S` `L` `K` `C` `I` | hire a Thug, Pistol man, Thompson man, Terrorist, Sniper, Shotgun man, Hit man, Cadillac, Thief |
| `R` | repair the selected business |
| `Ctrl`+digit | store the selected gangsters as a group |
| digit, `Shift`+digit | select a group (press again to show it); select it and show it at once |
| `Ctrl+G`, `Ctrl+A`, `Ctrl+I` | the money, alliances and statistics windows |

The bottom panel has buttons for every order; while you hold Shift, Go and Attack become Force go and Force
attack. The letter and digit keys follow key positions, so they work with a Russian keyboard layout too.

The game starts at the first episode you haven't won. Progress is kept in `campaign.cfg` in Godot's user data
folder (on Windows `%APPDATA%\Godot\app_userdata\CaponeSharp\`); delete it to start the campaign again. F5 saves
the running episode to `quicksave.json` in the same folder (one slot), and F8 loads it from anywhere in the game.

## Settings and differences from the original

`game.cfg` holds the remake's options and is read when the game starts. Text values need quotes.

| Setting | Default | Original game | What it does |
|---|---|---|---|
| `[map] company_chance` | `0.9` | `0.75` | chance that a building plot in a block holds a company |
| `[map] min_one_company_in_rich_blocks` | `true` | no such rule | every middle-class and city-centre block gets at least one company |
| `[movement] pathfinding` | `"original"` | `"original"` | `"astar"` plans the whole route around buildings; the original steps toward the target and walks around obstacles; speeds and walking rules are the same |
| `[movement] cars_on_forecourts` | `false` | `true` | whether Cadillacs and police cars may drive on the paved forecourts of some tall buildings |
| `[ai] brownouts` | `true` | `false` | computer players black out the block where their main enemy earns the most |
| `[ai] politician_share` | `20` | — | percent of a computer player's profit moved to politician bribes, which pay for brownouts |
| `[ai] brownout_days` | `7` | — | at most one brownout per computer player in this many game days |
| `[animation]` | | | unit animation speeds; see [docs/TUNING.md](docs/TUNING.md) |

About the brownouts: the original's code gives computer players no authority services, but players of the
original remember being blacked out by them. With `brownouts=true` a computer player pays for a brownout like you
do, and can only black out businesses it has seen.

Also new in the remake:

- The window can be resized or maximised. The 800×600 picture scales to fit, with black bars at the sides.
- During an episode the mouse stays inside the window, so scrolling at the window edges works in a window too.
- `Pause` pauses the game; the original has no pause key.
- Quick save and load with `F5` / `F8`.

The numbers behind units, businesses and rules (costs, hit points, damage, range, speed, prices, incomes, the
extortion chances, the authorities' prices) are in `Data/unit_stats.json`, `Data/businesses.json` and
`Data/rules.json`. They hold the original values, and you can edit them; see [docs/TUNING.md](docs/TUNING.md).

## How the original was studied

The rules were read from a disassembly of `Capone.EXE`. Code comments name the EXE function or table each rule
comes from (for example `sub_414A30` for the map projection, or the unit table at `0x484030`), so any behaviour can
be checked against the original. [TODO.md](TODO.md) collects the findings. Where the EXE couldn't settle a
question, behaviour was matched by playing the original side by side, and the code says so.

What is read from the original folder at run time:

| Data | Source | Reader |
|---|---|---|
| Executable tables and texts (English, Spanish, Russian) | `Capone.EXE`, by virtual address | `OriginalExe`, `OriginalTexts` |
| City-block layouts (38) | EXE table `0x47C830` | `OriginalBlocks` |
| Unit stats and names (13 types) | EXE table `0x484030`, name tables `0x494EEC` / `0x4952F4` | `OriginalUnitTable` |
| Company and business catalogue (161 rows) | EXE table `0x47DDC0` | `OriginalBusinessTable` |
| Mouse cursors | EXE resources | `OriginalCursors` |
| Tiles, sprites, portraits | `Chicago.tile` and `PICS/*.bmp` (XOR 0xCD) | `OriginalTiles`, `OriginalBmps` |
| Maps (29) | `Maps/*.map` (VER01 / VER02) | `OriginalMaps` |
| Campaign (21 episodes) | `episode/ep1..ep21.epi` (CP1251) | `EpiParser`, `CampaignManager` |
| Sounds and voices | `Sound/*.wav`, by the EXE's sound table `0x478700` | `Sound`, `UnitVoice` |

`Units/units.json` and `Buildings/companies.json` only map unit and business types to their pictures in
`Chicago.tile`. Unit type numbers are the original ones (`UnitCommon.UnitType`: 0 Thug … 12 Godfather), the same
numbers the episode files use.

## How it was made

Most of the code was written by [Claude](https://www.anthropic.com/claude), Anthropic's AI model, working in
Claude Code. That covers the C# game code, the reading of the original's rules from the disassembled `Capone.EXE`,
and the automated checks. The project's author set the goals, play-tested every change against the original game
and reported what looked or played differently. Commits made with Claude carry a `Co-Authored-By: Claude` line.

## Checks

Run these in the project folder after building. Each prints a summary and exits with code 0 when everything passes.

| Command | What it checks |
|---|---|
| `godot --headless --path . -- --smoke-test` | the original data: EXE tables, all 29 maps, all 21 episodes, sprite names, `Data/*.json` |
| `godot --headless --path . -- --unit-test[=Name]` | the rules taken from the EXE (`Tests/`); `=Vision` runs only the tests whose name contains "Vision" |
| `godot --headless --fixed-fps 60 --path . -- --scenario-test[=1,4,8] [--days=N]` | plays episodes fast (about 20 seconds) and checks the game every game hour: the money ledger, owners, hit points, collectors, police, fights, computer players, victory |
| `godot --headless --fixed-fps 60 --path . -- --save-load-test[=7] [--hours=N]` | plays an episode, saves, loads, compares the whole game state before and after, then plays on |

The scenario test doesn't touch your campaign progress. The save/load test overwrites your quick save.
`Scripts/DebugCommands.cs` has test hooks for driving the running game from outside (used with the Godot MCP
tools).

## Building a release

The release is a Windows package that players unzip and run. They don't need Godot or .NET installed, because the
.NET runtime is in the package.

1. **Install the tools**: Godot 4.7.2 .NET edition and the .NET SDK 8 or newer (see [Requirements](#requirements)).
2. **Install the export templates** once. Either:
   - in the Godot editor: Editor > Manage Export Templates > Download and Install; or
   - offline: download `Godot_v4.7.2-stable_mono_export_templates.tpz` from the
     [Godot 4.7.2 release](https://github.com/godotengine/godot/releases/tag/4.7.2-stable), then Editor > Manage
     Export Templates > Install from File. The `.tpz` is a zip: unpacking its `templates/` folder into
     `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\` works too, and the Windows files (`windows_*` and
     `version.txt`) are enough.
3. **Set the version** in `project.godot` (`application/config/version`, see [Versions](#versions)) and add the
   release to [CHANGELOG.md](CHANGELOG.md).
4. **Run the [checks](#checks).**
5. **Build**:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Godot <path to the Godot .NET editor>
   ```

   The script exports the game with the `Windows Desktop` preset in `export_presets.cfg`, adds the files below, and
   writes `builds/CaponeReborn-<version>/` and `builds/CaponeReborn-<version>-win64.zip`. Exporting from the
   editor's Project > Export window gives the game alone, without the editable files, readme and licences, so use
   the script. It takes about 20 seconds; Godot's output goes to `builds/export.log` and `builds/licenses.log`, and
   a Godot step that runs longer than 10 minutes is stopped with an error.
6. **Try the package**: unzip it somewhere else, run `CaponeReborn.exe`, pick the Capone folder, play a little.

The package holds:

- the game: `CaponeReborn.exe`, `CaponeReborn.console.exe` (the same game, with a console window showing its
  messages), `CaponeReborn.pck` and the `data_CaponeSharp_windows_x86_64` folder
- the files players may edit, next to the executable: `game.cfg` and `Data/*.json`. An exported game reads these
  from its own folder, not from inside the pack (`Scripts/GameFiles.cs`)
- `README.txt` for players, in English and Russian (from `tools/release/`), `LICENSE.txt` (this project's GPL)
  and `THIRD_PARTY_LICENSES.txt`, the Godot engine's licence notices

It holds no original game files and no `original.cfg`. On the first start the game asks the player for their
Capone folder and saves it to `original.cfg` next to the executable (or to the user data folder when the game's
folder can't be written). A game placed in the Capone folder itself finds the files without asking. Exported games
don't open the test port of the Godot MCP tools.

### Versions

Versions follow [semantic versioning](https://semver.org): `MAJOR.MINOR.PATCH`. Raise PATCH for fixes, MINOR for
new features, and MAJOR for changes that break saves or settings. The only place to change it is
`application/config/version` in `project.godot`. The window title, the log's first line, the executable's file
properties, the package names and the players' readme all take it from there. Each release is a git tag `vX.Y.Z` on
the commit it was built from.

### Publishing on GitHub

After the build, tag the commit and push it, then create the release with the zip attached:

```sh
git tag -a v1.0.0 -m "Capone Reborn 1.0.0"
git push origin main v1.0.0
gh release create v1.0.0 builds/CaponeReborn-1.0.0-win64.zip --title "Capone Reborn 1.0.0" --notes "See CHANGELOG.md"
```

Or, on GitHub: Releases > Draft a new release, pick the tag, paste the version's CHANGELOG entry and attach the zip.

## Project layout

| Folder | Contents |
|---|---|
| `boot.tscn`, `Main.cs`, `CameraController.cs` | start (asks for the Capone folder when it isn't set), episode start, map clicks, camera |
| `MapWorks/` | map building, projection, movement and path search |
| `Units/` | gangsters and cars, their orders and jobs |
| `Buildings/` | companies, businesses, headquarters, city blocks |
| `Scripts/` | game clock, combat, police, victory, windows and panels, test runners |
| `Scripts/Economy/` | players' money and services, training, computer players |
| `Scripts/Save/` | quick save and load |
| `originalData/` | readers for the original files and the EXE |
| `Data/` | editable unit, business and rule numbers |
| `Tests/` | unit tests |
| `tools/` | release build script, the players' readme, the licence notice writer |
| `docs/` | [tuning guide](docs/TUNING.md), [notes on the port from the GDScript prototype](docs/PORTING.md) |

## License

Copyright (C) 2026 the Capone Reborn contributors.

Capone Reborn is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any
later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text
(`GPL-3.0-or-later`).

The licence covers this repository: the remake's code, data files and documents. The Windows package also contains
the Godot Engine and the .NET runtime, both under the MIT licence; their notices are in its
`THIRD_PARTY_LICENSES.txt`.

## Legal

Capone Reborn is a fan project. It isn't affiliated with or endorsed by Byte Enchanters or the publishers of Capone
and *Legal Crime*. Capone, *Legal Crime* and their files, texts, pictures and sounds belong to their owners and are
not covered by this project's licence. This repository contains no files from the original game; to play, you need
your own copy.
