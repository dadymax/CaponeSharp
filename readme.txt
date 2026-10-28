CAPONE (C# port) - HOW TO TEST THE CURRENT STATE
================================================

1. What you need
----------------
- The original game data folder (Capone.EXE, Chicago.tile, Maps, PICS, Sound, episode).
  Its path is set in originalData/original.cfg:
      [directory]
      originalGameDirectory="L:/work/CaponeDisassemble/godottry/original_game"
  Change it if the folder is somewhere else. The game only reads it, never writes.
- Godot 4.7.2 .NET (mono) build: ..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe
- .NET SDK (to build the C# code).


2. Build and run
----------------
In a terminal, in this folder (sharptry):

    dotnet build CaponeSharp.csproj

Play (window):

    ..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe --path .

Start the episode straight away, without the briefing screen:

    ..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --skip-briefing

Quick data check (no window; prints "Smoke test done: 0 failure(s)"):

    ..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . -- --smoke-test

Or open the folder in the Godot editor (same .exe without arguments) and press F5.


3. Choosing an episode
----------------------
The game starts at the first episode not yet won. Progress is kept in
    %APPDATA%\Godot\app_userdata\CaponeSharp\campaign.cfg
To jump to episode N, write this file (N = 1..21):
    [campaign]
    current=N
    max_reached=N
Delete the file to start again from episode 1.

Good episodes to try:
    1   tutorial: extort, collect money, win by income
    2   business conversion goal
    4   first computer opponent (Big Jim), fights
    13  aggressive opponent (Torrio)
    15  both sides have an HQ: HQ menus, raids, attacks on your HQ
    8   six players


4. Controls
-----------
Arrow keys                move the camera
Mouse wheel               zoom
Left click                select a unit, company or HQ
Left drag                 select several units with a box
Right click               order: walk there / attack the clicked unit / go to a company
+ / -                     game speed x2 / /2 (one game hour = one real second at x1)
Bottom panel buttons      orders of the selected object (hover shows the text):
    own unit:     Go, Attack (Shift = Force Go / Force Attack), Extort, Disguise, Patrol
    other unit:   Ask cops to arrest gangster, Pay snitch to switch sides
    own company:  convert to a business, sell, repair, primary training center
    other company: police raid, demolish, brownout
    own HQ:       Mayor bribing investigations, legalization, primary center, repair
    other HQ:     FBI visit, investigations, rumours, announce death, FBI snitch
Recruit column (right)    train gangsters at your HQ / training center
Shares chart (right)      how income is split between savings and bribes
Authority services appear only when you have bribed enough money and favour points;
raise the bribe shares and let income come in.


5. What works now
-----------------
Briefing, map and units from the original files; extortion, income, collectors,
police and arrests, lawyers, recruiting, business conversion, selling, repair;
fog of war; combat with the original order modes; Disguise and the Hit man;
computer opponents (extortion, recruiting, conversions, attacks); HQ services;
raids, demolition, brownout; victory conditions, defeat, elimination,
last player standing; deserting unpaid gangsters.

Known missing parts: main menu, statistics chart on the end screen, many sounds,
Thief stealing, Cadillac passengers, the original tile-by-tile movement
(units can stand on the same cell), rubble after demolition.
Full list: TODO.md and PLAN.md.


6. Test helpers (for automated testing)
---------------------------------------
The node /root/Node2D/Node2D/Debug (Scripts/DebugCommands.cs) has helper methods
used through the Godot MCP tools, for example:
    Status(), Speed(8.0), Give(1000000), Favour(4, 100.0, 1000000),
    Fog(false), Select("hq", 0), DamageHq(2, 1300), Ai(), Owners(), HqState(1)
They are not meant for normal play.
