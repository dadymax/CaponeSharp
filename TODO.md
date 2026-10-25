# TODO — improvement plan for sharptry

How to get from the current C# port to a playable Capone, using what is actually in `godottry/original_game/` and `godottry/disassembled/Capone.EXE.lst`.

**Key finding:** Capone.EXE is the *Legal Crime* engine (Byte Enchanters, 1997; window class `"Legal Crime"`) with a Russian edition on top. Every UI string is stored in **English, Spanish and Russian (CP1251)** in the EXE. The game rules the projectinfo spec left open are there too: Mayor/FBI/police actions, legalization events, business upgrade/repair/sell, recruit costs, police units, the Godfather boss. Most open questions can be answered by reading strings and tables, without reverse-engineering code.

Addresses below are virtual addresses in `Capone.EXE.lst`. Grep for them; never read the whole 27 MB file.

---

## 0. What exists vs what sharptry uses

| Source | Contents | Used by sharptry now |
|---|---|---|
| `Capone.EXE` | Tile blocks, unit stat table, .epi/.map parsers, all texts ×3 languages, sound table, cursors (resources), rules | Tile blocks, unit table, unit names |
| `Chicago.tile` | 3000+ tile rows incl. unit animations, **action-button icons** (rows ~102–123, 2709–2830), signs, avatars | Map tiles, unit/company sprites, avatars, selection cursor |
| `PICS/*.bmp` (91) | Map blocks, GAMEBACK, GAMEBUTT, Menu, Recruit, Blood, **MAINMENU, NEWGAME, ScenDisp, GameWin/GameOver/GameRank, HelpWin, InfoWin, MoneyWin, Faces, Signs, Car, Park, godf, CREDIT…** | Map blocks, GAMEBACK, GAMEBUTT, Menu |
| `Maps/*.map` (29) | VER01 and VER02 maps | All (via the episodes) |
| `episode/ep1..21.epi` | 21-mission Russian campaign: texts, players, VCONDs, named units, BRIBEADVANCE | Parsed fully; texts not shown yet |
| `Sound/*.wav` (161) | Unit voices, weapons, business, police, FBI, messages, UI | Nothing |
| `disassembled/orig_unit_handheld_data.txt`, `../extracted_unit_data.md` | Unit costs/army points, partial unit struct | Costs copied into `units.json` |

---

## 1. Data correctness — done

Everything below is read from the original files at runtime; `--smoke-test` checks it (0 failures).

- [x] **.epi parser** (`EpiParser`): CP1251, `EPI_VER10`, keyword order of `sub_4670A5` reported, PLAYER `id kind hasBase name`, VCOND types 1–9 with the EXE's value counts, UNIT 6 ints + name, ALLIANCE, BRIBEADVANCE `player authority points money`, `{…}` text blocks. All 21 episodes parse without errors.
- [x] **Unit types = original numbers** (`UnitCommon.UnitType`): 0 Thug, 1 Pistol, 2 Thompson, 3 Terrorist, 4 Sniper, 5 Cadillac, 6 police car?, 7 Collector, 8 Shotgun, 9 Hitman, 10 Thief, 11 Sergeant?, 12 Godfather. The UNIT line passes its type straight to the constructor `sub_42A150`; type 5 spawns a Cadillac with 4 Thompson men (loop at 0x467F7A).
- [x] **Unit table** at 0x484030 (13 × 52 bytes; `OriginalUnitTable`): damage vs units/buildings, hit %, HP, range, ticks per tile, cost, vision. Costs match `orig_unit_handheld_data.txt`. Names (EN/ES/RU) from 0x494EEC / 0x4952F4.
- [x] **Tile blocks** from the EXE table at 0x47C830 (38 × 16 bytes; `OriginalBlocks`). `bldngs_parsed.txt` removed.
- [x] **All 29 maps** load and build (VER01 and VER02 have the same layout).
- [x] **Sprite names**: `units.json` rewritten against Chicago.tile (real names like "Sub", "Snipper attack", "Shotgun man go", "car", "Godfather"); `name@n` picks the n-th tile of a repeated name. Avatars by name.
- [x] **Real campaign**: `original_game/episode/ep1..ep21.epi`; `--episode=N`. `campaigns/` removed.
- [x] Bases go to players with `hasBase` in PLAYER order (start points shuffled for RANDOM 1); every base starts with a Thug and a Pistol man (base setup `sub_404098`). FUNDS goes to every player.

Still open from this part:
- [ ] Unit types 6 and 11 are identified by sprites only (police car / "Police Batman"); 10–12 share identical filler stats (cost 150000, no damage) — real Thief/Godfather/police behaviour is code, not table data.
- [ ] Unknown table fields: +0x00 float, +0x1C second price, +0x24 (600/1050/1200, probably training time), +0x26 (2 = area attack), +0x28 (Thompson 50), +0x2E (Shotgun 300), +0x30 (Thompson 1).
- [ ] Game clock: `UnitCommon.OriginalTicksPerSecond` = 60 (60 ticks per tile → 1 tile/s). Now backed by `sub_416790`, which returns 60 and has 46 callers that multiply it (×60, ×30) — very likely the ticks-per-second constant. Confirm where the main loop advances ticks.
- [ ] Attack rate and army favour are still from `units.json` (handheld data); find them in the EXE.
- [ ] Thief walk frames and the Terrorist attack frames are not named in Chicago.tile (idle/walk used as fallback). The EXE's per-type animation tile table is not located yet.
- [ ] UNIT flags: A = 0 clears the unit's value (vtable+0x174), B → vtable+0x1EC (stationary guard?). Stored on `Unit.EpiFlagA/B`, not used.
- [ ] VCOND 7 uses unit type 13 (not a table entry, probably "any unit"); VCOND 2/5/6/8 company-type numbers not decoded.
- [ ] BRIBEADVANCE authority numbering (1–4) not verified; stored on `Team.AuthorityFavour/AuthorityMoney`.
- [ ] Company stats (`companies*.json`: incomes, names, building HP) are still guesses, not from the EXE.
- [ ] Episode texts (briefing/success/failure) are parsed but not shown yet (§3).

## 2. Text and localisation (P1)

- [ ] **String extractor.** Strings are stored as EN / ES / RU groups referenced by pointer tables (e.g. 0x49E9C4). Produce a Godot translation CSV (`key,en,es,ru`) and use `TranslationServer`. Default to Russian.
  Cover:
  - unit names (0x493E1A)
  - business classes ("Legal/Slum", "Legal/Middle class", "Legal/Center", Bootlegging…, "Headquarters")
  - info-panel lines ("Salary: $%s", "Income: $%s", "Uncollected: $%s", "Market share: %3.0f%%", "Money carried", "Owner: %s", "Extort: %3.0f%%")
  - action labels ("Attack/Extort/Destroy %s", "Upgrade to … biz", "Repair ($%ld)", "Sell for $%ld", "Recruit … ($%s and %1.0f Army favour points)")
  - messages ("Cops raid your business !", "$%s stolen from %s.", "… has been legalized.")
  - tips and help screens
- [ ] **Don't commit extracted game text or images.** Extract at startup or into a git-ignored `generated/` folder (same rule as `original_game/`).

## 3. Screens and UI from PICS (P1)

- [ ] Main menu `MAINMENU.bmp` + buttons `MAINMEBU.bmp`; new game `NEWGAME.bmp`/`NEWGAMEB.bmp`; preferences `PREFEREN.bmp`/`PREFERBU.bmp`.
- [ ] Mission briefing `ScenDisp.bmp`/`ScenDiBu.bmp` showing SCENARIONAME, STORYTEXT, OBJECTIVETEXT, TIPTEXT from the .epi.
- [ ] End screens: `GameWin.bmp`/`GameWinA`/`GameWinB` (SUCCESSTEXT), `GAMEOVER.bmp`/`GameOvAn`/`GameOvBu` (FAILURETEXT), `GameRank.bmp`, `AllyWin.bmp`.
- [ ] In-game windows for the menu stubs: `HelpWin.bmp` (tips), `InfoWin.bmp` (statistics/messages), `MoneyWin.bmp` = the **spending window** with the bribe sliders (see §5).
- [ ] Bottom panel, as specified in `projectinfo/gui.md`:
  - finances block
  - Recruit panel (`Recruit.bmp`)
  - progress bar (`Blood.bmp`)
  - **action buttons from Chicago.tile**: go/kill/guard/stay/buy/upgrade/extort rows 102–123 and per-unit go/attack buttons 2709–2830. This answers "which buttons, which icons".
- [ ] Portraits `Faces.bmp`, business signs `Signs.bmp`/`Signs2.bmp`, Cadillac `Car.bmp`, `Park.bmp`, `hq.bmp`, `open.bmp`, Godfather `godf.bmp`/`godfkl.bmp`, credits `CREDIT.bmp`/`CREDITBU.bmp`.
- [ ] **Mouse cursors** `ArrowCur`, `GotoCur`, `AttackCu`, `ProtectCu` live in the EXE's PE resources (loaded in `fnLegalCrimeMainWindowMain`, 0x419F10). Extract them with a small PE resource reader and use them via `Input.SetCustomMouseCursor`.

## 4. Sound (P1 — cheap, big effect)

- [ ] Load WAVs at runtime from `original_game/Sound` (`AudioStreamWav.LoadFromFile`); add a `SoundManager` autoload. The EXE's id → file table starts at 0x4779B8.
- [ ] Unit voices per type: `<Unit>Arrive / Click / ClickEnemy / Die / Idle1-5 / Order / OrderHurry / Resign` for Thug, Pistol, Shotgun, Thompson, Sniper, Bombman, Hitman, Thief, Godfather.
- [ ] Weapons: `Gun`, `Machgun`, `Rifle`, `Shotgun`, `bang`, `Hit`, `CRASH`.
- [ ] Business: `Bootlegging/Entertainment/Gambling/TroubleShooting/CollectionCenter/TraningCenter/HQ/Business`. Capture outcome: `BusinessPay1-6` (paid) vs `BusinessRefuse1-6` (resisted — hook into `CompanyResistance`). Collector hits: `CollectorHit1-3`.
- [ ] Police/FBI: `PoliceArrest1-3`, `PoliceRaid`, `POLICE_S`, `FBIInforms`. Cars: `NewCar`, `CarGo`, `CarClick`. UI: `ButtonClick`, `OverButton`, `Click`, `select`, `Message1-8`, `OutOfFunds`, `Permit`, `Welcome`, `NewGame`, `WinEffect`, `LoseEffect`.

## 5. Gameplay rules recovered from the EXE (P2)

These replace the open questions Q2–Q7 in `godottry/projectinfo/aithoughts.md`.

- [ ] **Authorities = spending sliders.** "%1.0f%% of income is used to bribe police / army / politician / FBI". Favour accumulates as points ("Police own %1.0f points worth of favour"). The fourth authority is the **politician (Mayor)**, not the "senate". Find the income-to-points formula from the code that formats those strings.
- [ ] **Police actions**: "Ask Cops to raid the business (N police favour points)", "Ask cops to arrest gangster". Police are also on the map: unit "Sergeant", building "Police Station". Episode 1 tips say the police arrest your gangsters. Needs police units + arrest logic.
- [ ] **Army**: "Recruit gun man / shotgun man / thompson man / sniper / hitman / terrorist / thief / cadillac ($X and N Army favour points)". This matches `ArmyFavor` in `units.json`; enforce it on recruit. "Bribe the army to find and recruit a sniper."
- [ ] **Mayor actions** (politician points):
  - legalize beer / wines / spirits / gambling / bookmaking / lottery / bordellos / peep shows / dirty magazines (each triggers a "… has been legalized." event)
  - arrange brownout
  - demolish building
  - start a snitch / evil rumour
  - start (bribing) investigations
  - announce player death
- [ ] **FBI actions**: "Ask FBI for snitch", "Ask FBI to 'talk' to boss"; events "FBI 'visits' you …", "Former FBI agent reveals that %s used to be a snitch …".
- [ ] **Business**:
  - company classes Legal/Slum, Legal/Middle class, Legal/Center
  - Extort %
  - "Upgrade to Bootlegging/Entertainment/Gambling/Troubleshooting/Supporting biz"
  - upgrade cost in $ + Police favour
  - Repair ($), Sell for $
  - "Set as primary training center"
  - Market share
  - variant names and prices: find the table used with "Upgrade to %s ($%s …)" (pointer table at 0x49E9C4); the names are probably in the Russian string set
- [ ] **Units**:
  - Hitman "Disguise" / "(Disguised)" is a player-toggled state
  - Thief: "%s stole $%s."
  - Cadillac recruitable
  - **Godfather** boss unit (UNIT type 12, its own sounds and pictures; VCOND 3 `p 12` = kill the boss)
  - Shotgun man = Raid
- [ ] **Calendar**: "Bang: Full year!" and monthly "Salary $%ld" suggest a game calendar with periodic salary payments. Look for the code that formats these strings.

## 6. AI players (P3)

- [ ] AI kinds 2/3/4/6 from PLAYER. Find the AI update loop, starting from the code that reads `PLAYER`'s kind field (`sub_404390` calls in `sub_4670A5`). Until then: a simple scripted AI (capture nearby neutrals, recruit when rich, attack HQ).
  Known so far: `sub_404390` = Player::Player (0x80 bytes, vtable off_47613C, class id 7), called with `isComputer = (kind != 0)`, the name and the PLAYER numbers. The other class-7 vtables (off_475E3C … off_4763C8, 27 slots each) are probably the player subclasses — start there to find how kinds 2/3/4/6 differ.

## 7. Tooling

- [ ] Remaining EXE readers (strings EN/ES/RU, sound table, cursors), following `OriginalExe` / `OriginalBlocks` / `OriginalUnitTable` (runtime reading, no generated files committed).
- [x] Headless smoke test: `-- --smoke-test` (EXE tables, every map and episode, sprite names).
- [x] Commented listing: `godottry/disassembled/Capone.EXE.commented.lst`, generated by `godottry/disassembled/annotate/annotate_lst.py`. Add new findings to `annotate/hand_notes.py` and regenerate (command in the script's docstring).

## Out of scope

- Multiplayer (DirectPlay/IP: `JoinGame`, `STARTNET`, `SyncWin`, `fnSetIPAddresses`).
- Videos `Welcome.avi` / `Final.avi` (referenced but not shipped).
- Licence/beta/expiry checks, Byte Enchanters sales links, the English "Lesson 1–11" tutorials (Legal Crime only).

---

## To fix (found while commenting the EXE listing, 2026-10-26)

Source: `godottry/disassembled/Capone.EXE.commented.lst` (search the address).

- [ ] **Cadillacs take building damage.** `Unit::GetMinDamage`/`GetMaxDamage` (`sub_42C890`/`sub_42C960`) use `DamageMin/Max` only when the target is a unit (class 5) that is not a Cadillac (type 5); buildings **and Cadillacs** get `BuildingDamageMin/Max`. `CanAttack.RollDamage` currently uses unit damage against Cadillacs.
- [ ] **Hit man special case.** In the same getters a Hit man (type 9) returns a fixed 20 damage after a time check against unit field +0D0h (probably: not disguised / recently revealed); otherwise the table's 161–162. Decode the check and model it with the disguise (§5).
- [ ] **ALLIANCE is mutual.** The loader calls `a->SetRelation(b, 2)` and `b->SetRelation(a, 2)` (0x467714 / 0x467721). `CampaignManager.InitAlliancesFromScenario` sets only `from → to`; set both directions. (The projectinfo spec calls it one-way — the EXE says otherwise.)
- [ ] **Random personal names.** `Unit::Unit` names every unit except the Collector with `fnGetRandomParameters(world, 1, player)` (0x42A27E); the Collector gets text 1Dh/0Dh. Find the name list and give spawned units a personal name (episode names still override via unit vtable +178h `SetName`).
- [ ] **Unit HP field.** The unit stores its current HP at +20h (set to MaxHP at creation; vtable +0F4h returns it, +1A8h sets it, +1A0h sets 0). Matches `CanBeAttacked.HP`; no change needed, recorded for reference.

## To remake (found in manual test, 2026-10-26)

- [ ] **MENU button is wrong.** `GuiMenu` uses the whole `GAMEBUTT.bmp` and cuts it in half vertically. The button is only a small part of the bitmap: two "Меню" images side by side, normal and pressed (see `godottry/projectinfo/image-1.png`). Find the exact rectangles (Chicago.tile record or EXE coordinates) instead of guessing. Check the `Menu.bmp` cutting the same way.
- [ ] **Attack range against companies.** Units shoot companies from their full attack range. In the original, ranged attacks only work against units; a company can only be attacked from a neighbouring tile. Ranged units must walk up to the building.
- [ ] **Company logos are wrong.** The signs shown on companies (`companies.json` sprite names, per owner/type) don't match the original. Rebuild the mapping from Chicago.tile names (e.g. "Russian open sign", "Own protected business", "Own Bootlegging sign", "Power off … sign") and check against the original game.
- [ ] **`System.ObjectDisposedException: Cannot access a disposed object. Object name: 'Capone.CanBeAttacked'`** during play. Likely cause (not verified): a unit's `CanAttack.Target` still points at the `CanBeAttacked` of a company whose `CompanyCommon` was replaced by `Company.SetType` (capture) and freed, or of a dead unit. Targets need `IsInstanceValid` checks and must be cleared when the component is replaced.

## Known issues (carried over)

- [ ] **Company avatar not updating** when selecting a different company: check `SelectionIcon.UpdateIcon` on reselection.
- [ ] **Avatar tile index**: `AvatarTileIndex` in `companies2.json` is the 0-based tile index (Chicago.tile line − 58).
- [ ] **HQ placement**: when no base company is next to a start point, the HQ is placed wrongly (`TileMapGenerator`). Needs fallback logic.
- [ ] **RID leaks at exit** with `--headless --quit-after`: harmless.
- [x] ~~Group (rectangle) select~~: fixed in the C# port.
- [x] ~~PICS dir check with `res://L:/…`~~: not present in the C# port.
