# TODO — improvement plan for sharptry

> Route for the next milestone (episode 1 complete, 1:1): see [PLAN.md](PLAN.md). This file is the long-term backlog.

How to get from the current C# port to a playable Capone, using what is actually in `godottry/original_game/` and `godottry/disassembled/Capone.EXE.lst`.

**Key finding:** Capone.EXE is the *Legal Crime* engine (Byte Enchanters, 1997; window class `"Legal Crime"`) with a Russian edition on top. Every UI string is stored in **English, Spanish and Russian (CP1251)** in the EXE. The game rules the projectinfo spec left open are there too: Mayor/FBI/police actions, legalization events, business upgrade/repair/sell, recruit costs, police units, the Godfather boss. Most open questions can be answered by reading strings and tables, without reverse-engineering code.

Addresses below are virtual addresses in `Capone.EXE.lst`. Grep for them; never read the whole 27 MB file.

---

## Economy brick — play-tested 2026-10-27 (PLAN M3/M4/M8)

MCP test hooks: `Scripts/DebugCommands.cs` at `/root/Node2D/Node2D/Debug` (the main scene root is `Node2D`, not
`Main`): `Status()`, `Speed(f)`, `AutoExtort(bool)`, `ExtortNearest(i)`, `CallCops(i)`, `Give(amount)`, `Companies(n)`.
The game opens on the briefing: click "Начать игру" at (252, 544) with `game_click` (or run with `-- --skip-briefing`),
then wait ~200 frames before the first `game_eval` (map loading); a failed eval breaks into the
debugger and freezes the game — restart it.

Result on episode 1 at speed 16 with `AutoExtort(true)`: 2 starting units took 29 slum businesses in 10 days
(chance ~94 %, a few refusals with hits), one collector per block, deliveries to the HQ, daily salaries; VCOND 1
won on day 10 (weekly net 505 400 > 500 000) with the success text. `CallCops(0)`: car from the nearest station,
arrest, jail, car leaves. `Give(-500000)`: the unpaid Pistol man resigns; interest is refused past the overdraft
(the original also charges it through Spend, 0x430A8A), so "out of funds" repeats every 1008 min — as in the original.
Fixed: the clock kept firing events after the win paused it. The RE facts below are in
`godottry/disassembled/annotate/hand_notes.py` (commented listing regenerated), `godottry/CLAUDE.md` and the skill.

Still to do from this brick:
- On a win only the clock pauses; units keep walking (end screen is R8/M7).
- Cops at attempt 10 not seen naturally (slum chance ~94 % rarely refuses 10 times); `CallCops` covers the chase.

## Panel texts — R10/M5 (2026-10-27)

`Scripts/PanelInfo.cs` draws the bottom-panel texts like the original (screen 800×600, GAMEBACK 800×220 at y 380):
- Text routine `sub_40F126(hdc, text, x, y, maxWidth, font, colour)`: shadow 0x101010 at +2,+2, text cut to maxWidth;
  colours 0 green 00FF00, 1 red D64A4A, 2 yellow FFF77B (default), 3 light green 8CEFC6, 4 brown 524218;
  colour ≥ 100 = centred, colour − 100. Fonts (`sub_41AB73`, created in WinMain at 0x41A0D3, Arial, Cyrillic):
  0 = 14 px/400, 1 = 16/500, 2 = 24/700, 3 = 16/700, 4 = 28/700 (cell heights).
- Finance `sub_40A245`: cash (HQ vt+60h) at (600, 394) font 2 yellow, as "%sK" of cash/1000 from 20 000 000;
  weekly net income (vt+64h) at (600, 425) font 3 yellow; width 105. Numbers via `sub_408AD0` = digits grouped by spaces.
- Info `sub_40A997` at x 320, width 165: name (vt+18h) y 395 font 2, colour by relation vt+E4h (own green, enemy red,
  ally yellow, other light green); kind y 419 font 3 (unit type text 5,1–11/87; company: legal → district 5,15–18,
  business → type 5,19–23); own HQ: "Headquarters" + market share of types 1–4 at y 434/449/464/479 font 1 green;
  own company: Income y 437, Uncollected y 455 (not support), Market share y 470 (types 1–4); own unit: Salary y 437
  (if ≠ 0), Collector/Thief "Money carried" y 455; others: "Owner: %s" y 437 (yellow ally, red otherwise).
  HP bar: black 233..318 × 493..502, fill from 235, 81 px at 100 %, green > 50 %, yellow > 25 %, red.
- Open: action buttons and recruit progress bars (rects at 0x476BC8: 546..680 × 447..553, Blood.bmp source),
  the "Expenses" chart (green bars per business type, vt+C0h), the text 5,31/5,32 meaning (enemy/ally HQ).

Also fixed: a win now pauses the scene tree (units acted in physics frames after the clock stopped); units created in
code (base squad) get random personal names like `Unit::Unit`.

## Episode flow — R8/M7 (2026-10-27)

`Scripts/EpisodeScreens.cs` (`BriefingScreen`, `EndScreen`, `CampaignProgress`), `Scripts/OriginalFonts.cs`, `Main`:
- Briefing `sub_464AE0` (texts read by `sub_4644A1` into 76-byte records at 0x4E3A2C: +0 SCENARIONAME, +4 14 STORYTEXT
  lines, +3Ch 3 OBJECTIVETEXT lines, +48h MAP name): ScenDisp.bmp; title centred (410, 70) font 4 yellow; story
  (183, 109 + 15i) and objective (183, 357 + 15i) font 3 green, width 525; map name (179, 421) font 2 green; one
  7-px mark per finished episode on the bar at x 734 (from ScenDisp 734,412–771,419, upwards from y 405); buttons
  from ScenDiBu.bmp (200×42; rows: start hover 0, start pressed 42, back hover 84, back pressed 126, start normal 168)
  at (152, 523) and (500, 523). `fnPrintText_0(hdc, text, x, y, width, font, colour)`: font ≥ 100 = centred;
  colours 0 green, 1 red, 2 yellow, 3 blue, 4/5 brown (5 without shadow).
- Result `sub_43C1B2`: the first met VCOND ends the episode; SUCCESSTEXT if it is the local player's, else FAILURETEXT,
  posted as message kind 3 / 2 (`sub_408A70` → `sub_41D3E0`). `sub_43C930`: on a campaign win episode + 1, furthest
  episode = max, `fnWriteConfig`; music `sub_408BE2(0 win / 4 loss)`, sound 24/25; end screen `sub_43CAB5`:
  GameWin/GAMEOVER.bmp, button sheet GameWinB/GameOvBu (283×42 "Back to menu" at 259, 527: row 0 hover, row 1 normal;
  rows 2–4 = tabs Income/Money/People), animation GameWinA/GameOvAn.
- Open: the statistics chart in the end screen's box (`sub_43CCD0`; our box shows the result text for now), the
  animations, music, the main menu (briefing "Back" quits), story scrolling past 13 lines (`sub_406D24` scroller).
- Command line: `--skip-briefing` starts the episode at once (MCP tests); `--episode=N` overrides the saved progress.

## Business conversion — R5/M6 (2026-10-27)

`Scripts/Economy/Businesses.cs` (`AuthorityItems`, `BusinessRules`), `Company.StartConversion/SetRow/Sell`,
`Scripts/ActionBar.cs` (replaces the `CenterButtons` stub and its empty scene buttons, which swallowed clicks):
- **Authority services** (table 0x488268, 54 × {word authority, dword price, float points}; authorities 1 army,
  2 FBI, 3 politician, 4 police): player vt+B8h (sub_4310A0) = available when money given to the authority ≥ price and
  favour ≥ points; vt+BCh (sub_4310F6) uses it, paying the points. This replaced the wrong "items owned" model
  (`Organisation.CanUseItem/UseItem`; the police lawyers 24h/27h/29h use it).
- **Tier** sub_4212C2 by row district (+24h) and price: slum < 100 000 / < 200 000, middle < 750 000 / < 1 000 000,
  center < 1 500 000 / < 2 000 000 → 0/1/2. **Licence** by (district, tier): slum –/23h/28h, middle 25h/2Ah/30h,
  center 2Eh/33h/1Fh (politician); district 3 needs none. vt+CCh (sub_431244) = licence available; vt+D0h (sub_4313C6)
  = its points (menu text 30,9/10/11).
- **Menu** sub_421370 (own company): legal → "Upgrade to … biz" (actions 3–7, icons Chicago.tile 0xADC/0xAE2/0xAE0/
  0xADA/0xAE4, the next tile = "down"); a type → up to 5 rows (same grade, district 3 or the block's, available),
  action 1Eh + row, icon 0xAD4; "Back" action 8 icon 0xADE; business: Repair (action 1, icon 0xAD6, cost = missing HP
  × vt+160h) when damaged, Sell (action 0Bh, icon 0xD47, price × 0.5) when undamaged; "primary training center" for
  support rows with vt+1A8h = 4. Slots: 43×43 from screen (232, 505).
- **Conversion job** sub_427050 (object sub_427000, dispatch sub_421CF5): CanAfford(price) else text 25,1; licence
  UseItem else text 25,0 (support rows exempt); Spend(price); company busy; done after catalogue +1Ch minutes (so +1Ch
  = build time); rows with +2Ch also use politician service 1Fh; then SetRow.
- **SetRow** sub_422070: HP to the new max if full; another player opening a row with +20h = 2 → "%s founds a %s."
  **Sell** sub_421E40 case 11: Deposit(price × 0.5), random legal row of the grade (fnSelectBuildingRandomly), owner none.
- Open: repair job (sub_4289A0), training center (case 12), raids/demolition/brownout on other players' companies
  (items 14h/18h/1Dh, 26h/2Bh/2Fh/32h…), freezing, the original tooltip line (texts probably go to the message bar),
  button hover animation (12 frames).

## Recruiting — R9/M9 (2026-10-27)

`Scripts/Economy/Training.cs`, `Scripts/RecruitPanel.cs`:
- **Unit type 11 is the Thief, 10 is unused** (panel name switch at 0x40B131, recruit action 8 → type 0Bh); the enum
  had them swapped. 6 and 10 show "Unknown".
- Recruit menu sub_431FD3 (HQ vt+ECh), built every frame for the local player (sub_40A660) when the primary training
  center exists and is idle: actions 0–8 = Thug, Pistol, Thompson, Terrorist, Sniper, Shotgun, Hitman, Cadillac,
  Thief at slots 6–14 (30×30, x 730 / 760, y 392 + 33k, table 0x476B48; slots 0–5 are 43×43 at (230 + 43i, 504),
  table 0x476B18); icons Chicago.tile 3Ah/3Ch/3Eh/34h/40h/9B6h/AB2h/AE7h/D8Bh (+1 = down, bitmap 47 = Recruit.bmp);
  shown if the army service (type → service table 0x483790: Pistol 0, Thompson 6, Terrorist 4, Sniper 1, Cadillac 5,
  Shotgun 2, Hitman 3, Thief 13h (politician)) is available; texts 20,17–25.
- Training job (class 16h, sub_427505): time = unit table +24h (600 / 1050 / 1200 min) / (1 + own training centers,
  sub_431DF3); at the end CanAfford(price) → UseService → Spend(price) → unit (or bare Cadillac) on a free cell next to
  the center; arrive sound by type. Nothing is paid up front. Primary center sub_431E63: chosen one, else the HQ
  building (class 9), else an own training-center business (catalogue +28h = 4).
- Progress: Blood.bmp (6 columns × 60×210, animated) rises over 728..789 × 390..589 by progress × 210; the job's
  button stays down. Conversions (class 15h) instead show GameButt.bmp's frame + coin bar over the six action slots
  (frame 0,0–258,43 at (230, 504), fill 0,63 up to 252 px at (233, 507)) — TODO.
- The recruit-price table 0x4884EC / 0x480E18 differs from 0x484030 for the Collector (15 000 vs 5 000) and type 6.
- Open: "set as primary training center" (company command 12), the conversion progress bar, Cadillac passengers.

## Bribe shares — "Расходы" chart (2026-10-27)

`Scripts/SharesPanel.cs`: five coin bars (GameButt.bmp 16,43, 20 high) at x 546..680, rows y 447/472/499/526/554
(table 0x476BF8) = savings, army, FBI, politician, police (`Organisation.Shares` 0–4). Dragging a row (sub_411F84 /
sub_413350 / sub_413A04) sets its width (0–134) and sub_409C60 rebalances: piecewise scale with knots (0,0) (49,20)
(88,40) (108,60) (122,80) (134,100) (inverse switches at 21/41/61/81); the other bars are scaled to the rest of 100 %
(a quarter each when all zero), a shortfall goes to the next row (row 0 after the last); shares written via
sub_414CB0. Hover (sub_40C2C2) prints texts 5,63/64/67/70/73 at (232, 555) and the favour 5,65–75 at (232, 573);
that line is also the panel's status line for order hints (5,40–62) — TODO: show button texts there instead of
OS tooltips. MCP-tested: 22.6 % to the police → 11 745 bribed, 2.4 favour points by day 6.

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
