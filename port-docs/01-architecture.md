# 01 — Core Architecture

This is the skeleton: the object model, the load/lifecycle order, the grid, and
the two event systems. Get this and the rest of the codebase makes sense.

---

## 1. The base classes

Two tiny base classes that almost everything inherits from:

- **`EmergeBase`** (`Interfaces/EmergeBase.cs`) — a plain (non-Unity) class.
  Just provides log helpers gated by a `G.LogLevel`. Used by pure-logic classes
  like `Grid<T>` and `EventManager`.
- **`EmergeMonoBase`** (`Interfaces/EmergeMonoBase.cs`) — extends Unity's
  `MonoBehaviour`. Adds:
  - a reference to the owning **`EmergeSceneManager`** (`_manager`),
  - a `virtual Init(EmergeSceneManager manager)` entry point,
  - the same log helpers.

> **Key idea:** the game does **not** rely on Unity's `Awake/Start` ordering for
> its own wiring. Instead, almost every component implements `Init(manager)` and
> the managers call those `Init`s **explicitly, in a controlled order**. When you
> port this, reproduce the *explicit init order*, don't rely on Godot's `_ready`
> order. (In Godot, `_ready` fires children-before-parents, which is the wrong
> way round for this design.)

---

## 2. The manager hierarchy

**`EmergeSceneManager`** (`Manager/EmergeSceneManager.cs`) is the root of a live
scene. In `Awake()` it finds the other managers via `FindObjectOfType`, and in
`Start()` it initialises them **in this order**:

```
res.Init()                 // ScriptableObject: prefab/scene/item registry
playerManager.Init(this)
cameraDirector.Init(this)
timeManager.Init(this)
levelManager.Init(this)    // <-- this one then inits the whole level (below)
```

The managers it owns (each a `MonoBehaviour` found in the scene):

| Manager | Responsibility |
|---|---|
| `Res` (ScriptableObject, not a node) | Registry: prefab lookups by id, scene names per scenario, item data, laser presets, materials. The game's "asset database". |
| `LevelManager` | Loads the grid, inits all bodies, runs the round phase machine. The heart. |
| `PlayerManager` | Spawns players, switches UI/in-game input modes, attaches to elevators. |
| `CameraDirector` | Wraps Cinemachine target groups. |
| `TimeManager` | The 0.2s tick scheduler (see §6). |
| `AudioDirector` | Plays named SFX/clips. |
| `EventManager` | A plain-C# event bus (see §5). Created with `new`, not found. |

`LevelManager` in turn finds-and-holds a second tier of sub-managers it inits:
`ElevatorHandler`, `HighlightHandler`, `DialogManager`, `LightingController`,
`ProgressManager`, `TriggerHandler`.

---

## 3. The `Body` model — everything on the grid

Class hierarchy (`Bodies/BaseClasses/`):

```
EmergeMonoBase
└── Body (abstract)                  // has a View child, an occupied tile, prefabId
    ├── FloorBody                    // sits "under" things: sockets, foundations
    └── SurfaceBody (abstract)       // occupies a tile's surface
        └── Machine (abstract)       // interactable; has Idle/Hovering/Working states
            ├── EmitterAlien         // laser source
            ├── Reflector / Splitter / Merger / AmpAlien ...  (Devices)
            ├── SingleItemPedestal, ItemDispenserTimed ...    (Machines)
            └── ...
        └── Door, LaserTarget, ItemOrb, TriggerIntermediate,
            ChargedIntermediate, EndLevelButton, SecurityDoor ...
```

Every `Body` defines four lifecycle methods that the `LevelManager` calls in
**four separate passes over the whole level** (see §4):

```csharp
abstract void Init(manager)        // cache refs, build sub-objects
abstract void ActivateOnGrid()     // = PlaceBody(): snap to tile, register on grid
abstract void AfterGridPopulated() // now that ALL bodies exist, wire cross-refs
abstract void RemoveBody()         // detach from its tile
abstract void PlaceBody()          // compute occupied tile, write self into it
```

`PlaceBody()` is the important one: it asks the grid for the tile at its world
position, snaps the transform to that tile's centre, and stores itself in the
tile (`tile.SetSurfaceBody(this)` / `SetFloorBody`). So **the grid is the source
of truth for "what is on tile (x,z)"**, kept in sync with world transforms.

A `Body` also has:
- **`view`** — a child `Transform` named `"View"` holding the visuals (so logic
  and presentation are separated; rotation/hover animations move `view`, not the
  logical body).
- **`prefabId`** — a string id used by `Res` to look the prefab back up.
- **`occupiedTile`** — the `EmergeTile` it currently sits on.

### `Machine` state machine

`Machine` (`Bodies/BaseClasses/Machine.cs`) adds a 3-state machine —
**Idle / Hovering / Working** — with `Begin*State`/`End*State` hooks dispatched
through dictionaries. Player interaction (`OnInteractButton`, `OnCancelButton`,
…) flows through here. **Hovering** = the player has lifted the machine to carry
or reposition it (see `Hoverable` in `02`). **Working** = it's doing its job
(e.g. emitting a laser).

---

## 4. Level load order (the most important sequence to replicate)

`LevelManager.Init()` → `OnLoadLevelData("round_0")` does:

```
1. LoadStaticEntities("grid_data")      // build the tile grid from JSON
2. progressManager.LoadProgressDataFromFile(0)   // story flags (if loadProgress)
3. InitializeEditorEntities()           // PASS 1: Init() every body & editor entity
4. PopulateGrid()                       // PASS 2: ActivateOnGrid() -> bodies claim tiles
5. AfterGridPopulated()                 // PASS 3: AfterGridPopulated() cross-wiring
6. OnPreRoundStart()                    // start the round phase machine
```

Each of passes 1–3 iterates the same three sources:
- all `FloorBody` objects (`FindObjectsOfType<FloorBody>`),
- children of **`surfaceBodyParent`** (the surface bodies),
- children of **`editorBodyParent`** (editor entities: cables, zones, spawners…).

> Bodies are **authored directly in the Unity scene** as GameObjects parented
> under `surfaceBodyParent` / `editorBodyParent`. The grid JSON only carries the
> *tile layout*, not the bodies. (There is a legacy code path,
> `LoadSaveData`/`SpawnSurfaceBody`, that instantiated bodies from `round_0.json`
> — it's **commented out**. See `03-levels-and-data.md`.)

### Round phase machine

After load, `LevelManager` runs a small phase enum:
`Init → PreRoundStart → RoundStart → Play → PreRoundEnd → RoundEnd`. Elevators
animate players in, control is handed over at `RoundStart`, and `EndLevelButton`
/ `ExitToScenario` drives the exit (elevators out → load next scene). Scene
changes go through `EmergeSceneManager.LoadSceneByEnum` using `Res` to map a
`G.Scenarios` enum to a scene name.

---

## 5. The two event systems (decoupling)

**A. `EventManager`** (`Manager/EventManager.cs`) — a plain object holding C#
`event` delegates. UI and systems subscribe; gameplay calls `TriggerOnX(...)`.
Examples: `OnTickUpdateUI`, `OnTileChanged` (static), `OnBodyPlaced` (static),
`OnPlayerActivityChanged`, `OnRequestHighlightUpdate`, `OnRoundStartEvent`.

> Note some events are **`static`** (`OnTileChanged`, `OnBodyPlaced`) so
> `EmergeTile` can fire them without a manager reference.

**B. `TriggerHandler`** (`Manager/TriggerHandler.cs`) — the **puzzle signal
bus** (see `02-gameplay-systems.md §2`). Distinct from `EventManager`: this is
gameplay wiring (buttons→doors), not engine events.

Both map cleanly to **Godot signals + an autoload event-bus**.

---

## 6. The tick scheduler

`TimeManager` (`Manager/TimeManager.cs`) accumulates `Time.deltaTime` and fires a
**tick every `G.TICK_TIME` = 0.2s**. Other systems schedule delayed callbacks via
`Schedule(new Occurrence(callback, durationSeconds, senderId))`; occurrences are
kept in a sorted list and fired when their due tick arrives. Used for things like
"start the round N seconds after the elevator animation", item respawn timers,
etc. Cancellable per-sender (`CancelAllOccurrencesForId`).

In Godot this is either a single autoload with a `_process` accumulator, or just
`SceneTreeTimer`/`Timer` nodes + `await` — see the port plan.

---

## 7. The grid

`Grid<TGridObject>` (`Level/Grid/Grid.cs`) is a generic 2D array helper, here
specialised as `Grid<EmergeTile>`. Important facts:

- It's a **3D-world grid on the X/Z plane** (Y is up). `cellSize` is usually 1.
  World→cell is `floor((world - origin)/cellSize)`; cell→world-centre adds a half-
  cell offset. **This matters for the port:** Godot 2D uses Y-down; if you port to
  3D you keep X/Z, if you port to 2D you map Z→Y and flip sign.
- Helpers: `GetGridObject(worldPos | x,z)`, `GetAdjacentOrthogonal`,
  `GetAdjacentRing` (8-neighbour), `GetTilesInLineBetween`, `GetInsideTilesArea`,
  `GetWorldPositionCenter`. Directions use `G.GridDir` (N/S/E/W + diagonals).

### `EmergeTile`

`EmergeTile` (`Level/Grid/EmergeTile.cs`) is one cell. It stores:

- flags: `isWalkable`, `isPlaceable`, `isInteractable`,
- `bakedTile` (a `G.BakedTile` enum — the static type, see below),
- `roomType` (`Desert/Cave/Room`),
- references to the `floorBody` and `surfaceBody` currently on it,
- its `x,z` coords, and an `hasIssue` flag for editor validation.

`SetBakedTile(type)` derives walkable/placeable from the tile type. Whenever a
tile changes it fires the static `EventManager.OnTileChanged` so visualisers/UI
update.

**`G.BakedTile` values** (from `Utils/G.cs`):

```
Null=0  Sand=1  Block=2  Placeable_Static=3  Water=4  Walkable_static=5
Plateable_Static=6  RaycastStaticBlock=7  RaycastStaticPlaceable=8
```

Walkable = Null/Placeable_Static/Sand/Water/Walkable_static/Plateable_Static.
Placeable = Placeable_Static/Plateable_Static/RaycastStaticPlaceable.
(In practice most shipped grids are just type **7 = wall/blocked** and **8 =
floor/placeable**, with some Sand/Water — see the extracted maps.)

---

## 8. `G.cs` — the constants & enums file

`Utils/G.cs` is the single global enum/constant bag. Worth skimming once; it
defines `Scenarios` (the level list), `BakedTile`, `SurfaceBody`, `Machine`,
`Devices`, `ItemType`, `GridDir`, `LaserDir`, `DialogTheme`, player/log enums,
and constants like tick time, player speeds, file names
(`STATIC_GRID_FILE_NAME = "grid_data"`). In Godot this becomes a couple of
`enum`s in an autoload or a `const`-holding script.
