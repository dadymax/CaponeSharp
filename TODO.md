# TODO — Known Issues

## GUI / Selection
- [ ] **Company avatar not updating** — avatar texture doesn't change when selecting a different company. Need to check if `update_icon()` is called correctly on reselection.
- [ ] **Avatar tile index offset** — `AvatarTileIndex` in `companies2.json` must be 0-based tile array index (file line number minus 58). Example: line 3159 → index 3101.

## Map Generation
- [ ] **HQ placement** — `createTileMap.gd:157` — HQ placed "very wrong" when no base company found at start point. Needs fallback logic.
- [ ] **Group select not working** — `node_2d.gd:105` — rectangle selection for multiple units is marked as FIX ME.

## Data Loading
- [ ] **PICS dir check fails** — `gui_gameback.gd` checks `res://L:/work/...` path which doesn't exist (absolute path mixed with `res://`). Not critical since BMPs load separately via absolute paths.

## Memory
- [ ] **RID leaks at exit** — Normal for `--headless --quit` (clean shutdown not triggered). No issue during normal gameplay.
