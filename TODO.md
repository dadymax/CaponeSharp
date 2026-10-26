# TODO — improvement plan for sharptry

> Route for the next milestone (episode 1 complete, 1:1): see [PLAN.md](PLAN.md). This file is the long-term backlog.

How to get from the current C# port to a playable Capone, using what is actually in `godottry/original_game/` and `godottry/disassembled/Capone.EXE.lst`.

**Key finding:** Capone.EXE is the *Legal Crime* engine (Byte Enchanters, 1997; window class `"Legal Crime"`) with a Russian edition on top. Every UI string is stored in **English, Spanish and Russian (CP1251)** in the EXE. The game rules the projectinfo spec left open are there too: Mayor/FBI/police actions, legalization events, business upgrade/repair/sell, recruit costs, police units, the Godfather boss. Most open questions can be answered by reading strings and tables, without reverse-engineering code.

Addresses below are virtual addresses in `Capone.EXE.lst`. Grep for them; never read the whole 27 MB file.

---

## Next session — savepoint 2026-10-26 (economy brick, PLAN M3/M4/M8)

Committed as a savepoint: compiles with 0 warnings, `--smoke-test` green, episode 1 starts with the economy
("232 businesses in 313 blocks, 2 police stations"). **Not play-tested yet.** Resume here:

1. Add a small debug node (e.g. `Scripts/DebugCommands.cs`, child of Main) for MCP `game_eval`: `Status()` (clock,
   cash, net/gross income, owned companies, collectors), `ExtortNearest(i)`, `AutoExtort(on)` (idle player units
   extort the nearest unowned business).
2. MCP test episode 1 at `GameClock.Speed = 16`: a business pays → its block spawns a collector at the HQ →
   collector waits 60 min, visits the owner's companies in row order, delivers to the HQ → `Organisation.Cash`
   and `NetIncome` grow → VCOND 1 (net income > 500 000) wins. Also check refusals → one hit per hour, cops at
   attempt 10 (slum), police car chase/arrest/jail, salaries paid daily, resign when broke.
3. Fix what the test shows, then mark PLAN M3/M4 done and commit.
4. Add the new RE facts below to `godottry/disassembled/annotate/hand_notes.py`, regenerate the commented listing,
   and add the time/economy facts to `godottry/CLAUDE.md` and the capone-godot skill.

Known gaps in the brick (do later):
- `CanAttack.HitOnce` is a simplification of Unit::Attack `sub_42BD15` (bursts, ammo, Cadillac).
- `ExtortOrder.CanExtort`: which unit types may extort is a guess (all fighters except Collector/Cadillac/police).
- Company portraits in the panel use placeholder "info …" tiles per grade (`SelectionIcon.CompanyPortrait`).
- HQ building HP unknown (class 9, `sub_415090`); HQs can't be attacked yet.
- Police car stats: unit type 6 table row is used (TicksPerTile 20); the original car object is `sub_4459D0` (0x44 bytes).
- Lawyers (HQ items 0x24/0x27/0x29) and informers (0x2C/0x31/0x34, `sub_42C503`) need M6 items; `Organisation.Items` is empty.
- UNIT flag A = 0 → salary 0 is assumed (`CampaignManager.SpawnUnit`).
- Smoke test leaks nodes (RID warnings at exit).

### Economy facts recovered (this brick)

- **Company** (vtable off_4803CC, handler sub_420ED5): hourly — cache block income flag, heal 5 HP if unowned or
  legal; 441× per week (slot 22 min + random 0–21) — uncollected += income / 441 (upkeep rows charge the owner);
  weekly — percent = 100, unowned lose uncollected. Income = catalogue × percent × damage factor (1 at full HP,
  0 at ≤ 75 %) × district (legal: 1 / 1.25 / 1.875) × owner's type factor. SetOwner sub_4211F0 → block vt+178h.
- **Extortion** sub_422FB4: base chance from income ((0,100),(25000,90),(100000,15),(1000000,0)) + unit pressure
  within 4 cells (Thug 5, Pistol/Shotgun 6, Thompson 10, Terrorist/Hitman 20; own +, others −) − 45 after the first
  attempt + 15 if owned; unowned: the owner calls the cops when attempt = threshold (slum 10, middle 5, center 0;
  Thompson +3, Terrorist/Hitman +5, Sniper −2), after that −100. Legal pays if rand(100) ≤ chance; any pays below
  15 % HP. Order sub_428C11: one attempt per hour, a refusal = attempt+1 and one hit.
- **Map placement** fnBuildingPlacementOnMapByNumber: B grade 1 and C grade 2 businesses 3 in 4, M/H grade 3
  always, P police station; fnSelectBuildingRandomly = random legal row with +24h = district or 3 (all legal rows
  are 3) and grade. Catalogue +28h = 3 marks cash-out businesses (company vt+1A8h).
- **Block** (off_47CF28): owner entries {HQ, collector, income enabled, lost time}; first company → collector at
  once (sub_41FC15); weekly sub_41FF60 replaces a lost collector one week after the loss.
- **Collector job** sub_428D62 (on arrival): pickup → wait 60 → next own company in row order (sub_429146) or the
  nearest HQ / cash-out (sub_429221); deliver → Deposit, heal, next round at max(start + 2520, now + 60); no
  companies → block forgets the owner, collector leaves; killed → owner's income in the block stops until the
  next collector's first pickup.
- **Organisation** (HQ class 6): Spend sub_430860 (cash − amount + 200 000 ≥ 0 or net ≥ 0, else "out of funds"
  20,0 + sound 24); Deposit sub_430790 (bribe shares of the profit part); UpdateStats sub_430C30 hourly; interest
  −cash/10 every 1008 min when negative; market-share curve; salaries = unit table +0x20 per week, paid daily / 7
  (sub_42A535), unpaid non-collectors resign.
- **Police** order sub_4460C5 (start sub_4461A4, step sub_4462B4): car from the nearest station (sub_446123) to the
  gangster's cell; within 5 cells → chase ≤ 12 h; within 1 → arrest (sounds 27/28); at the station → jail (unit
  vt+1A0h) unless lawyers (70/50/30 %) → release with text 28,1.

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
- [x] Game clock (R1): game time is in **minutes**; 60-minute turns, at most one per 1000 ms → 1 game hour = 1 real second at speed 1 (`GameClock`, scheduler like World::Schedule `sub_43AF10` / `sub_43ABD0`). Unit speed = tile length × 60 / TicksPerTile per second. `GameClock.Speed` (cmd `--speed=N`, keys `-`/`=`) speeds everything up for tests.
- [ ] Attack rate and army favour are still from `units.json` (handheld data); find them in the EXE.
- [ ] Thief walk frames and the Terrorist attack frames are not named in Chicago.tile (idle/walk used as fallback). The EXE's per-type animation tile table is not located yet.
- [ ] UNIT flags: A = 0 clears the unit's value (vtable+0x174), B → vtable+0x1EC (stationary guard?). Stored on `Unit.EpiFlagA/B`, not used.
- [ ] VCOND 7 uses unit type 13 (not a table entry, probably "any unit"); VCOND 2/5/6/8 company-type numbers not decoded.
- [ ] BRIBEADVANCE authority numbering (1–4) not verified; stored on `Organisation.Favour/AuthorityMoney`.
- [x] Company stats come from the EXE catalogue (`OriginalBusinessTable`); `Company` is rebuilt on it (PLAN M3). `companies2.json` is no longer read (delete it once the logos are remade); `companies.json` still maps sign types to Chicago.tile sprites.
- [ ] Episode texts (briefing/success/failure) are parsed but not shown yet (§3).

## 2. Text and localisation (P1)

- [x] **Text reader** `OriginalTexts`: `fnMessagePrep(group, index)` = 37 groups of EN/ES/RU triples set up by `sub_466DB0`; 544 texts, 152 without Russian (Legal Crime lessons/help → English fallback). Still open: a translation CSV / `TranslationServer` only if needed — the game reads texts directly. Original task:
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
- [ ] Business: `Bootlegging/Entertainment/Gambling/TroubleShooting/CollectionCenter/TraningCenter/HQ/Business`. Capture outcome: `BusinessPay1-6` / `BusinessRefuse1-5` — done in `Company.Extort` (`Sound` class). Collector hits: `CollectorHit1-3`.
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

- [ ] Remaining EXE readers (sound table, cursors). Done: `OriginalTexts`, `OriginalBusinessTable`.
- [x] Headless smoke test: `-- --smoke-test` (EXE tables, every map and episode, sprite names).
- [x] Commented listing: `godottry/disassembled/Capone.EXE.commented.lst`, generated by `godottry/disassembled/annotate/annotate_lst.py`. Add new findings to `annotate/hand_notes.py` and regenerate (command in the script's docstring).

## Out of scope

- Multiplayer (DirectPlay/IP: `JoinGame`, `STARTNET`, `SyncWin`, `fnSetIPAddresses`).
- Videos `Welcome.avi` / `Final.avi` (referenced but not shipped).
- Licence/beta/expiry checks, Byte Enchanters sales links, the English "Lesson 1–11" tutorials (Legal Crime only).

---

## To fix (found while commenting the EXE listing, 2026-10-26)

Source: `godottry/disassembled/Capone.EXE.commented.lst` (search the address).

- [x] **Cadillacs take building damage.** Done: `CanAttack.RollDamage`. `Unit::GetMinDamage`/`GetMaxDamage` (`sub_42C890`/`sub_42C960`) use `DamageMin/Max` only when the target is a unit (class 5) that is not a Cadillac (type 5); buildings **and Cadillacs** get `BuildingDamageMin/Max`. `CanAttack.RollDamage` currently uses unit damage against Cadillacs.
- [ ] **Hit man special case.** In the same getters a Hit man (type 9) returns a fixed 20 damage after a time check against unit field +0D0h (probably: not disguised / recently revealed); otherwise the table's 161–162. Decode the check and model it with the disguise (§5).
- [x] **ALLIANCE is mutual.** Done: `CampaignManager` sets both directions. The loader calls `a->SetRelation(b, 2)` and `b->SetRelation(a, 2)` (0x467714 / 0x467721). `CampaignManager.InitAlliancesFromScenario` sets only `from → to`; set both directions. (The projectinfo spec calls it one-way — the EXE says otherwise.)
- [ ] **Random personal names.** `Unit::Unit` names every unit except the Collector with `fnGetRandomParameters(world, 1, player)` (0x42A27E); the Collector gets text 1Dh/0Dh. Find the name list and give spawned units a personal name (episode names still override via unit vtable +178h `SetName`).
- [ ] **Unit HP field.** The unit stores its current HP at +20h (set to MaxHP at creation; vtable +0F4h returns it, +1A8h sets it, +1A0h sets 0). Matches `CanBeAttacked.HP`; no change needed, recorded for reference.

## To remake (found in manual test, 2026-10-26)

- [x] **MENU button is wrong.** Done: button = the 48×14 "Меню" images at (158,42) normal / (210,42) pressed, measured from the bitmap (EXE coordinates still not located); the drop-down shows Menu.bmp cell 0, hover shows cell i, item zones detected from the bitmap. Was: `GuiMenu` uses the whole `GAMEBUTT.bmp` and cuts it in half vertically. The button is only a small part of the bitmap: two "Меню" images side by side, normal and pressed (see `godottry/projectinfo/image-1.png`). Find the exact rectangles (Chicago.tile record or EXE coordinates) instead of guessing. Check the `Menu.bmp` cutting the same way.
- [x] **Attack range against companies.** Done: `CanAttack.BuildingAttackRange` = 1.5 for companies. Was: Units shoot companies from their full attack range. In the original, ranged attacks only work against units; a company can only be attacked from a neighbouring tile. Ranged units must walk up to the building.
- [ ] **Company logos are wrong.** The signs shown on companies (`companies.json` sprite names, per owner/type) don't match the original. Rebuild the mapping from Chicago.tile names (e.g. "Russian open sign", "Own protected business", "Own Bootlegging sign", "Power off … sign") and check against the original game.
- [x] **Fixed** (`CanAttack` keeps the target object and looks its health component up on each use; re-checks hostility before attacking): **`System.ObjectDisposedException: Cannot access a disposed object. Object name: 'Capone.CanBeAttacked'`** during play. Likely cause (not verified): a unit's `CanAttack.Target` still points at the `CanBeAttacked` of a company whose `CompanyCommon` was replaced by `Company.SetType` (capture) and freed, or of a dead unit. Targets need `IsInstanceValid` checks and must be cleared when the component is replaced.

## Known issues (carried over)

- [x] Clicks on GUI controls (menu, bottom panel) no longer select/deselect on the map: `Main` uses `_UnhandledInput`.

- [ ] **Company avatar not updating** when selecting a different company: check `SelectionIcon.UpdateIcon` on reselection.
- [ ] **Company portraits**: `SelectionIcon.CompanyPortrait` uses Chicago.tile "info …" pictures per grade as placeholders; find the original building → picture mapping.
- [x] **HQ placement**: the HQ now stands on the start point itself, as in `sub_404098` (a business there is replaced).
- [ ] **RID leaks at exit** with `--headless --quit-after`: harmless.
- [x] ~~Group (rectangle) select~~: fixed in the C# port.
- [x] ~~PICS dir check with `res://L:/…`~~: not present in the C# port.
