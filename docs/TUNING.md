# Tuning units and businesses

Everything here is read when the game starts: restart it after a change.

## Unit and business parameters

`Data/unit_stats.json` (every unit type: cost, hit points, damage, hit chance, range, rate of fire, reload, speed,
training time, sight) and `Data/businesses.json` (the business catalogue: name, type, grade, price, weekly income,
hit points, build time, district, support kind) hold the original game's values. Edit them and restart the game; a
missing file, unit, row or field keeps the original value. Each file starts with a `_fields` list explaining the
fields. `Data/rules.json` holds rule numbers: headquarters hit points, company healing, the extortion chance table,
the market-share income steps, legalization factors, the fortification's shot, the Hit man's damage after a shot, and
the authorities' services (the bribe money each needs and the favour points it costs).
To get fresh files with the original values back, delete them and run
`godot --headless --path . -- --export-params`. The smoke test reports files that don't load or contain unknown
fields, and warns when unit costs differ from the original.

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
   type, owner and activity is the picture method sub_42D8D0 (see [TODO.md](../TODO.md): e.g. the Thief walks with the Thug's
   pictures and attacks with the Hit man's, the Terrorist's attack uses his walk pictures). `-- --smoke-test`
   reports names that don't resolve. To list the record names:
   `python -c "L=open('Chicago.tile',encoding='cp1251').read().splitlines(); n=int(L[0]); t=L[n+2:n+2+int(L[n+1])]; [print(i, r.split(';',1)[1].strip()) for i,r in enumerate(t) if ';' in r]"`
   (run in the original game folder).
2. **How fast** — `game.cfg`, section `[animation]`: `idle_fps`, `move_fps`, `attack_fps`, `killed_fps` for every
   unit, and `<Type>.<kind>_fps` for one type (`Thompson.attack_fps=8`). Default 5 frames per second.
   `attack_hold_minutes` sets how long a unit keeps its attack picture between an order's hits (extortion);
   one game hour is one real second.
3. **Where** — the picture's offset from the unit's cell is the `Animation` node position in `Units/Unit.tscn`
   (0, −14). The facing (SE / NE / NW / SW) comes from the move or target direction on screen
   (`MapTileMath.GetDirection`). Death pictures play once and hold the last frame.

`game.cfg` and `units.json` are read when the game starts; restart it after a change.
