# 03 — Levels, Data & the LDTK Migration

This answers: **how is a level actually stored, what could I extract, and how do
we move to LDTK?**

---

## 1. What a "level" is made of

A playable level (= a **scenario**, `G.Scenarios`) is **three things glued
together**:

1. **A Unity scene** (`Assets/Scenes/Application/NN_Name.unity`) — contains the
   camera rig, lights, managers, *all the décor meshes*, **and the gameplay
   bodies** (machines/devices/doors/cables/zones) authored as GameObjects under
   `surfaceBodyParent` / `editorBodyParent`.
2. **A baked tile grid** (`Assets/StreamingAssets/<Scenario>/grid_data.json`) —
   the logical playfield: for every cell, its `BakedTile` type
   (wall/floor/water/…) and the derived walkable/placeable flags. **This is loaded
   at runtime by `LevelManager.LoadStaticEntities`** and is authoritative for tile
   logic.
3. **(Legacy) a body export** (`Assets/StreamingAssets/<Scenario>/round_0.json`) —
   an older serialization of the bodies (type/position/rotation). **The live game
   no longer loads this** (`LevelManager.LoadSaveData` is commented out); bodies
   come from the scene now. But it's still a useful machine-readable snapshot of
   the puzzle layout.

> **The crucial takeaway for the port:** the *décor* lives in giant `.unity`
> files (see §4) but the *gameplay-relevant* level data is small and clean:
> `grid_data.json` (always current) + `round_0.json` (legacy but informative).
> Those two are what you migrate to LDTK — **not** the 90 MB scenes.

### How the grid is authored (so you trust it)

In the editor, `LevelEditor.AnalyseStaticGrid` (`Level/LevelEditor.cs`) builds the
grid by **raycasting straight down** over each cell onto terrain colliders:
- hit `BuildableTerrain` near y≈0 → `Sand` (or `RaycastStaticBlock` otherwise),
- hit `PlaceableTerrain` near y≈0 → `RaycastStaticPlaceable`,
- nothing/too-high → `RaycastStaticBlock` (wall).
Then it overlays explicit `BakedTile` marker objects (Block/Water/Placeable/…).
`SaveGrid()` writes it to `grid_data.json` via ES3. So the grid is a **baked
top-down footprint** of the 3D terrain — exactly the thing LDTK wants.

### The serialization format (ES3 / Easy Save 3)

Both JSON files are written by **Easy Save 3**. The shape is:

```json
{ "StaticGridData": { "__type": "StaticGridData,Assembly-CSharp",
  "value": { "_width":64, "_height":57, "_cellSize":1,
             "_offset":{"x":-46,"y":0,"z":-28},
             "_gridArray":[ [ {tile}, {tile}, ... ], ... ] } } }
```

`_gridArray` is indexed **`[x][z]`**; each tile has `x,z,bakedTile,isWalkable,
isPlaceable,roomType,floorBody,surfaceBody`. In the shipped files `floorBody`/
`surfaceBody` are always `null` (bodies aren't baked into the grid). `round_0.json`
uses the same wrapper with root key `DynamicData` → `surfaceBodies[]` of
`{type:int, position, rotation}`, where `type` is the `G.SurfaceBody` enum value.

---

## 2. What I extracted (it works)

I wrote **`tools/extract_levels.py`** and ran it. For every scenario it produces,
under `tools/level_export/`:

- **`<Scenario>.map.txt`** — an ASCII top-down preview (eyeball check),
- **`<Scenario>.grid.json`** — a clean, engine-agnostic
  `{width,height,offset,cell,tiles[z][x]}` integer grid,
- **`<Scenario>.bodies.json`** — decoded bodies (`type` name, grid `x,z`, `rotY`),
  where a `round_0.json` exists.

Coverage (15 scenarios):

```
Accelerator      41x66   bodies=51     LongMachine      50x69   bodies=73
BuildingScene_1  64x57   bodies=0      MachineTestScene 40x40   bodies=0
Credits           1x1    bodies=0      MessScene        64x57   bodies=54
DesertEntrance   64x57   bodies=7      TestScene_1     105x87   bodies=0
EasyAmp          68x66   bodies=71     Vaults          200x200  bodies=40
Entry            81x57   bodies=53     EmptyScene       79x75   bodies=0
FirstMachine     79x75   bodies=84     HubWorld         35x22   bodies=8
                                       LastChoice       62x88   bodies=0
```

Example — `tools/level_export/HubWorld.map.txt`:

```
###################################
###########...####...##############
########......####......###########
########......####......###########
########......####......###########
########......####......###########
########......####......###########
######....................#########
######....................#########
######....................#########
######.........##.........#########
######.........##.........#########
######....................#########
########..................#########
########................###########
########...###....###...###########
##############....#################
##############.##.#################
##############.##.#################
###################################
   (#=wall/blocked  .=floor/placeable)
```

Example — decoded body (`FirstMachine.bodies.json`), type counts:
`Door:18, LaserTarget:20, ChargedIntermediate:12, TriggerIntermediate:11,
Crystal_Door_Dropper:10, KeyDeviceDetector:5, PyramidOrb:2, ItemCrystal:2,
SecurityDoor:2, FlatOrb:1, CubeOrb:1`.

**Conclusion:** the tile grid extracts perfectly and is directly usable. The body
list extracts cleanly too, **but** because `round_0.json` is legacy, treat
body positions/types as a strong starting point to verify against each scene
rather than gospel (some scenarios have no `round_0.json` at all — their bodies
exist only in the `.unity` scene).

---

## 3. Recommended LDTK structure

LDTK is a 2D, layer-based, grid editor — a clean fit for this game's logical
playfield. Suggested setup:

- **One LDTK project**, **one Level per scenario** (sized `width×height` from the
  grid, `gridSize` = 1 unit, e.g. 16px). Store the world `offset` (x,z) as a
  level field so you can map back to 3D world coords if you keep 3D.
- **Layer 1 — `Tiles` (IntGrid):** one int value per `BakedTile` you care about.
  Minimal useful set: `1=Floor/Placeable` (was 8/3/6), `2=Wall` (was 7/2),
  `3=Sand` (1), `4=Water` (4), `5=WalkableStatic` (5). An IntGrid value of 0 =
  empty/outside. The extractor's `tiles[z][x]` array maps straight onto this.
- **Layer 2 — `Entities`:** one LDTK Entity per body **type** (Door, LaserTarget,
  Reflector, EmitterAlien, Splitter, AmpAlien, ItemOrb, Cable, …), with fields:
  `rotation` (0/90/180/270), `triggerGroups` (string list), and type-specific
  fields (e.g. Door.`triggerWeightNeeded`/`reverseState`, Emitter.`startsActive`/
  `repaired`). Place them at the grid `x,z` from `bodies.json`.
- **Layer 3 — `Markers` (optional):** cutscene destinations
  (`ActorGotoDestination`), camera anchors, spawn/elevator points, trigger zones —
  things currently authored as named GameObjects.

### Axis mapping (important)

The grid is `[x][z]` with z increasing "north/up". LDTK's grid is `[cy][cx]` with
**cy increasing downward**. So when writing LDTK: `cx = x`,
`cy = (height-1) - z` (flip), matching the ASCII maps (which already print
high-z first). Keep this flip in **one** place.

---

## 4. Two ways to get the data into LDTK

**Option A — from the clean JSON (recommended, mostly done).**
You already have `tools/level_export/*.grid.json` + `*.bodies.json`. Extend
`tools/extract_levels.py` to also emit an `.ldtk`/`.ldtkl` (LDTK's JSON) directly,
or write the IntGrid CSV + an entities list and import. This needs **no Unity**.
Gap: bodies for scenarios lacking `round_0.json`, and any drift between
`round_0.json` and the live scene — fill those by Option B for the affected
scenes, or by hand (they're small).

**Option B — straight from the `.unity` scenes (authoritative, heavier).**
The scenes are **47–91 MB YAML with ~130k GameObjects each** — too big to open
casually, and most of it is décor. But the gameplay bodies are identifiable by
their script (`MonoBehaviour` referencing e.g. the `Door`/`Reflector` script
GUID) and their `Transform` position. A script could:
1. read each body script's `.cs.meta` **GUID**,
2. stream-parse the scene YAML, collect `Transform` + `MonoBehaviour` records,
3. for transforms whose GameObject has a gameplay-body MonoBehaviour, emit
   `{type, worldPos, rotation, serialized fields}`.
This is the *authoritative* body layout (matches what actually ships) but is real
work. **Recommendation:** do Option A now (grid is perfect, bodies are 90% there),
and only fall back to Option B for scenes where `round_0.json` is missing/stale.

> If you'd like, I can extend the extractor to (a) emit `.ldtk` directly from the
> clean JSON, and/or (b) add a scene-YAML body ripper for the authoritative pass.

---

## 5. Persistence / save data (for completeness)

- **`IOManager`** (`Manager/IOManager.cs`) wraps ES3. `GetSaveFolder(scenario,
  isRuntime)` points at `StreamingAssets` (read-only, shipped) vs
  `persistentDataPath` (writable, runtime). On first run it copies scenario data
  from streaming→persistent.
- **`ProgressManager`** (`Manager/ProgressManager.cs`) persists **story progress
  only**: which Ink variables are set and visit counts, plus last scenario
  (`ProgressData` → `story/story_progress_0.json`). There is currently **no
  mid-level state save** — the per-body `SaveData`/`LoadSaveData` paths are
  commented out. So in the port, "save" = story flags + which level you're on.

In Godot: `FileAccess` + `JSON` to `user://`, or typed `Resource` save files, with
a small `SaveManager` autoload mirroring `ProgressManager`.
