# Plan: episode 1 complete in C#, 1:1 with the original

Goal: `ep1.epi` ("Билет в один конец") playable from start to end in sharptry and behaving like the original:
briefing → extort companies → collectors bring money → money/income grow → (optionally) upgrade companies to
businesses → win at the objective (or lose) → success/failure screen. Everything from the original files.

`TODO.md` keeps the long-term backlog; this file is the route for episode 1. Addresses refer to
`godottry/disassembled/Capone.EXE.commented.lst`.

---

## What episode 1 is

From `ep1.epi` and the EXE:

- Map `Slum_City.map`, one player ("User", human), `FUNDS 200000`, `RANDOM 1`, no units in the file.
- Every base starts with a Thug and a Pistol man (`SetupPlayerBase` 0x404098) — done in sharptry.
- Objective `VCOND 1 1 500000`. The check (`sub_463610`, VCond1 vtable slot 0) takes the first object of
  class 6 and wins when its `vt+64h` value (field +14h) is **greater than** 500000. Class 6 is created by
  `sub_4305A0`, the HQ constructor called by `SetupPlayerBase`; it also holds the player id (+12Ch).
  Field +14h is most likely the player's cash (FUNDS goes there?) — to confirm (R8).
- Story/tip texts: extort the companies with the "Открыто" (open) sign and watch income grow; **police may
  arrest your gangsters**; don't bother with bribes yet; hiring is pointless while the two gangsters are free.
  So episode 1 needs: extortion, income, collectors, money display, police arrests, and the recruit panel
  (for when gangsters are lost).

## What the EXE already told us (new)

- **Company/business catalogue**: 161 records × 48 bytes at **0x47DDC0** (identical copy at 0x4737C0; the
  game code reads 0x47DDC0). Record: `+0 name*`, `+4 price`, `+8 income` (negative = upkeep),
  `+0C grade` (1 slum, 2 middle, 3 center, 4 top), `+10 type` (0 legal, 1 bootlegging, 2 entertainment,
  3 gambling, 4 troubleshooting, 5 support), `+14 HP` (500/800/1500), `+18` (4 / 200 / 400 — police favour?),
  `+1C` (≈ price/250), `+20` (1/2), `+24` (0–3), `+28` (2/3/4/7), `+2C` flag.
  Rows 0–41 are the legal companies (names from `companies2.json`; incomes 25200/37800/50400 by grade).
  Rows 42–160 are business variants with real names and prices, e.g. Пивной бар 75000 → 92000,
  Рюмочная 125000 → 127000, VIP-бар 250000; support: Укрепление (−100000), Обналичивание денег (−150000).
  Row 160 "Объект Х". (The projectinfo prices with "+1 police" don't match field +18 exactly — verify.)
- **Company class**: vtable **off_4803CC** (constructor `fnSetBuildingType` 0x420C90, 116 slots, class id 0).
  Its methods 0x420D80–0x4238B0 read the catalogue (income, price, grade, type, HP…). UI texts go through
  `fnMessagePrep(id, sub)` (message tables), not direct strings.
- **Collector**: dispatched **per city block** — `sub_41FF60`, a virtual method of the block object
  (vtables off_47CF28 / off_47CAA8). It compares the time against `sub_402CD0` = **10080** (= 7×24×60, a week
  in minutes), checks the companies in the block (`vt+54h`), finds where to start (`sub_41FEF0`), takes a
  free cell (`sub_43B955`) and creates a type-7 unit.
- **Time**: `sub_402CD0` = 10080 and `sub_416790` = 60 suggest the clock counts **game minutes**
  (60 = minutes per hour?). The "60 ticks per second" in `UnitCommon.OriginalTicksPerSecond` may be wrong.
  "Bang: Full year!" and monthly "Salary $%ld" exist. Must be settled first (R1).
- **Class ids**: 0 company, 5 unit, 6 HQ/organisation (money, player id), 7 player, 9 object created next to
  each HQ (`sub_415090`), 11 police station.

---

## R. Research (reverse-engineering) — do first, one note per finding in `annotate/hand_notes.py`

| # | Question | Where to start |
|---|---|---|
| R1 ✅ | **Game clock** (done: minutes, 60-min turns, 1 turn per real second; `GameClock`): what one tick is (minute?), how fast it runs per real second, game speed, calendar (week = 10080, "Full year!"). | callers of `sub_402CD0` / `sub_416790`, `sub_43C150` + `fnSetThisToa2` + `sub_402D20` time arithmetic, the main loop in `fnMainWndMsgProcess`. |
| R2 ✅ | **Company economy** (done: `Company`, event handler sub_420ED5, income sub_422AF4; see TODO §findings): company object fields; how income accrues (per week? per minute × income/10080?); "Uncollected"; "Extort %"; market share. | company vtable off_4803CC slots (+154h, +17Ch, +180h, +184h, +A4h, +A8h…), catalogue field reads at 0x421xxx–0x422xxx. |
| R3 ✅ | **Extortion/capture** (done: sub_422FB4 + order sub_428C11; cops threshold sub_422F50) of a legal company: what an attack on an "open" company does, refusal chance (sounds `BusinessRefuse1-6` / `BusinessPay1-6`), how ownership and extort % are set; HP of companies (catalogue +14). | company methods that call `fnMessagePrep` with the Pay/Refuse sound ids; replace `CompanyResistance` (a GDScript invention). |
| R4 ✅ | **Collector route** (done: block sub_41FC15/sub_41FF60, job sub_428D62, next company sub_429146, cash point sub_429221): dispatch interval and condition, start point (HQ or cash-out business, `sub_41FEF0`), which companies it visits (own, same block), pickup, return, deposit into HQ +14h, capacity, what happens if it dies. | `sub_41FF60`, the collector unit's methods (type 7 in Unit vtable paths), class 9 object `sub_415090`. |
| R5 | **Business conversion**: which catalogue rows are offered for a given building (grade + type), price, police favour requirement (+18?), legalization, repair cost (+1C?), sell price, freezing. | company methods reading +4/+0C/+10/+18/+1C/+20/+24/+28; the "Upgrade to …" / "Repair ($%ld)" / "Sell for $%ld" message ids. |
| R6 ✅ | **Money flow** (done: `Organisation` = HQ class 6: Spend/Deposit/UpdateStats/interest; salaries sub_42A535. Panel layout still R10): what +14h is (cash), income/expenses totals shown in the panel, unit salaries (`Salary $%ld`), upkeep (negative incomes), when they are charged. | class 6 methods (vtable off_488858, 72 slots), setters of field +14h. |
| R7 ◐ | **Police** (police order sub_4460C5/sub_4461A4/sub_4462B4 done as `Police`/`PoliceJob`; informers sub_42C503, raids, police car stats open) in episode 1: police station (class 11), "Police Batman"/police car units, arrests of gangsters (`PoliceArrest1-3`), raids. | `fnSetBuildingPolice` class methods, unit types 6/11. |
| R8 | **Episode end**: confirm VCOND 1 value; how often conditions are checked; failure conditions; success/failure screens and what happens next. | `sub_463610`, callers of the VCOND list, `GameWin*` / `GAMEOVER*` bitmap loading. |
| R9 | **Recruit**: training time (unit table +24h?), price, army favour (episode 1: none needed for Thug/Pistol?), spawn point, progress bar. | "Recruit … ($%s and %1.0f Army favour points)" message users, `Recruit.bmp`/`Blood.bmp` users. |
| R10 | **Panel layout and texts**: positions of money/income/expenses, info texts (Salary/Income/Uncollected/Extort/Owner), action buttons. | `fnMainLoadBMPs` (0x40FA16) surface object, message tables (`fnMessagePrep`). |

## M. Build (C#) — in order, each step testable in the running game

**M1 — Data readers** ✅ (`OriginalBusinessTable`, `OriginalTexts`, smoke-tested)
- `OriginalBusinessTable` (0x47DDC0, 161 × 48) → replaces the hand-written incomes/names in `companies2.json`.
- `OriginalTexts`: EN/ES/RU message tables used by `fnMessagePrep(id, sub)` (needed for every panel text).
- Smoke test checks both.

**M2 — Game clock** ✅ (`GameClock` autoload: minutes, scheduler, turns, `Speed` for tests)
- One `GameClock` autoload: ticks/minutes, week, month, year events; pause/speed. Everything time-based
  (income, collectors, salaries, unit speed) uses it.

**M3 — Company economy** ✅ (MCP play-tested on episode 1: extortion, income, VCOND 1 win on day 10)
- Company model from the catalogue: catalogue row, grade, type, owner, income, uncollected cash, HP.
- Extortion of neutral companies as in the original (replaces `CompanyResistance`).
- Income accrual into "uncollected" per clock rule.
- Fix company signs/logos (TODO "Company logos are wrong") using the catalogue type + owner.

**M4 — Collector autopilot** ✅ (play-tested: one collector per block, row-order pickups, HQ deliveries)
- City blocks as objects (from `.map` BLOCKS); each block dispatches a collector on the original schedule from
  the HQ (or cash-out business), visits the owner's companies in that block, returns, pays into the account.
- Replace the current `CollectorAI` (GDScript-era design) with the original rules.

**M5 — Money and panel** (after R6/R10) ◐ money model done (`Organisation`), panel numbers not shown yet
- Player account (HQ +14h equivalent), income and expenses; bottom-panel finance block with the original
  texts and positions; company/unit info texts; selection avatar.

**M6 — Business conversion** (after R5)
- Action buttons for a selected own company (Chicago.tile button tiles): upgrade to a business variant
  (price, police favour check), sell, repair. New income/upkeep from the catalogue.

**M7 — Episode flow** (after R8)
- Briefing (`ScenDisp.bmp`, SCENARIONAME/STORYTEXT/OBJECTIVETEXT/TIPTEXT), victory checks (VCOND 1 ✅ hourly on net income),
  success/failure screens (`GameWin*`, `GAMEOVER*`), next episode / replay.
- Camera starts on the player's HQ.

**M8 — Police and losses** ◐ (play-tested: car dispatch, arrest, jail, resign when unpaid; arrest on "owner calls the cops", jail, unit death/corpse; lawyers need M6 items)
- Police station behaviour and arrests needed by episode 1; unit death/removal.

**M9 — Recruit panel** (after R9)
- `Recruit.bmp` buttons, price/army favour checks, training progress (`Blood.bmp`), spawn at the base.

**M10 — Sound** for the events above (`Sound/*.wav`, EXE sound table at 0x4779B8).

## Definition of done for episode 1

- [ ] Starts with the briefing; the map, base and two gangsters match the original.
- [ ] Extorting "open" companies works with the original refusal/payment behaviour.
- [ ] Companies earn on the original schedule; collectors come per block, collect and deliver.
- [ ] Money, income and expenses in the panel match the original numbers over the same game time.
- [ ] Captured companies can be converted to businesses with the original choices, prices and incomes.
- [ ] Police behaves as in the original episode 1 (arrests).
- [ ] Gangsters can be recruited when lost.
- [ ] Reaching the objective shows the success screen; losing shows the failure screen.
- [ ] No hand-written game data: everything from the original files.

## Verification

- Keep `--smoke-test` green; add checks for each new reader.
- Compare numbers with the original game where possible (run Capone.EXE under Windows compatibility mode or a
  DirectDraw wrapper; note game time and money at fixed points).
