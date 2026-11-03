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
arrest, jail, car leaves. (Superseded: the "refused Spend / resign / repeated out of funds" behaviour seen here was
wrong — see the Spend correction below.)
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
- Done later: action buttons (`ActionBar`), recruit progress bars (blood bar, M9), the "Expenses" chart (`SharesPanel`).
  Texts 5,31 / 5,32 are both "База": another player's HQ title at y 419, red for an enemy (company vt+0E4h = 1), yellow for
  an ally (2) — already drawn so by `PanelInfo`.

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
  Animation done 2026-10-31 (sub_43CCD0): GameWinA 100 × 100 frames at one of three random places (0x48D4D4), frames
  0–5 every 250 ms (columns 0,1,2,4,4,5), restarting at random; GameOvAn 30 × 40 frames looping at (750, 470).
  Sound: sub_43C930 plays id 18h / 19h of the screens' sound set (table 0x477A1C, loaded by sub_41D255(2)) —
  WinEffect.wav / LoseEffect.wav — done. The music is a CD audio track (sub_408BE2 → sub_460A10, MCI "cdaudio":
  track 0 won, 4 lost), not in the game files: nothing to play.
- Statistics chart (done 2026-10-29): `Statistics` (sub_460B36 / sub_460BE4) samples every player's weekly net
  income, cash and gangsters every 30 game hours (the original: 30 s of real time) into 31 averaged slots that are
  halved when full; `EndScreen` draws it like sub_43CCD0 with the Income / Money / People tabs (sub_43DB50). The result
  text shows at the top left for 15 s (message log). Unit tests `StatisticsTests`.
- Open: the animations, music, story scrolling past 13 lines (`sub_406D24` scroller).
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
- Done: the conversion progress bar (GameButt frame + coin fill over the slots, `ActionBar._Draw`).
- Done: **Repair** (action 1, job sub_4289A0 / sub_4289E8, class 1Eh): menu cost = missing HP × vt+160h, where vt+160h
  (sub_422F13) = price / max HP × 0.5 (integer division first); the job heals 5 HP (vt+ECh) and pays one vt+160h per
  game hour until full (so the menu figure overstates the real cost 5×, as in the original); the panel bar runs to
  now + missing HP × 12 min. `Company.StartRepair`, `ActionBar` slot 6. MCP-tested 300 → 500 HP.
- Done later: training center (`Training.IsTrainingCenter`), raids / demolition / brownout on other players'
  companies (M8), the tooltip line (`PanelInfo.StatusText`). "Freezing" is not a rule: company +0C4h (thought to
  be a frozen flag) is the running job (setter vt+9Ch sub_4239A0, getter vt+0A0h sub_4239C0; the menu sub_421370 opens
  only while it is 0; event case 13 cancels a job of type 16h) — `Company.Busy`. The dead `Frozen` flag is removed.
  Open: button hover animation (12 frames).

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
- Done: "Set as primary training center" (command 12, slot 4, icon 0xD45, text 30,8; HQ +0CEh via vt+90h,
  `Organisation.PrimaryCenter`), shown on an own training center that isn't primary. MCP-tested: Thug 600 → 300 min.
- Verified (sub_420ED5 at 0x42114B): a row with catalogue income ≤ 0 calls owner Spend(−Income() / 441) on each of the
  441 weekly ticks, so a Тренировочный центр costs ~500 000 a week and a failing Spend repeats "out of funds" every
  tick — corrected: Spend keeps paying while the player has gangsters, so there is no repeated message; with none
  left and negative net income the player is eliminated. Open: the owner type factor the original applies to type 5 (HQ +120h?) is assumed 1.
- Cadillac passengers: done (see §5 Units).

## Bribe shares — "Расходы" chart (2026-10-27)

`Scripts/SharesPanel.cs`: five coin bars (GameButt.bmp 16,43, 20 high) at x 546..680, rows y 447/472/499/526/554
(table 0x476BF8) = savings, army, FBI, politician, police (`Organisation.Shares` 0–4). Dragging a row (sub_411F84 /
sub_413350 / sub_413A04) sets its width (0–134) and sub_409C60 rebalances: piecewise scale with knots (0,0) (49,20)
(88,40) (108,60) (122,80) (134,100) (inverse switches at 21/41/61/81); the other bars are scaled to the rest of 100 %
(a quarter each when all zero), a shortfall goes to the next row (row 0 after the last); shares written via
sub_414CB0. Hover (sub_40C2C2) prints texts 5,63/64/67/70/73 at (232, 555) and the favour 5,65–75 at (232, 573);
that line is also the panel's status line: button texts now show there (`PanelInfo.StatusText`, `HookStatus`) instead
of OS tooltips. The map hover label (done 2026-11-03, `MapHint`, sub_40C2C2 from 0x40C9FB): drawn on the map at the
hovered object (font 3, width 150, centred, colour by relation: own/none green, enemy red, ally yellow, nobody's light
green); with an own unit selected it reads the order text for the armed order (none / Go = the default action) —
5,40–54 for buildings (enemy / ally / nobody's: Extort, an HQ Attack, Attack, Destroy), 5,55–62 for units (Attack,
Terminate); police stations and own objects show the name. The Russian texts have no %s. Unit test `MapHintTexts`. MCP-tested: 22.6 % to the police → 11 745 bribed, 2.4 favour points by day 6.

## Unit orders — action bar (2026-10-27)

- Unit menu sub_42D2C1 (unit vt+10h): own units get buttons gated by unit vt+1C4h(order): slot 0 order 0 "Go To"
  (29,0; with Shift action 5 "Force Go To" 29,6), slot 1 order 1 "Attack" (29,1; Shift: action 6 "Force Attack"
  29,7), slot 2 order 2 "Extort Business" (29,3), slot 3 order 3, slot 4 order 0Ah, slot 5 order 8 "Patrol" (29,8);
  others' units: orders 0Bh / 0Ch. Texts 29,0–9 are loaded into 0x4A1368… by Unit::Unit; 29,4 "Stand Ground",
  29,5 "Frency", 29,9 "Unload" (Cadillac). Icons per unit type: go 0x4842E4, attack 0x484318, extort 0x48434C,
  0x484380 / 0x4843B4 (orders 3 / 0Ah), go+Shift 0x4843E8, attack+Shift 0x48441C, patrol 0x484484, car exit 0x4844B8.
- Done (`ActionBar.BuildUnitMenu`, `Main.PendingOrder`): Go, Attack (units), Extort; the button arms the order and the
  next left click on the map carries it out. MCP-tested: a Thug extorted a business and walked to a clicked cell.
  Shift switches slots 0–1 to Force Go To / Force Attack (icons 0x4843E8 / 0x48441C, texts 29,6/7, "…OrderHurry"
  voice; movement as Go for now). Stand Ground was tried and removed: its icon table 0x484380 holds tile 0x38 ("Button
  upgrade up", blank) for every type, so the original likely hides order 3 via vt+1C4h; Chicago.tile has a separate
  "Button guard" (tile 0x30) — decode the gate before showing it.
- Unit OnAction sub_42C0C0 (vt+BCh): actions 3 (Stand Ground), 0Ah (Disguise), 0Bh, 0Ch are immediate commands
  (object sub_428752 added to the world); the others (0 Go, 1 Attack, 2 Extort, 5/6 Force, 8 Patrol) are targeted:
  they arm the order mode and the next map click builds the order. Next: find the map-click order builder for mode 8
  (Patrol) and the command handler for 3 / 0Ah. Lead: the slot click arms [+8E0h] at 0x41287A..0x4128CA; the map
  click (0x413280) calls, for each selected unit, unit vt+C0h = sub_42C1F6(mode, target), which builds the order:
  0 go (sub_428BC8 / sub_4284B0), 1 attack (sub_4284B0), 2 extort (sub_428BC8, 0x64 bytes), 4/5/6 (sub_4284B0,
  0x60 bytes), **8 patrol = sub_4292E0 (0x68 bytes, vtable off_483E54)**: its event sub_429375 stores the two ends
  (+60h / +64h) on start and passes every event to the shared order stepper sub_428500 — the back-and-forth lives
  there / in the vtable overrides. Done: patrol overrides vt+88h (sub_4293C7, leg finished): if the unit lives, swap
  the target end (+2Ch = +60h / +64h) and re-run after one game hour (event 3EAh); sub_428500 then sets the unit's
  target / stance (vt+1A4h, vt+194h with 1/2/4 by the order's mode +5Ch) and steps the move order sub_4279E7.
  `Units/PatrolOrder.cs`, slot 6 (icons 0x484484, text 29,8). MCP-tested: a Thug shuttles between its cell and the
  clicked cell. Open: the stance (fighting on the way).
- Gate sub_42CCE5 (unit vt+1C4h): Collectors and units inside a car have no orders; own units: orders 1/2/8 need unit
  vt+110h (probably "can fight"), 2 also checks vt+A0h; order 0Ah "Disguise" (29,10, icons 0x4844EC) needs vt+B8h
  and 60 min since the last use (Hit man). Others' units: 0Bh "Ask cops to arrest gangster" (29,11, icon 0xAD8),
  0Ch "Pay snitch to switch sides ($200.000)" (29,12) — both authority services.
- Map clicks now use the event position (`GetGlobalMousePosition` reads the OS cursor), so MCP `game_click` on the
  map selects and orders units. The camera starts on the local player's HQ (PLAN M7).
- Done (M10): order acknowledgements sub_42CEF8 → `UnitVoice` — selection "…Click", go "…Order" (Cadillac CarGo /
  CarClick), attack/extort "…ClickEnemy" (Hit man uses the Bombman voice), forced go "…OrderHurry"; played for the
  first commanded own unit (not collectors).
- Gate sub_42CCE5 decoded 2026-11-03: own units get only orders 0 Go, 1 Attack, 2 Extort, 8 Patrol (1/2/8 need
  unit vt+110h, i.e. a weapon) and 0Ah Disguise; never 3 Stand Ground (so its button stays hidden). Order 2 also needs
  the owner's HQ flag +1D2h (vt+0A0h), which sub_4043D0 clears (vt+9Ch(0)) for a player set up without a base: in
  episodes 4, 13 and 18 the user can't extort (`Organisation.MayExtort`, scenario check). Disguise, Force variants,
  Unload and enemy-unit buttons are done.
- Frenzy: order 4 (29,5 "Frency", no Russian text) is refused by the gate for every unit, so it is not in the game.
- Done: the order-mode cursor and the map hover hints (texts 5,40–62). Open: order 4 "Destroy %s" (attacking
  buildings) is not reachable from the menu either (gate) — only the hint texts exist.

## Victory conditions and defeat (2026-10-27)

- VCOND IsMet virtuals (slot 0 of each class vtable), checked hourly (`VictoryCondition.Evaluate`):
  1 sub_463610 weekly net income > target; 2 sub_463A40 the player owns ≥ count companies of business type
  (fields: count +8, type +0Ch — the old parser had them swapped); 3 sub_463B04 no living unit of the type (any
  owner); 4 sub_463774 a unit of the type exists (any owner); 5 sub_4639B0 market share of the type > target (reads
  the first HQ object); 6 sub_4638D0 the player owns a business (not legal / support) in district n. 7 (unit reaches
  area) and 8 (companies in a rectangle) are not decoded yet.
- MCP-tested: episode 2 "open four thieves' dens" (VCOND 2 1 4 4) won on day 8 after four type-4 businesses.
- Defeat: see the Spend correction (sub_430860 → sub_43B677); MCP-tested with no gangsters and cash −400 000.
- Test tip: start another episode by writing `current=N` into `%APPDATA%\Godotpp_userdata\CaponeSharp\campaign.cfg`
  (delete it afterwards). C# default parameters are not visible to GDScript `game_eval` — pass every argument.

## Unit combat AI (2026-10-27)

`Units/UnitAI.cs` (partial `Unit`), from the unit tick sub_42AA86 (unit vt+5Ch):
- Scan of the vision square (vision grid +0E4h): nearest enemy building (player vt+E0h(owner) = 1), nearest enemy
  unit that is fighting (vt+11Ch sub_42E454: activity +0BCh in 5–8 or hit within the last hour, +0CCh) and nearest
  passive one; hidden units (vt+19Ch) are skipped; an unowned unit (police) counts every owned unit as an enemy.
- With a running move/attack/patrol order (job classes 17h/22h/27h/28h) the order's mode (job vt+90h) is the
  stance: 0 passive, 1 shoot the order's target in range, 2 the nearest enemy, 3 target else nearest, 4 enemy
  buildings when no unit. Without one (idle): keep the target in range; else shoot the nearest enemy in range unless
  stance 3; else chase (vt+164h) — stance 2/4 always, stance 1 within 5 cells (flt_4845EC) — and stance 1 chases an
  enemy that is fighting. Default stance vt+1B0h: 3 Terrorist, 1 others. Shots: vt+104h; in range: vt+118h.
- Done: the idle rules for owned units (every 5 game minutes), the "fighting" mark on both sides of a hit.
  MCP-tested in episode 4: walking 4 gangsters near Большой Джим's snipers started a fight on both sides.
- Per-order stances: done (`Unit.MoveMode`, `UnitAI.ThinkUnderOrder`). sub_42C1F6 builds the move order sub_4284B0 with a
  mode: Go 2 (Terrorist and Thief 0), Attack 3 (Terrorist 1), Force Go 0, Force Attack 1, action 4 → 4, Patrol legs 3
  (sub_4292E0); Go on another player's company is an extort order. While it runs (switch 0x42AF52): 0 never fights;
  1 the ordered target once in range; 2 the nearest enemy in range; 3 the target, else the nearest; 4 enemy buildings
  (chase). Opening fire drops the order (order vt+7Ch = sub_428470) and the unit continues with its idle stance.
  MCP-tested in episode 4: a Go stopped 5 tiles (its range) from a deserter and shot it; a Force Go walked on without
  a target. Computer players use the same orders (home: Go, attacks: Attack).
- Patrol legs fight with mode 3: opening fire ends the leg, the patrol waits its hour and turns back (sub_4293C7);
  waiting at an end, the unit shoots what comes in range but doesn't chase. MCP-tested in episode 4.
- Another player's unit (menu sub_42D2C1): order 0Bh "Ask cops to arrest gangster" (29,11, slot 0, icon 0xAD8) is done
  (`Unit.ReportToPolice`): gate sub_42CCE5 / handler sub_42C503 — police service 2Ch for Thug and Pistol man (500 000,
  30 points), 31h for Shotgun / Terrorist / Thompson (2 000 000, 40), 34h for any (4 000 000, 60; only checked, not
  paid), then a police car with "lawyers can't help" (sub_4460C5 with 1). MCP-tested in episode 4 (30 points paid, car
  sent). Order 0Ch "Pay snitch to switch sides ($200.000)" (29,12, slot 1, icon table 0x4844EC) needs unit +0D8h =
  2 000 000 (vt+1E4h sub_42CED7), set by the FBI snitch service — done, see "HQ authority services".
- Open: mode 4 (action 4 is not on the menu), enemy buildings, police targeting, the
  Terrorist/Cadillac chase rule.
- Movement (done 2026-10-29, `MoveMap` + `UnitMovement`, A* removed): the original walks tile by tile. Each cell,
  sub_427EF5 scores the (up to two) neighbours toward the destination with sub_427CC0 (6 destination, 0 blocked,
  5 free road / pavement, 4 free other ground or a moving occupant, 2 a standing one, 1 crowded), ties go to the
  cell nearer the start–end line, a poor lone choice tries a sidestep; stuck → detour search sub_428244 (rays,
  waypoint), give up when a cell repeats in the last six. Units may share cells; a road / pavement cell with a
  fighting unit is closed (sub_432E13). Letters: R road, S pavement (no Cadillac), I walkable (was blocked in the
  port), the rest not. Buildings are reached edge to edge. Unit tests `MovementTests`.
- Start points (sub_43BDB7): RANDOM 0 → the first free start point in list order, RANDOM 1 → a random free one; .map
  STARTPOINTS and .epi UNIT use the same (x y) order (episode 13: Torrio's guard at 84,37 next to start 81,38).
  Episode 4's boss stands far from his base on purpose.

Known gaps in the brick (do later):
- Unit::Attack `sub_42BD15` (done 2026-10-29): units think once a game hour (event 5, sub_42A535); a shot needs
  the aim ticks (table +00h: Sniper 4, others 1) and the reload time (+2Eh: Shotgun 300 min) → `CanAttack.AttackRate`
  from the EXE (the hand-written `attackRate` in units.json is gone); a Sniper re-aims on a new target; only the
  Thompson man (+30h) shoots from a car; shot sounds by type (table 0x484554). Still simplified: the shot object
  `sub_438873` (hit roll, projectile) and `CanAttack.HitOnce` for extortion hits (unit vt+104h).
- The shot sub_438873 (done 2026-10-29, `CanAttack.Fire`): round(rate) bullets (Thompson 6), area table +26h − 1
  (Shotgun, Terrorist 3×3, friends included), stray +28h (Thompson 50 %), hit roll +0Ch (×0.2 from a car), damage
  min + random(max − min); hits reset the victim's aim; the Terrorist dies with his blast; extortion hits are
  ordinary attack steps (vt+104h = sub_42BD15). Removed: the old Sniper/Raid/Bomb/Thompson abilities and the
  evasion roll (not in the EXE). On the target's own cell the bullet meets the target (units may share cells here).
  Balance changed: in the idle scenario run episode 15 is no longer lost on day 4 (the AI attack stalls on the
  player's units). To compare with the running original.
- `ExtortOrder.CanExtort`: which unit types may extort is a guess (all fighters except Collector/Cadillac/police).
- Company portraits in the panel follow the EXE (district and grade, sub_422158; `SelectionIcon.CompanyPortrait`).
- Police car stats: unit type 6 table row is used (TicksPerTile 20); the original car object is `sub_4459D0` (0x44 bytes).
- Lawyers are police services 29h / 27h / 24h (200 000 / 75 000 / 10 000 bribed, 1 point): the police job uses the
  best one the arrested gangster's owner can use (70 / 50 / 30 % release, 28,1) — already working. 2Ch / 31h / 34h are
  the "ask cops to arrest gangster" services (order 0Bh), not informers.
- Orders on another player's company (`Scripts/Economy/CompanyServices.cs`, menu sub_421370, actions sub_421CF5,
  commands sub_421E40): raid (slot 5, 30,13/14, police 26h/2Bh/2Fh in the slums, 32h/35h in the middle class by price
  tier; raid job sub_445CA4/sub_445D72 sends a car, "Cops raid your business !" 28,0, after 15 minutes the business
  becomes an unowned legal company, sub_422D82); demolish (slot 4, 30,15/16, slum businesses, politician 14h/18h/1Dh;
  30,20/30,21; HP 0 and removal, sub_422DE0); brownout (slot 3, 30,17/18, any company of a powered block, politician
  1Ch 2 weeks / 19h 1 week / 15h 3.5 days via block command 1 sub_420150; block vt+18Ch = powered). MCP-tested in
  episode 15: raid closed an Интим-салон, demolition removed a Собачьи бега, brownout cut block 10 for 2 weeks.
  No rubble (checked 2026-11-03): sub_422DE0 calls vt+30h(40h) (sub_4386F5 → sub_414F80: a world notification, code
  40h, unused) and sub_43BCE0, which puts a plain building-cell object (fnSetGroundWalkabilityToNone, vtable
  off_47CDB0 — the same one the map loader uses for ordinary buildings) on each footprint cell and removes the
  company. So the building tile stays and its cells stay blocked, as here.
- UNIT flag A = 0 → salary 0 is assumed (`CampaignManager.SpawnUnit`).
- Smoke test leaks nodes (RID warnings at exit).

## Computer players (2026-10-27)

`Scripts/Economy/ComputerPlayer.cs`: the AI HQ object (sub_461510, vtable off_4911BC, 0x244 bytes) for every PLAYER
with kind ≠ 0. The kind goes to vt+160h (sub_4626B5) from sub_4043D0.
- Timing (sub_462480 / handler sub_462590): events 44Dh / 44Eh / 44Fh first at +1 / +2 / +3 hours, then every
  3 hours: 44Dh unit tasking (vt+130h sub_461CA8 → vt+140h sub_4618D0 per unit), 44Eh recruit and convert
  (vt+134h sub_461F24), 44Fh strategy (vt+138h sub_4620D7). Event 450h at +3 hours sets the bribe shares from
  parameters 5–9 (sub_4623E4, share job sub_4287F0, ×/÷ flt_48381C = 1000).
- Parameters (+210h, setter vt+15Ch): [0] convert legal companies, [1] minimum Thugs, [2] recruit when the human is
  stronger, [3] salary budget in % of company income (+18h = gross income, flt_4911A8 = 100), [4] net income
  below which fighters extort (400 000), [5..9] shares (savings, army, FBI, politician, police), [10] attack the
  human HQ, [11] cash reserve, [12] type left alone (12 Godfather). Defaults: 1 5 1 25 400000 60 30 0 0 10 0 0 12.
  Kind 1: shares 100/0, [0]=0 [1]=2 [2]=0 [3]=10; kind 2: shares 70/30, [0]=0 [1]=2 [2]=0 [3]=20; kind 3: shares
  70/30, [0]=1 [1]=5 [2]=1 [3]=25; kind 4: defaults; kind 5: [11]=−20 000; kind 6: as 3 plus [10]=1, [11]=−20 000.
  +1F8h = random business type 1–3 (bootlegging / entertainment / gambling) used for conversions.
- Strategy: strength = Σ word_491188[type] (1 4 16 6 5 14 0 0 8 10 0 0 0) over own units (vt+14Ch sub_461F88);
  human strength + 5 > mine → "weaker"; mine / 2 > human → target the human. Intruder (vt+154h sub_462028): another
  player's unit (not a Collector) within 30 cells (flt_4911A4) of the AI's training center. Recruit the best
  licensed type (vt+158h sub_461BD7: Thompson if service 6, Hitman 3, Shotgun 2, Pistol 0, else Thug) when
  (weaker and [2]) or [10], and cash > reserve (or cash > −60 000 with an intruder); else a Thug when Thugs < [1];
  else the chosen type when salaries < [3] % of company income; else when net income > 2 000 000 and the lead over
  the human's net income / 2 > salaries. Convert when cash > reserve and [0]. +1ECh (attack the human HQ) turns on
  at strength − Thugs ≥ 12 and off below 7.
- Unit tasking (not Collectors, not type [12], not fighting): Thugs extort; others attack the intruder, else the
  human's training center (+1ECh), else the target's, else extort while net income < [4] (not Cadillacs), else walk
  home when ≥ 10 cells (flt_491180) from the training center. Extort search (sub_461781 / sub_461690): square rings
  around the unit up to 500, a company of nobody or a non-ally outside district 2, 50 % per try (LCG sub_461660).
- Conversion (vt+13Ch sub_461E10): the own legal company (vt+190h = row type 0) nearest the training center that is
  not busy and has a row (vt+144h sub_461D40: the AI's type, the company's grade, its district or 3, licensed, price
  < cash + 100 000; the most expensive) starts converting (job sub_427000). One per cycle.
- MCP-tested: episode 4 (kind 2) extorts, keeps 2 Thugs, trains Pistol men once licensed, no conversions; episode 13
  (kind 6) converts legal companies to dog tracks and recruits Shotgun men; episode 15 (kind 6) destroyed the
  player's base and army in a week.
- Deviations: one extort try per company instead of per ring cell; a unit already extorting keeps its order; allies'
  and police units aren't intruders. Unknown: unit vt+1F0h (units skipped by the tasking) and unit vt+A0h.
- Found while testing: attacking units stack on one cell. (HQ HP and elimination: fixed, see below.)

## HQ authority services (2026-10-28)

`Scripts/Economy/HqServices.cs`. HQ building menu sub_4152F0, commands sub_415FF0 (issuer pays, the clicked HQ's owner
is the target), HQ object methods vt+FCh…11Ch (sub_4325C7…sub_432A61).
- Own HQ: slot 0 "Ask Mayor to start bribing investigations" (17,3/4, service 17h politician 700 000 / 20 points):
  "Mayor declares that FBI will start bribing investigations ..." (17,48), every player's bribed money except the
  politician's × 0.75 (dbl_488850). Slots 1–3: legalize business type 1–3 one step (17,5…22 by type and level:
  beer / wine / spirits, magazines / peep shows / bordellos, lottery / bookmaking / gambling; services 1Bh, 1Eh, 21h
  = politician 2 000 000/200, 4 500 000/400, 6 000 000/600), "… has been legalized." (17,39…47). Slot 4 "Set as
  primary training center" (17,24, icon 0xD45, command 0Ch): shown while another center is primary (and the HQ has no
  job but training), makes the HQ primary again. Slot 5 Repair (17,23, icon 0xAD6, command 1 → repair job sub_4289A0
  via HQ vt+BCh sub_415D24): free for the HQ (cost per HP vt+160h sub_402C20 = 0); MCP-tested 1000 → 1030 HP in 6
  game hours, no money spent.
- Another player's HQ: slot 0 FBI "talk" to the boss (17,25/26, 11h FBI 2 000 000/100): 30 game hours during which
  the victim, if local, can't give input (main window +0A40h) and sees "FBI \"visits\" you ..." (20,2) with progress;
  slot 1 Mayor investigations (17,27/28, 16h 500 000/20): two weeks, income × 0.85, 20,27; slot 2 evil rumour
  (17,29/30, 1Ah 1 500 000/60): two weeks, income × 0.7, 20,26; slot 3 snitch rumour (17,31/32, 20h 5 000 000/1000):
  20,28, each gangster quits with 30 %; slot 4 announce player death (17,33/34, 22h 8 000 000/1600): 20,29, each
  gangster quits with 50 %, each company lost with 25 %; slot 5 ask FBI for snitch (17,35/36, 0Eh 500 000/100): the
  first non-snitch gangster of the target becomes a snitch (+0D8h = 2 000 000), "Snitch at ..." (17,49). Slots 0–2
  hide while the effect runs. Buttons show the "1 point" text when the service costs one point.
- A snitch of another player shows order 0Ch "Pay snitch to switch sides ($200.000)" (slot 1): he joins the buyer
  (`Unit.ChangeOwner`), "%s switches sides." (29,19).
- MCP-tested in episode 15: beer legalized; investigation + rumour on Дион О'Брайен (his type factors 0.6); a snitch
  planted and bought (switched sides, 200 000 paid). Not tested: the FBI visit on the local player (the AI doesn't
  buy services). Open: the original's FBI-visit progress box.
- [ ] FUTURE — do not start until the user explicitly asks: computer players using HQ services (FBI visit, rumours,
  investigations, snitches, legalization). The AI code read so far (sub_461510 family) never calls them.

## Elimination and last player standing (2026-10-27)

- HQ building (class 9, vtable off_477644): vt+F8h `sub_416750` = 514h → 1300 HP (`Company.HqMaxHp`).
- HQ destroyed (vt+FCh `sub_415F20`): "Your headquarter has been destroyed." (17,37) to the owner, else
  "%s's headquater has been destroyed!" (17,38); the owner is eliminated (`sub_43B677`) and the building removed
  (`sub_43BCE0`). The owner is out even with units left.
- An HQ can only be destroyed, never captured (user, from the original). The EXE still has an owner-change handler
  (vt+74h `sub_4151C5`: "You have just lost your headquarters." 17,0 / "You've just conquered %s's headquarters."
  17,1, then the old owner is eliminated), but play doesn't reach it; not ported.
- Elimination (`sub_43B677`): 19,1 defeat for the local player, else "%s has been eliminated." (19,2); then
  `sub_43B711` removes the HQ object (`sub_43AB50`, so its events stop) and, while the game runs (+186h): fewer
  than 2 players left → the local player, if still in, wins with "You have won ! The city belongs to your gang !"
  (19,3); with World +4EAh (alliance victory, copied by `sub_404B50` from the game setup +7870h) and no two enemy
  players left → "… belongs to your alliance !" (19,4). The flag's campaign value is unknown; the port keeps it off
  (`Organisation.AllianceVictory`).
- MCP-tested: episode 4, destroying Большой Джим's HQ (Debug `DamageHq`) → 17,38, 19,2, win screen with 19,3;
  episode 15, the player's HQ destroyed → 17,37 and the defeat screen.
- An eliminated player's assets (user, from the original game): every unit disappears (`Unit.Vanish`) and every
  company and business goes back to an unowned legal company, like a sale without the money
  (`Company.ReleaseToNeutral`; running conversions and repairs are dropped). Prepared for a future setting:
  `Organisation.RogueUnits` keeps the units (not Collectors) as neutrals hostile to everyone, other rogues included
  (`Unit.GoRogue`, `UnitAI.IsHostileTo`, `CanAttack.IsHostile`). MCP-tested in episode 8: Морган's 33 units vanished,
  his 24 companies became unowned legal ones; with rogue units on, Рико's fighters fought each other.
- Done: the HQ of a player eliminated by bankruptcy is removed (see "HQ of a bankrupt player"); the other removal
  paths `sub_429658` / `sub_43C052` are multiplayer only.

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
- **Organisation** (HQ class 6): Spend sub_430860 pays when cash − amount + 200 000 ≥ 0, or net ≥ 0, or the player
  has a non-collector unit (vt+D8h sub_4308F0); otherwise "out of funds" (20,0 + sound 24) and the player is
  **eliminated** (sub_43B677: local → "Game over. You have been defeated!" 19,1 as a loss → GAMEOVER screen; others
  "%s has been eliminated." 19,2; sub_43B711 then checks for a last-player win, 19,3); Deposit sub_430790 (bribe shares of the profit part); UpdateStats sub_430C30 hourly; interest
  −cash/10 every 1008 min when negative; market-share curve; salaries = unit table +0x20 per week, paid daily / 7
  (sub_42A535). Payday asks vt+70h (cash − pay + 200 000 ≥ 0), not Spend: past the overdraft a gangster quits
  (vt+74h(0), resign sound for the local player) and a Collector just goes unpaid. A gangster who quits is ownerless
  and, like every ownerless unit in the unit AI, fights every player's units, his former owner's included
  (`Unit.IsDeserter`; deserters leave each other and the police alone). MCP-tested: two deserters attacked the
  player's Shotgun man and were killed. Bankruptcy itself doesn't end the game while you have gangsters; it does
  (Spend, e.g. the debt interest 0x430A8A) once they've all quit or died, the debt is past the overdraft and net
  income is negative (the user remembers bankruptcy never defeating the player; kept as in the EXE).
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
- [x] Unit table fields (checked 2026-11-02 by every direct read of 0x484030..0x484060): +00h fire rate, +26h attack kind, +28h stray chance, +2Eh reload minutes, +30h fires from a car, +24h training minutes (sub_427505) are all used. +10h/+12h are 0 in every row (padding). +1Ch is never read directly; it equals +20h except the police car (400 000 / 375 000) and the Collector (15 000 / 5 000) — probably a list price for the menu; +20h is the unit value / weekly salary (Unit::Unit sub_42A150 copies it to +0A0h).
- [x] Game clock (R1): game time is in **minutes**; 60-minute turns, at most one per 1000 ms → 1 game hour = 1 real second at speed 1 (`GameClock`, scheduler like World::Schedule `sub_43AF10` / `sub_43ABD0`). Unit speed = tile length × 60 / TicksPerTile per second. `GameClock.Speed` (cmd `--speed=N`, keys `-`/`=`) speeds everything up for tests.
- [x] Attack rate comes from the EXE table (`CanAttack`: fire rate +00h, reload +2Eh); army favour from the services table 0x488268 (`Training.ServicePoints`). `ArmyFavor` in units.json is no longer used.
- [ ] Thief walk frames and the Terrorist attack frames are not named in Chicago.tile (idle/walk used as fallback). The EXE's per-type animation tile table is not located yet.
- [x] UNIT flags (verified 2026-11-03): A = 0 → vt+174h(0) (sub_42FAA0) clears the unit value +0A0h, i.e. no salary (done); B → vt+1ECh (sub_42FBE0) stores unit +0F8h, read only by the AI unit tasking (sub_4618D0 via vt+1F0h): a computer player never tasks such a unit (`ComputerPlayer.TaskUnit`). A starting Cadillac's passengers get B = 1.
- [x] VCOND 7 (sub_463BC3): a living unit of the player of the type (13 = any) within the radius of the cell;
  VCOND 8 (sub_463CD8): at least `count` companies of the business type (6 = any but legal) in the rectangle of
  original coordinates. The first condition met ends the episode (sub_43C1B2) — not all of a player's (fixed
  2026-10-29). `MapTileMath.ToOriginal`.
- [x] BRIBEADVANCE authority numbering verified 2026-11-02: the reader (sub_4670A5 at 0x467D67, " %d %d %f %ld ") calls player vt+0C4h (sub_432D40: favour +0B8h[a] = points) and vt+0C8h (sub_432D60: money +0A4h[a]), the same arrays the services check (sub_4310A0) with authority 1 army / 2 FBI / 3 politician / 4 police. `Organisation.Favour/AuthorityMoney` use the same indices.
- [x] Company stats come from the EXE catalogue (`OriginalBusinessTable`); `Company` is rebuilt on it (PLAN M3). `companies2.json` is no longer read (delete it once the logos are remade); `companies.json` still maps sign types to Chicago.tile sprites.
- [x] Episode texts are shown: briefing (`BriefingScreen`), TIPTEXT at the start and SUCCESSTEXT / FAILURETEXT at the end in the info window.

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

- [~] Main menu `MAINMENU.bmp` + buttons `MAINMEBU.bmp` — done 2026-10-29 (`MainMenuScreen`, sub_408F86 / sub_4090D0, button tops from 0x47698C, x 257, 283×42; MainMeBu rows 0–4 lit, 5–9 pressed); the game opens on it, Campaign → briefing, the briefing's Back → menu (PLAN M12: buttons Campaign, Network game, Settings, Developers, Exit; build only Campaign and Exit — Network game, Settings and Developers are FUTURE, explicit user command only); new game `NEWGAME.bmp`/`NEWGAMEB.bmp`; preferences `PREFEREN.bmp`/`PREFERBU.bmp`.
- [x] Mission briefing `ScenDisp.bmp`/`ScenDiBu.bmp` showing SCENARIONAME, STORYTEXT, OBJECTIVETEXT, TIPTEXT from the .epi.
- [x] End screens (done; `GameRank.bmp` and `AllyWin.bmp` belong to other windows): `GameWin.bmp`/`GameWinA`/`GameWinB` (SUCCESSTEXT), `GAMEOVER.bmp`/`GameOvAn`/`GameOvBu` (FAILURETEXT), `GameRank.bmp`, `AllyWin.bmp`.
- [ ] In-game windows for the menu stubs (tips → InfoWin, statistics → GameRank and money → MoneyWin done 2026-11-02; `MoneyWin.bmp` is the transfer to other players, not the bribe sliders): `HelpWin.bmp` (unknown use), `MessSend.bmp` (messages, multiplayer chat). `AllyWin.bmp` (alliances) done 2026-11-02.
- [ ] Bottom panel, as specified in `projectinfo/gui.md`:
  - finances block
  - Recruit panel (`Recruit.bmp`)
  - progress bar (`Blood.bmp`)
  - **action buttons from Chicago.tile**: go/kill/guard/stay/buy/upgrade/extort rows 102–123 and per-unit go/attack buttons 2709–2830. This answers "which buttons, which icons".
- [ ] Portraits `Faces.bmp`, business signs `Signs.bmp`/`Signs2.bmp`, Cadillac `Car.bmp`, `Park.bmp`, `hq.bmp`, `open.bmp`, Godfather `godf.bmp`/`godfkl.bmp`, credits `CREDIT.bmp`/`CREDITBU.bmp`.
- [x] **Mouse cursors** `ArrowCur`, `GotoCur`, `AttackCu`, `ProtectCu` live in the EXE's PE resources (loaded in `fnLegalCrimeMainWindowMain`, 0x419F10). Extract them with a small PE resource reader and use them via `Input.SetCustomMouseCursor`.
  Done 2026-10-31 (`OriginalCursors`, `OriginalExe.ReadResource`): the four cursor groups are read from the EXE's PE
  resources at run time (32 × 32 monochrome; hotspots 1,1 / Attack 10,10). sub_41E970(n): 0 arrow (always, the
  window's cursor), 1 go, 2 attack, 3 protect while an order is armed (keys / buttons: Go, Force Go, Patrol → go;
  Attack, Force Attack → attack; Extort → protect) until the map click. Unit test `CursorsComeFromTheExeResources`.

## 4. Sound (P1 — cheap, big effect)

- [x] WAVs load at runtime (`Sound`, at most 16 at once); the EXE's id → file table is off_478700 (205 entries).
- [x] Unit voices per type (`UnitVoice`); death sounds for the local player's units (sub_42B880, `Unit.DieSound`): `<Unit>Arrive / Click / ClickEnemy / Die / Idle1-5 / Order / OrderHurry / Resign` for Thug, Pistol, Shotgun, Thompson, Sniper, Bombman, Hitman, Thief, Godfather.
- [x] Weapons: shot sounds by type (table 0x484554, `CanAttack.ShotSound`); `Crash` for destroyed HQs, demolished companies and Cadillacs.
- [x] Business (selection sounds sub_422C80 / sub_416660, `Company.SelectSound`; collector cry below half HP): `Bootlegging/Entertainment/Gambling/TroubleShooting/CollectionCenter/TraningCenter/HQ/Business`. Capture outcome: `BusinessPay1-6` / `BusinessRefuse1-5` — done in `Company.Extort` (`Sound` class). Collector hits: `CollectorHit1-3`.
- [ ] Police/FBI: `PoliceArrest1-3`, `PoliceRaid`, `POLICE_S`, `FBIInforms`. Cars: `NewCar`, `CarGo`, `CarClick`. UI: `ButtonClick`, `OverButton`, `Click`, `select`, `Message1-8`, `OutOfFunds`, `Permit`, `Welcome`, `NewGame`, `WinEffect`, `LoseEffect`.

- [x] Message log over the map (sub_40C2C2 / sub_40FFD0 / sub_41D3E0, `MessageLog`, done 2026-10-29): messages of the
  last 15 s at (25, 13 + 16n), panel font 3; kind 4 green with Message.wav, others yellow; episode results (kinds
  2/3) go to the end screen. Before this, game messages were only logged. A message may carry a map position (FBI
  reports, raids, demolitions); clicking it centres the view there (assumption).
- [x] FBI informers (sub_431542, `FbiInformers`, done 2026-10-29): FBI services 7–0Fh with no point cost; once
  bribed past the price they report every week (7: every 28 days): rival funds, income, gangsters, market share,
  a business / gangster / HQ location, with FBIInforms.wav.
- [x] The "Меню" button (sub_40C103): GameButt.bmp (158,43)–(208,56) / pressed (208,43)–(258,56) at (5, 0), click
  area (5,0)–(55,13); the message lines start under it.

## 5. Gameplay rules recovered from the EXE (P2)

These replace the open questions Q2–Q7 in `godottry/projectinfo/aithoughts.md`.

- [x] **Fog of war** — done 2026-10-28 (`Scripts/Vision.cs`, `Scripts/FogOfWar.cs`): sight grids with the EXE's line of sight (opaque = block letters B C M H D G P), units' current vision, buildings 4 tiles; the local player and allies share sight; unseen cells use a darkened copy of their tile (alternative tile 1), other players' units there are hidden and can't be clicked, companies keep their last seen sign (dimmed); shots and unit AI use the grid (`Unit.Sees`). `FogOfWar.Enabled` is the switch (Debug `Fog(bool)`). MCP-checked in episode 4. The panel shows a fogged company's last seen owner and type (`Company.ShownOwner` / `ShownEntry`; MCP: 19 companies Big Jim took in the fog still showed as unowned). Open: computer players see everything. The unrolled radius 3/4/8/9 routines (sub_456D98 / 457D79 / 44C237 / 453BF1) differ from the generic one: they end by clearing the outer ring and cells farther than r (done 2026-10-29, `Vision.Build`). Original notes: Every unit has a sight radius (unit table +2Ch Vision / +2Ah VisionMax, `OriginalUnitTable`) and the player sees the map only through their own units' sight (and probably their buildings'). Enemy units and other players' actions outside it must be hidden. Start from the unit's vision grid (+0E4h, (2r+1)² bytes, r = `sub_42E420`, filled around `0x42A35E`) and find how the map renderer uses it. Rules confirmed by the user (from playing the original):
  - Own buildings reveal the map around them too.
  - The fog is dynamic: when a unit leaves, the fog comes back over that area (units in it are hidden again). Companies and businesses outside the vision of the player and their allies stay drawn in their last seen state (owner, business type, damage) until someone sees them again.
  - Sniper: done. Current vision (unit vt+1ACh `sub_42E385`) is unit table +2Ch while walking (activity +0BCh 1–4) and +2Ah
    otherwise; the Sniper has 8 / 9 (Thief, Godfather 7 / 8, the rest equal). A shot (vt+118h `sub_42B750`) needs the target
    inside the current vision square and within the table range (Sniper 9.0), so standing gives +1 sight and +1 reach
    (`Unit.CurrentVision`, `Unit.Sees`, `CanAttack.TargetInRange`). Open: line of sight in the vision grid; activities 12+
    shrink vision to +2Ah × (27 − activity) / 15; `SniperAbility`'s farther-is-stronger damage is a GDScript-era guess.
  - Disguise and the Hit man: done (`Units/UnitDisguise.cs`, replaces the GDScript-era `HitmanStealth`). Unit +0D0h =
    last shot (Unit::Attack sub_42BD15), +0D4h = disguise start (constructor: 0 / −3601). Order 0Ah "Disguise"
    (29,10, slot 4, icon tile 0xD85 from 0x4844EC, immediate): any own unit but a Collector or one in a car, when the
    owner can use service 12h (FBI, 1 500 000 bribed, 100 points) and 60 game hours passed since the last one
    (gate sub_42CCE5, handler sub_42C503). Exposed (vt+19Ch sub_42B3C0): Hit man — shot within the last 30 hours;
    others — 60 hours after disguising or shot within 30 hours. Only exposed units are picked by the unit AI; the
    player can still order an attack. Panel: " (Disguised)" (5,12) after the type name while vt+1D8h (sub_42EC08).
    Damage to units (sub_42C890 / sub_42C960): an exposed Hit man does 30, a hidden one the table 161–162.
    MCP-tested in episode 13 (Hit man exposed for the first 30 h, then hidden; Disguise button on a Pistol and a
    Shotgun man, FBI points paid, label shown). The Shotgun man's black portrait: fixed ("Shotgun man face", now checked by the smoke test).
  - A disguised Hit man (vt+19Ch) inside someone's sight is visible but never auto-attacked, by unit AI or computer players; the human can still attack him manually. Once he loses the disguise he is a normal target. Check `UnitAI.Think` (it currently skips hidden units entirely, which matches the auto-attack part) and that hidden Hit men are still drawn.

  - RE findings (2026-10-28, not implemented yet):
    - Sight grid: (2r+1)² bytes per object, rebuilt by sub_447870(world, grid, cell, r) when the cell or radius
      changes (units in the tick sub_42AA86 at 0x42AB79; own buildings in sub_4393C6; businesses with catalogue
      +28h = 7 in sub_423500). Special routines for r = 3, 4, 8, 9, generic sub_447660: all cells visible, then ring
      by ring (2..r) a cell is hidden unless a neighbour one step back toward the centre (sub_4471D0 picks one or two,
      by which axis dominates) is visible, see-through and within r by straight-line distance (sub_4470D0). The
      target cell itself is not tested, so walls facing you are seen and what is behind them is not.
    - See-through = map object vt+168h: 1 for the ground classes with vtables 0x475C48, 0x47CAA8, 0x47CDB0, 0x47CF28,
      0x47D0BC, 0x47D230, 0x47D514, 0x48D30C (sub_402C90); 0 for companies (sub_423A10), the HQ building
      (sub_416780) and classes 0x475790, 0x475A90, 0x47CC3C, 0x47D3A4. Map which class is which block letter.
    - Buildings see 4 tiles (company vt+0E8h sub_4234C0, HQ building sub_415150). sub_4393C6 runs for the local
      player's buildings and marks every visible tile (tile vt+0D8h / vt+0E0h) and the objects on it.
    - Godot map: DiamondRight isometric, original (x, y) = Godot cell (x, −y), so the grid works on Godot cells.
    - Plan: Scripts/Vision.cs (grid per radius with the rule above, cached per unit until its cell or radius changes),
      a per-frame visibility set for the local player and allies, `Unit.Sees` using the grid (sub_42B750 checks it),
      a dark overlay on unseen cells, enemy units hidden there, companies keeping their last seen sign.
- [x] (done: bribe shares chart `SharesPanel`, `Organisation.Deposit`) **Authorities = spending sliders.** "%1.0f%% of income is used to bribe police / army / politician / FBI". Favour accumulates as points ("Police own %1.0f points worth of favour"). The fourth authority is the **politician (Mayor)**, not the "senate". Find the income-to-points formula from the code that formats those strings.
- [x] (done: police stations, cars, arrests, raids — `Police`, `CompanyServices`) **Police actions**: "Ask Cops to raid the business (N police favour points)", "Ask cops to arrest gangster". Police are also on the map: unit "Sergeant", building "Police Station". Episode 1 tips say the police arrest your gangsters. Needs police units + arrest logic.
- [x] (done: recruit column with army favour, `Training`) **Army**: "Recruit gun man / shotgun man / thompson man / sniper / hitman / terrorist / thief / cadillac ($X and N Army favour points)". This matches `ArmyFavor` in `units.json`; enforce it on recruit. "Bribe the army to find and recruit a sniper."
- [x] (done: `HqServices`, `CompanyServices`) **Mayor actions** (politician points):
  - legalize beer / wines / spirits / gambling / bookmaking / lottery / bordellos / peep shows / dirty magazines (each triggers a "… has been legalized." event)
  - arrange brownout
  - demolish building
  - start a snitch / evil rumour
  - start (bribing) investigations
  - announce player death
- [x] (done: FBI visit, snitch, disguise — `HqServices`, `UnitDisguise`) **FBI actions**: "Ask FBI for snitch", "Ask FBI to 'talk' to boss"; events "FBI 'visits' you …", "Former FBI agent reveals that %s used to be a snitch …".
- [x] (done: conversion, licences, repair, sell, primary center, market share — `Businesses`, `Company`) **Business**:
  - company classes Legal/Slum, Legal/Middle class, Legal/Center
  - Extort %
  - "Upgrade to Bootlegging/Entertainment/Gambling/Troubleshooting/Supporting biz"
  - upgrade cost in $ + Police favour
  - Repair ($), Sell for $
  - "Set as primary training center"
  - Market share
  - variant names and prices: find the table used with "Upgrade to %s ($%s …)" (pointer table at 0x49E9C4); the names are probably in the Russian string set
- [x] **Units** (done: Disguise, Shotgun raid, Thief, Cadillac, Godfather):
  - Godfather done 2026-10-29: no code checks type 12 except voices and pictures; he is an unarmed unit (stats 0
    damage) and the order gate sub_42CAF0 gives units without a weapon (Thief, Godfather, Cadillac) no Attack, Extort
    or Patrol (`Unit.HasWeapon`); they also never start fights by themselves, and a right click on an enemy just walks
    there. VCOND 3 (kill the boss) and the computer player's param [12] (leave him alone) were already in place.
  - Cadillac done 2026-10-29 (`UnitVehicle`, `BoardOrder`; sub_42EE30 / 42EE90 / 42F3C2 / 42F270): four seats; own
    units sent onto the car board it, ride hidden and keep their AI (they shoot from the car, Unit::Think puts them at
    the car's position); Unload button (29,9); a destroyed car hurts its passengers (20–99, 1000 by a Terrorist) and
    lets the survivors out; the car only drives; episode Cadillacs start with four Thompson men inside; the computer
    player doesn't task passengers. Not checked in a fight yet: passengers' shots from the car.
  - Thief done 2026-10-29 (`StealOrder`, sub_42FC60 / sub_42F6E0): sent onto another player's company he takes its
    uncollected money, onto another player's HQ all of that player's cash; sent onto his own HQ or cash-out business
    he delivers. A killed unit's carried money goes to the killer (`Unit.LoseCarriedMoney`, sub_42B8A3). Scenario
    checks: steal, deliver, killed collector. Elimination now releases assets before the defeat screen.
  - Hitman "Disguise" / "(Disguised)" is a player-toggled state
  - Thief: "%s stole $%s."
  - Cadillac recruitable
  - **Godfather** boss unit (UNIT type 12, its own sounds and pictures; VCOND 3 `p 12` = kill the boss)
  - Shotgun man = Raid
- [x] **Calendar** (checked 2026-11-02: there is none). "Bang: Full year!" is a debug log (fnWriteErr) in the event scheduler sub_416C16 when its 372-slot (0x174) wheel wraps; "Salary $%ld" is the unit panel line. Salaries are paid by sub_42A535 (done).

## 6. AI players (P3)

- [x] AI kinds from PLAYER: done, see "Computer players" above. Open: AI elimination cleanup, alliances between AIs, AI use of HQ services (lawyers, informers).
- (old note) AI kinds 2/3/4/6 from PLAYER. Find the AI update loop, starting from the code that reads `PLAYER`'s kind field (`sub_404390` calls in `sub_4670A5`). Until then: a simple scripted AI (capture nearby neutrals, recruit when rich, attack HQ).
  Known so far: `sub_404390` = Player::Player (0x80 bytes, vtable off_47613C, class id 7), called with `isComputer = (kind != 0)`, the name and the PLAYER numbers. The other class-7 vtables (off_475E3C … off_4763C8, 27 slots each) are probably the player subclasses — start there to find how kinds 2/3/4/6 differ.

## 7. Tooling

- [x] EXE readers: `OriginalTexts`, `OriginalBusinessTable`, sound table (`Sound.FileOf`, 0x478700), cursors (`OriginalCursors`).
- [x] Headless smoke test: `-- --smoke-test` (EXE tables, every map and episode, sprite names).
- [x] Headless scenario checks `-- --scenario-test` (`Scripts/ScenarioTest.cs`, PLAN M13). Run:
  `Godot --headless --fixed-fps 60 --path . -- --scenario-test[=1,4,8,13,15] [--days=14] [--speed=16]` (about 20 s).
  Every game hour: cash ledger, bribe money / favour ≥ 0, eliminated players own nothing, HQ owner and 1300 HP,
  company HP and uncollected money, released businesses are legal, unit HP and position, deserters hostile both ways,
  a rich AI recruits up to its minimum Thugs, no scheduled event threw. Scripted: a gangster resigns (hour 6); raid,
  demolish and brownout on enemy companies (hour 24, favour given); every opponent's HQ destroyed on the last day → the
  local player must win. The end screen is skipped and campaign.cfg is not written. Idle play loses 13 (Torrio kills
  your man, the escape episode) on day 1 and 15 (HQ attacked) on day 4 — expected.
  Found and fixed with it: the game clock stayed paused after the end screen reloaded the scene (`GameClock.Reset`);
  a hit with no (or a freed) attacker left an object at 0 HP alive (`CanBeAttacked` now always raises HPisZERO);
  a player eliminated by bankruptcy kept the HQ building (now removed, `Company.RemoveHq`).
- [ ] Scenario checks still to add: VCOND progress, the computer player converting and attacking. Done 2026-11-03: a scripted fight (Thompson man vs an enemy Pistol man at
  the company farthest from the player's units; shots must land and someone must die within a day); collectors bring money
  to the HQ (`CollectorJob.Delivered`); the police arrest and jail a gangster called in by a station. That check found a
  bug: a road-only police car sent back to its station (and the raid car on its way home) circled the pavement ring
  for ever, so arrested gangsters were never jailed. Cars now drive to the road cell nearest the building
  (`Unit.DriveTo`, `MoveMap.NearestRoad`).
- [x] Unit tests `-- --unit-test[=filter]` (`Tests/`, a small runner inside Godot; every static `[UnitTest]` method):
  22 tests — line of sight (open field, walls, map edge, Sniper 8/9), overdraft, Spend, Deposit split and ledger,
  service points, market-share and legalization factors, extortion chance by income, price tiers, licences, raid /
  demolish / brownout service and authority, clock event order and failing events, Money and CFormat.
  Pure helpers pulled out for them: `Company.IncomeChance`, `Organisation.MarketFactor`,
  `CompanyServices.RaidServiceFor/DemolishServiceFor(district, tier)`, `Brownouts`; test hooks `Vision.InitForTest`,
  `Organisation.SetIncomeForTest`.
- [x] Unit tests for the rules that needed their pure part pulled out:
  done 2026-11-03: computer-player strategy choices (`ComputerPlayer.WeightOf / Compare / AttackHq`), payday and desertion (`Unit.Pay`, `PaydayFollowsTheExe`), unit hostility (`Unit.Hostile`), disguise timing (`Unit.Exposed`), order modes (`Unit.GoMode / AttackMode`), `VictoryTests` (VCOND 1
  strictly greater, values of types 2/6/7/8, every episode's objectives well formed; ep 7 has none — last player
  standing).
- [x] Commented listing: `godottry/disassembled/Capone.EXE.commented.lst`, generated by `godottry/disassembled/annotate/annotate_lst.py`. Add new findings to `annotate/hand_notes.py` and regenerate (command in the script's docstring).

## Out of scope

- Multiplayer (DirectPlay/IP: `JoinGame`, `STARTNET`, `SyncWin`, `fnSetIPAddresses`).
- Videos `Welcome.avi` / `Final.avi` (referenced but not shipped).
- Licence/beta/expiry checks, Byte Enchanters sales links, the English "Lesson 1–11" tutorials (Legal Crime only).

---

## Bugs to fix (reported by the user, 2026-10-28)

- [x] **Fog of war graphics (user, 2026-10-29).** The fog is drawn with tiles from CHICAGO2.bmp: tile (0,0) is the
  plain fog tile; the other tiles from (0,0) to (4,1) (all but (0,0)) are fog edge tiles, so the fog doesn't stop
  abruptly at the border of the seen area but "slides into" it. Today the port only darkens unseen cells (alternative
  tile 1). To do: draw fog cells with the plain tile and pick the edge tiles by which neighbours are seen (find the
  EXE's edge selection, near the map renderer that reads the vision grids).
  Done 2026-10-29 (`FogTiles`, sub_438C20): unseen cells get Chicago.tile 49h "DARK SQUARE"; a seen cell gets the
  edge piece 9C8h–9CFh of each unseen neighbour ((−1,−1), (0,−1), (+1,−1), (+1,0), (+1,+1), (0,+1), (−1,+1), (−1,0)),
  composed per combination; drawn after the cell's own tile. Fixed after the user's check: only R road and S
  pavement (class 2 ground) get fog and count as fogged — building blocks never; the pieces are chosen by the
  neighbour's screen direction (their names), not the EXE's map offsets (our grid is oriented differently).
  Second fix (user screenshot: edges piled on each other): every piece is turned into a coverage map (full fog
  0.5, edges = local dot density, the highest where pieces overlap) and dithered by one 4×4 pattern on the map's
  pixel grid, so overlapping pieces never get darker than full fog. The square is
  dithered by a shader on the map's pixel grid (our half-pixel rows would misalign per-tile dither).
  Third fix (user screenshots fog_original / fog_remake: the original sees one cell less, and fog edges came out of
  the blocks past a corner): the unrolled sight routines for radius 3, 4, 8 and 9 hide the outer square ring and every
  cell farther than r, so a radius-8 unit sees a round area of radius 7; the cells that had leaked round the block
  corners on ring 8 are gone. "Seen" in the EXE (vt+0DCh sub_438B80) = marked in the last 61 game minutes (one
  think), or the local player's object. Done 2026-10-30: `FogOfWar` keeps the minute each cell was last in sight
  and shows it while now < mark + 61, so the fog closes about a game hour behind a viewer (the original marks once
  per unit think; ours marks continuously).

- [x] **Draw order of walking units.** While a unit's animation moves across the map, the tile below (in front of) it
  is drawn over the unit sprite at some points: Y / Z sorting is wrong for the isometric map (units vs. block tiles).
  Done 2026-10-29: flat ground tiles (picture one floor tile high, 19 px — most R/S/F/G and every B/M/H cell) are
  drawn on z −1 under everything; only tall pictures (buildings, trees, lamps) are y-sorted with the units, so the
  diamond of the cell in front no longer covers a unit's feet. Watch for remaining cases on tall road tiles.
- [x] **Companies hidden until discovered.** At the start of an episode companies under the fog of war must not be
  visible; the player has to reveal the map to find them. Once seen, a company keeps its last seen state in the fog
  (as now). Done 2026-10-29: `Company.Discovered`, set by `FogOfWar`; undiscovered companies are hidden and can't be
  clicked or hovered (their building tiles stay on the darkened map).
- [x] **Collector visits.** When a collector (courier) takes money from a company or business, it "enters" the
  building and disappears from the map for a while, then reappears carrying the money and walks on to the next one.
  When it reaches the HQ it disappears for good (delivered). Today it stays visible next to the building.
  Done 2026-10-29: `Unit.EnterBuilding` / `ExitBuilding`; the collector is inside during the job's hour wait at each
  company (sub_428D62) and, after delivering, inside the HQ until its next round (so it vanishes at the HQ; the EXE
  job keeps serving the block from there). Inside units are hidden, not targeted and not hit by area attacks. Also
  fixed: the fog refresh showed arrested units again.

- [x] **Mass selection.** (1) The drag selection must be a rectangle on screen. (2) It selects units only — never
  companies or collectors (couriers). Done 2026-10-29: a box is drawn while dragging (`SelectionBox`); it picks the
  player's own gangsters whose position is inside (no companies, collectors, police or other players' units); a drag
  under 4 pixels is a click.
- [x] **HQ of a bankrupt player** (traced 2026-11-02). sub_43B677 → sub_43B711 → World::RemoveObject sub_43AB50,
  which calls the player's vt+28h (sub_430A40: only +88h = 0, the "alive" flag) and drops it from the object list.
  Nothing in the EXE touches the player's HQ, units or companies. sharptry follows the user's observation of the
  game (assets released, units gone) and removes the HQ too (`Company.RemoveHq`), since an HQ can't change owner.
  The other removal paths are multiplayer only: sub_429658 (a player leaves, texts 25,x) and sub_43C052 (network
  drop, 19,x, from the network handler sub_41D700).
- [x] **HQ placement.** An HQ must stand on a medium standalone building — letter B in the block description map
  (Chicago.tile block rows) — not on any company cell. Done 2026-10-29: the HQ takes the B cell nearest to the
  start point (original coordinates, within 12 cells) that no other HQ took. On most maps the start point itself is a
  G cell one row off its B (original y − 1); SLUM_CITY is one row the other way, LOST / CAPONECITY / QUARANTINE /
  SMALLNEIGHBORHOOD up to 8 cells. Unit tests `MapTests` check every map.

- [x] **Order click animation (user, 2026-10-29).** When the player gives a unit an order (walk, attack, acquire,
  etc.), an animation plays at the clicked point. Frames come from Signs.bmp: 6 sprites in the rectangle at
  (288, 66), 71 × 99 px, 3 rows of 2. Done 2026-10-29 (`OrderMarker`): Chicago.tile 0AD3h "Attack cursor" (6 frames
  36 × 33). The map view keeps the order's target (+0A28h, set by the map click sub_411F84 after the orders and by
  the handlers at 0x413FD8 / 0x4141BE / 0x41438A) and a frame (+0A24h); sub_40B914 draws one frame per redraw
  (timer about 100 ms, sub_4198A0), bottom-centred on the target (a unit: its walk position − 2 px; else its cell
  + 1 px), and clears the target after the last frame. Ours: right click and order-button clicks that order at least
  one unit; it follows a clicked unit. MCP-checked (slowed down: the frames play at the clicked road cell).

- [x] **Collector sprites with and without money (user, 2026-10-29).** The collector (courier) has different walking
  sprites when he carries money and when he walks empty. The sprites with money are in OWNMAN.BMP; the ones without
  money, and the death animation, are in OWNATTAC.BMP. Done 2026-10-29: the unit picture (vt+68h sub_42D8D0) checks
  the carried money +0A4h in its collector cases. `Unit.CarriesBag`: with money the collector walks with the
  "Collector SE/NE/NW/SW" tiles (units.json attack* layers), stands on their first SE frame and dies as "Collector
  killed" (killed2); without money the "no money" tiles. Changing `CarriedMoney` switches at once. MCP-checked
  (spawned collectors walking, money given and taken mid-walk). New Debug hooks: `Spawn(type, money)`,
  `Carry(index, money)`, `Walk(index, x, y)` (index over all the player's units).

- [x] **No income while a business is being built (user, 2026-10-29).** A business under construction (conversion
  job sub_427050, until the catalogue +1Ch build minutes pass) earns nothing. Done 2026-10-29: the income tick
  (sub_420ED5) adds nothing while a job runs on the company (vt+0A0h) — conversion or repair (sub_4289E8 sets the
  job too); support upkeep is still charged. `Company.IncomeTick` skips while `Busy`. The panel income line is unchanged:
  the EXE's panel (sub_40A997, 0x40B39C) prints vt+188h without looking at the job, next to the job's progress bar. Scenario check "a business under construction earns
  nothing" (scripted conversion at hour 10; fails without the fix).

- [x] **Bribe "stockpile" bars (user, 2026-10-29).** When part of the income goes to an authority, small green lines
  must grow to show how many points that authority has in its "stockpile". Done 2026-10-29 (`SharesPanel`,
  sub_40A245): under each authority's coin bar (rows 0x476C08: x 546..680, bottoms 498 / 525 / 553 / 580) a green
  (00FF00) bar 2 px high from bottom − 3, x left + 1, √favour / 40 (dbl_476D70) × 112 px (row − 22); past full width
  (1600 points) it grows by (points − 1600)^¼, at most 20 px more. Favour is HQ +0B8h (vt+0C0h sub_432D20). Unit
  test `FavourBarFollowsTheSquareRoot`; MCP-checked with 100 / 400 / 1600 / 5000 points.

- [x] **Market share grows with built businesses (user, 2026-10-29).** When a business is built, the owner's market
  share of that type grows, and with the share the income grows (from the minimum to the maximum). Check the current
  model (`Organisation.MarketShare`, floor unk_4C4B40, multiplier steps unk_4881F4) against this in play.
  Checked 2026-10-30: the hourly statistics (sub_430C30) add each owned company's income to its type and the share is
  own / max(everyone's, 5 000 000) (`Organisation.ShareOf`); the type's income multiplier steps from ×1 to ×2.25
  (sub_430BE8). Fixed: the statistics skip a company while a job runs on it (vt+0A0h — conversion, repair), as the
  EXE does, so a business counts toward the share only once it is built. Unit test
  `MarketShareGrowsWithIncomeAndRivalsCapIt`.

- [x] **Market share competition (user, 2026-10-29).** A more expensive business gives more market share. When several
  competitors build the same business type, they compete for the share: nobody can reach 100% while anyone else still
  has at least one business of that type. Check our share formula (share against total income per type) gives this.
  Checked 2026-10-30: yes — the share is weighed by income (pricier rows earn more), and any rival income of the type
  keeps everyone below 100 % (same unit test). A rival's business that earns nothing this hour (blackout, frozen,
  being built) doesn't count.

- [x] **Units sent to the same tile (user, 2026-10-29).** In the original, units sent to exactly the same tile arrive
  and merge into one cell, then are "teleported" to the empty tiles around it by a simple rule: 1 unit = 1 tile.
  Done 2026-10-30 (`Unit.SpreadOut`, `MayStayOn`): the idle branch of the hourly think (sub_42AA86, 0x42B2E4) calls
  unit vt+15Ch sub_42B500. A unit with no job (+0A8h, set by vt+9Ch), not in a car, that may not stay on its tile
  (vt+160h sub_42B490: another living unit without a job is there) steps one tile (vt+140h, a normal step) to a
  neighbour, 4 directions from a random start (tables dword_484604..10): first a road or pavement tile (tile
  vt+158h = 1) it may stay on, else any road or pavement neighbour. Not found where I first looked: the move job,
  the tile lists, the unit draw and the map click have no spreading. Scenario check "units sent to one cell spread
  out" (two idle units sent to one cell must meet, then stand on different cells; fails without the spread).

- [x] **Playtest fixes (user, 2026-10-30).**
  1. Police cars and Cadillacs keep to the road: pavement (S) refuses unit type 5 and class 0Ch, the police car
     (sub_432E80; PoliceCar::PoliceCar vtable off_48D928 → class 0Ch). `Unit.RoadOnly` for moves, the spread-out
     step and placement (`FreeCellsAround(roadOnly)`: new cars, police cars, trained Cadillacs). I ground stays open
     to cars (sub_420C80 = 1). A raid car can't reach a business behind its pavement ring, and our detours then
     circled the block (the six-cell memory forgets): the raid now starts when the car stands within 2 cells
     (Manhattan) — not from the EXE. Scenario check "cars keep off the pavement".
  2. A click on the pressed button of the unit in training cancels it (`Training.Cancel`; nothing is paid before the
     end). The EXE's cancel path wasn't found (the recruit actions sub_4323F0 only start jobs).
  3. Force Attack can target own and allied units (`CanAttack.ForcedTarget`; cleared by a Go order).
  4. Extortion keeps the attack animation between its hourly attempts and drops it when the order ends.
  5. The panel's name line is the catalogue name of the row as last seen (company vt+18h sub_421CC0 → +0D4h,
     refreshed by vt+0E0h sub_420E50 when seen), so a built business shows its type. (State 5: text 30,19; not
     modelled.)
  6. `game.cfg` [map] `company_chance` (default 0.9; the EXE's 3 in 4) for B / C cells, and
     `min_one_company_in_rich_blocks` (middle-class and center blocks get a business, a C cell first). Scenario
     check "middle-class and center blocks have a business (user)".
  Seen once while testing: after clicking an own company the panel text showed but the portrait box stayed black —
  fixed 2026-10-31 (see "Company avatar not updating").

- [x] **Minimap (user, 2026-10-30).** The original has a minimap in the bottom-left corner of the panel (where the
  remake now shows only the dollar-pyramid background, about x 10..210, y 390..590). Done 2026-10-31 (`Minimap`,
  sub_40DD60): box (10, 390)..(210, 590) (dword_476BA8..B4). Map layer (sub_40E378 full scale, sub_40DF43 half
  scale when width + height > 200), drawn once and colour-keyed over the panel: a cell = 2 pixels at its screen
  position / (20, 10); road and pavement grey 808080, police stations blue, the rest silver C0C0C0; the right pixel
  of an unseen cell black (a dither over the unexplored map). Live layer (sub_40EBD0 / sub_40E665): companies by
  relation (own green, enemy red, ally yellow, nobody's blue), seen units (own green, collectors dark green, allies
  yellow, enemies red, police cyan; a police car even unseen), anything fighting (vt+0D4h → vt+11Ch) blinks black
  every other redraw (dword_476F50), white view rectangle. Colours from fnMainLoadBMPs (0x40FD33..). Ours redraws
  the map layer every game hour (the EXE on a full view redraw, sub_410110 arg). A click / drag in the box moves
  the view (not looked up in the EXE). Half scale samples every cell at half the pixel size, not exactly the EXE's
  2×2 pick. MCP-checked on Harlem.

- [x] **Key shortcuts (found in the EXE, 2026-10-31).** sub_410EC3 (letters either case) presses panel buttons
  (+20h + 78h × slot): T/P/M/B/S/L/K/C/I recruit slots 6–14 (Thug, Pistol, Thompson, Terrorist, Sniper, Shotgun,
  Hit man, Cadillac, Thief); G/A/E/D/O order slots 0/1/2/4/5 (Go, Attack, Extort, Disguise, Patrol) with their
  cursors; H puts the view on the own HQ (sub_409FE7). Done (`Hotkeys`, physical keys so a Russian layout works).
  R repairs the selected own idle company (its slot 5, company vt+0BCh). Digits: Ctrl + n stores the selected own
  units but collectors as group n (+948h + 20 × n); n selects the group, and when it is already selected moves the
  view to it (sub_409FE7). MCP-checked (Ctrl+1, deselect, 1). Open: what Ctrl changes for M / I / G / A, and Shift
  with a digit (GetKeyState 10h).

## To fix (found while commenting the EXE listing, 2026-10-26)

Source: `godottry/disassembled/Capone.EXE.commented.lst` (search the address).

- [x] **Cadillacs take building damage.** Done: `CanAttack.RollDamage`. `Unit::GetMinDamage`/`GetMaxDamage` (`sub_42C890`/`sub_42C960`) use `DamageMin/Max` only when the target is a unit (class 5) that is not a Cadillac (type 5); buildings **and Cadillacs** get `BuildingDamageMin/Max`. `CanAttack.RollDamage` currently uses unit damage against Cadillacs.
- [x] **Hit man special case.** (Done: `Unit.ExposedHitmanDamage` in `CanAttack.RollDamage`, with the disguise / 30-hour rule.) In the same getters a Hit man (type 9) returns a fixed 20 damage after a time check against unit field +0D0h (probably: not disguised / recently revealed); otherwise the table's 161–162. Decode the check and model it with the disguise (§5).
- [x] **ALLIANCE is mutual.** Done: `CampaignManager` sets both directions. The loader calls `a->SetRelation(b, 2)` and `b->SetRelation(a, 2)` (0x467714 / 0x467721). `CampaignManager.InitAlliancesFromScenario` sets only `from → to`; set both directions. (The projectinfo spec calls it one-way — the EXE says otherwise.)
- [x] **Random personal names.** (Done: `RandomNames.Gangster()` in `CampaignManager.CreateUnit`.) `Unit::Unit` names every unit except the Collector with `fnGetRandomParameters(world, 1, player)` (0x42A27E); the Collector gets text 1Dh/0Dh. Find the name list and give spawned units a personal name (episode names still override via unit vtable +178h `SetName`).
- [x] **Unit HP field.** The unit stores its current HP at +20h (set to MaxHP at creation; vtable +0F4h returns it, +1A8h sets it, +1A0h sets 0). Matches `CanBeAttacked.HP`; no change needed, recorded for reference.

## To remake (found in manual test, 2026-10-26)

- [x] **MENU button is wrong.** Done: button = the 48×14 "Меню" images at (158,42) normal / (210,42) pressed, measured from the bitmap (EXE coordinates still not located); the drop-down shows Menu.bmp cell 0, hover shows cell i, item zones detected from the bitmap. Was: `GuiMenu` uses the whole `GAMEBUTT.bmp` and cuts it in half vertically. The button is only a small part of the bitmap: two "Меню" images side by side, normal and pressed (see `godottry/projectinfo/image-1.png`). Find the exact rectangles (Chicago.tile record or EXE coordinates) instead of guessing. Check the `Menu.bmp` cutting the same way.
- [x] **Attack range against companies.** Done: `CanAttack.BuildingAttackRange` = 1.5 for companies. Was: Units shoot companies from their full attack range. In the original, ranged attacks only work against units; a company can only be attacked from a neighbouring tile. Ranged units must walk up to the building.
- [x] **Company logos are wrong.** The signs shown on companies (`companies.json` sprite names, per owner/type) don't match the original. Rebuild the mapping from Chicago.tile names (e.g. "Russian open sign", "Own protected business", "Own Bootlegging sign", "Power off … sign") and check against the original game.
  Done 2026-10-31 (company draw sub_422276, HQ draw sub_415E07): nobody's → "Russian open sign" (0DC8h); legal →
  "protected business" own / enemy / ally (70Eh / 70Fh / 70Dh); otherwise catalogue +28h first — 4 training center,
  3 collection center, 7 the troubleshooting sign — then the type (bootlegging, entertainment, gambling,
  troubleshooting), anything else "illegal business" (710h / 711h / 9D3h, new `Illegal` sign type). A block without
  power shows the "Power off …" picture (also the HQ: 0DCCh, the second "Russian ally Hq", `@1`); the own sign
  stands still while the company earns nothing (+0CCh). Fixed: training centers showed the troubleshooting sign.
  Unit test `CompanySignsAreChicagoTiles`.
- [x] **Fixed** (`CanAttack` keeps the target object and looks its health component up on each use; re-checks hostility before attacking): **`System.ObjectDisposedException: Cannot access a disposed object. Object name: 'Capone.CanBeAttacked'`** during play. Likely cause (not verified): a unit's `CanAttack.Target` still points at the `CanBeAttacked` of a company whose `CompanyCommon` was replaced by `Company.SetType` (capture) and freed, or of a dead unit. Targets need `IsInstanceValid` checks and must be cleared when the component is replaced.

## Known issues (carried over)

- [x] Clicks on GUI controls (menu, bottom panel) no longer select/deselect on the map: `Main` uses `_UnhandledInput`.

- [x] **Company avatar not updating** when selecting a different company: check `SelectionIcon.UpdateIcon` on reselection.
  Fixed 2026-10-31: the portrait control (keep-size mode) shrank to 0 × 0 when its texture was cleared while hidden
  and stayed so — the panel showed the text and an empty portrait box. It is now a fixed 84 × 95 box. Found with the
  new Debug hook `ClickAt(x, y)` (a map click through `Main.ClickSelect`).
- [x] **Company portraits**: `SelectionIcon.CompanyPortrait` uses Chicago.tile "info …" pictures per grade as placeholders; find the original building → picture mapping.
  User, 2026-10-29: corner buildings show the wrong portrait. The portraits are in BLOCKINF.BMP, a 4 × 4 table. From
  playing the original (not 100% sure; the user will try to get the original data), by the letter in the block
  description table, as (row, column). The choice depends on the neighbouring letters in the same block:
  - M: 0,0 or 2,2
  - H: 1,0 or 2,0
  - C in small blocks (3×3, 5×5): 3,0, 3,1 or 3,2
  - B: 2,1
  - C in standard blocks with M: 0,3
  - C in standard blocks with H: 1,2 or 1,3
  - P with M: 0,2
  - P with H: 1,1

  Done 2026-10-30: the EXE picks the portrait by district and grade, not by letter (company picture vt+68h
  sub_422158): no block → extra 1 (C14h); slum → slum (C1Eh); middle class: grade 3 theathern (C13h), 2 L-house
  (C16h), 1 news stand (C1Ch), else hall (C1Dh); center: grade 4 hotel 2 (C1Bh), 3 hotel 1 (C17h), 2 central 1
  (C15h), else L-house. An owned HQ shows the HQ picture 0D8Dh (sub_415258); police stations C18h (sub_429D30); a
  generic building by district (sub_4025F0: C1Eh / C1Dh / C1Bh, else C19h). Not modelled: state +18h = 5 (set by sub_422D82 when a company
  is released and the local player isn't the owner) shows hall / hotel 2. Unit test
  `BuildingPortraitsAreBlockInfPictures`.
- [x] **HQ placement**: the HQ now stands on the start point itself, as in `sub_404098` (a business there is replaced).
- [ ] **RID leaks at exit** with `--headless --quit-after`: harmless.
- [x] ~~Group (rectangle) select~~: fixed in the C# port.
- [x] ~~PICS dir check with `res://L:/…`~~: not present in the C# port.

- [x] **Sounds (2026-11-02).** `Sound.PlayId` plays by the EXE's table (off_478700). Idle chatter (unit step
  sub_42E660): each idle-animation round ticks a countdown shared by all units (sub_408BB2, restarts at
  GetTickCount % 100); when it fires the unit's counter (+0DCh) grows, and an own unit past 15 says one of five idle
  lines from its type's base (Pistol 80h, Thompson B9h, Terrorist 60h, Sniper A1h, Shotgun 95h, Hit man 74h, Thief
  ADh, others the Thug's C5h; GetTickCount % 5). Screens use the second set (0x477A1C): Welcome.wav when the main
  menu opens (0x41B7E0), ButtonClick.wav on a screen button press, OverButton.wav on hover (sub_409380 /
  sub_407760). No sound was found for the in-game panel buttons. Network-game sounds (NewGame / NewGamer) skipped.
  Unit test `SoundTableMatchesTheExe`.

- [x] **Menu: Exit asks first (2026-11-02).** sub_411F84 opens the info window in mode 2: title "Покинуть игру"
  (5,82), lines 0 "Покинуть игру" (5,83) and 4 "** Вы уверены? **" (5,84), buttons OK (7,1) / "Вернуться" (7,2)
  from InfoWin's strip (0, 340, 400, 40). OK leaves the episode for the main menu (`Main.ConfirmLeave`; the game no
  longer quits outright), Back closes the window. Still stubs in the menu: alliances (AllyWin), messages,
  (statistics, reroll, money transfer and alliances done 2026-11-02, below; messages is multiplayer chat).
  Fixed with it: the game clock is an autoload, so after leaving an episode (or after the end screen) its timed
  events ran on in the menu — the left player was "eliminated" and a defeat window opened over the main menu. The
  main scene now resets and pauses the clock when it starts; starting a scenario runs it again.

- [x] **Menu: Statistics window (2026-11-02).** Main window +0A20h: sub_45FD50 computes, sub_460206 loads
  `Pics\GameRank.bmp`, sub_460290 draws, sub_4603F0 / sub_4603A2 handle the mouse. GameRank's top 600 × 400 at
  (100, 100), "Ваше положение относительно других кланов"; a hand (50 × 60 at (100, 400), colour key = the pixel
  at (239, 421)) per column at x 140 / 258 / 376 / 496 / 602 (table 0x48E4EC), y = 135 + place. Place =
  H − (value − min) × H / (max − min) over the players in the game (vt+54h), H = 375 − 135, clamped; 120 when
  all are equal. Columns: Деньги = cash (vt+60h), Доходы = weekly net income (vt+64h), Гангстеры = own units
  other than Collectors, % = own legal companies (class 0, vt+1CCh = 0; vt+1CCh = catalogue type ≠ 0), $ = own businesses
  (vt+1CCh ≠ 0). Computed once on opening; a second menu select does nothing while it is open. "Вернуться"
  (350, 468)–(450, 493) shows (0, 400, 100, 25) under the mouse (OverButton on entering) and closes on a click
  (ButtonClick); Escape / Enter close it too (remake convenience). `RankWindow`, unit test `RankPlacesFollowTheExe`.

- [x] **Menu: Переиграть (2026-11-02).** The menu items in sub_411F84 by rectangle (0x476C48..0x476CB4, x 5..96):
  y 18 alliances, 33 messages, 48 statistics, 63 money, 78 tips, 93 "Переиграть", 108 exit. "Переиграть" (single
  player only: game +186h = 0, sub_414DA0) opens the same "Покинуть игру / ** Вы уверены? **" window as Exit, but
  with action 8 instead of 5 (main window +0D888h, sub_41EA00). On OK, fnMainWndMsgProcess case 8 ends the game
  (sub_419D00), shows "loading" (9,43) and builds the same episode again (sub_43A5A7, episode dword_4A0E74) — no
  briefing. `Main.ConfirmLeave(restart: true)`. Action 5 (exit) goes back to the main menu (sub_408EA0).

- [x] **Menu: Деньги = money transfer (2026-11-02).** Main window +0AE4h: sub_4629A6 lists the other players in
  the game, sub_462B86 loads `Pics\MoneyWin.bmp` (key pixel (367, 312)), sub_462C13 draws, sub_4630A8 / sub_463450
  handle the mouse. "Перевести деньги другим игрокам": tick boxes in 3 columns × 6 rows, the amount slider (the
  generic slider class sub_406996: positions 0..19, arrows step by one, track click jumps and drags) gives
  50 000 × (position + 1). "Перевести" posts event sub_429470 per ticked player; sub_4294B0 pays when the giver
  can afford it (vt+70h sub_430951: cash − amount + 200 000 ≥ 0): giver Spend, receiver Deposit (split by its bribe
  shares), messages 25,3 / 25,4 and sound 4 for the local side; nothing when refused. `MoneyWindow`, unit test
  `MoneyTransferFollowsTheExe`. The original draws the ticks of columns 2–3 off the list (y not reduced); drawn in
  place here.

- [x] **Menu: Альянсы (2026-11-02).** Main window +944h: sub_45B796 builds, sub_45BA00 loads `Pics\AllyWin.bmp`
  (key pixel (349, 397)), sub_45BA83 draws, sub_45BD30 / sub_45C0A0 / sub_45BF06 handle down / move / up. Four
  squares: columns = the local stance, rows = theirs (player vt+0E0h: 1 not cooperating, 2 cooperating). Each
  other player is a hand with the name below; start slots from tables 0x48E010 / 0x48E078 / 0x48E0E0 / 0x48E148
  (13 each, cycling); a hand is dragged sideways in its row and snaps into a square (bounds 0x48E1E8, middle x 378,
  y 329). "Изменить" posts event sub_432AA0 per player (left column 1, right 2) → vt+0DCh sets only the local
  stance. Here the unit pictures (own / ally / enemy sets) and company signs of that player are refreshed at once
  (`Unit.RefreshOwnerRelation`, `Company.RefreshOwnerRelation`; the original reads the relation when drawing).
  `AllianceWindow`, unit test `AllianceSquaresFollowTheExe`. Open: whether computer players react to it.

## Playtest (user, 2026-11-02)

- [x] **Campaign select: Up / Down buttons do nothing.** On the briefing (campaign select) screen the Up and Down
  buttons must switch the current episode between 1 and the highest unlocked one (`max_reached`: starts at 1 and
  grows with each won episode). Done 2026-11-02: the arrows painted in ScenDisp.bmp (up 728,98 / down 728,426,
  48 × 25; the EXE's scroll control at briefing +28h, 7 px per episode) switch the briefing to the next / previous
  episode within 1 .. `CampaignProgress.MaxReached`. MCP-checked (4 → 3 → 4, stops at 4).
- [x] **Takeover sound in episode 4.** The player has only four units, yet the "company taken over" sound plays
  when a computer player takes a company. Check which sound plays and whose takeovers should be heard (only the
  local player's?). Fixed 2026-11-02: in Company::Extort (sub_422FB4) the pay voices (ids 45h–4Ah, BusinessPay)
  and the first-refusal voices (30h–33h…, BusinessRefuse) play only when the extorting unit is the local player's;
  ours played them for every player.
- [x] **Death animation is wrong.** When a unit dies its animation plays incorrectly. Check the killed layers per
  unit type (units.json `killed` / `killed2`, Chicago.tile "… killed"), frame order, looping and how long the
  corpse stays. Fixed 2026-11-02: the death pictures (Chicago.tile "… killed" / "… die", 2 frames: falling, lying)
  looped for the 3 hours the body stays; now they play once and hold the lying frame. Types without death pictures
  (Thief, Cadillac, Policeman) kept walking / shooting in place; now the body stops. MCP-checked (killed:1 still).
  Not verified against the EXE: the exact frame timing of the fall.
- [x] **No defeat when all units die (episode 4).** When all of the player's units are killed the episode doesn't
  end in defeat. Find the original's defeat rule (bankruptcy sub_43B677, losing the last gangster?) and apply it.
  Done 2026-11-02 as the user's rule: the EXE eliminates a player only when its HQ is lost (sub_4151C5 / sub_415F20)
  or by bankruptcy in Spend (sub_430860), and VCOND 3 (episode 4's only condition) has no failure branch; episode 4
  starts the player without a base (PLAYER 1 0 0). Now the hourly statistics eliminate a player without an HQ who
  has no unit left (jailed units count) and trains nobody → "Game over" (19,1). Scenario check "a player without an
  HQ who lost every unit is defeated (user)" (episode 4).
- [x] **End-of-episode popup.** When an episode is won or lost, a popup window (HelpWin.bmp or InfoWin.bmp) must
  first show SUCCESSTEXT or FAILURETEXT; only then the end screen with the charts. Now the end screen opens at once
  and the result text is drawn on it, which is wrong. (EXE: info window class sub_447C10 / sub_447A41 with texts
  5,52h / 5,53h — see sub_411F84 near 0x412B2E.)
- [x] **Tip popup at the start.** When an episode starts a popup (HelpWin.bmp or InfoWin.bmp) must show TIPTEXT
  from the .epi.
  Both done 2026-11-02 (`InfoWindow`, `Main.ShowInfoWindow`): the EXE's info window (sub_447A41 / sub_447A97,
  drawn by sub_447CB0): InfoWin.bmp 400 × 300 at (200, 150) plus its one-button strip at (200, 410); title at
  (205, 152) font 2 yellow; lines from (210, 180) 15 px apart, font 3 green; OK (7,0) at (380, 418), button
  (350, 418)–(450, 440) with the pressed picture (0, 380, 100, 25). At the start (sub_40A160 → sub_414760(1)) the
  TIPTEXT under "Полезный совет" (5,85); at the end (sub_41D3E0, message kinds 2 / 3) the result text under
  "Игра окончена!" (9,37), the end screen after OK; the end screen no longer draws the text. Also: without an HQ
  the view starts on the player's units. MCP-checked in episode 4 (tips → OK → defeat → OK → end screen).
