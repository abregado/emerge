# Phase 2 — Grid & Level Loading

**Goal:** Load a single level's tile grid from LDTK, build the logical
`Grid<EmergeTile>` in memory, instance one or more placeholder body scenes at
the correct world positions, and run the three-pass `Init → ActivateOnGrid →
AfterGridPopulated` lifecycle. By the end of this phase you can see a top-down
camera looking at a flat grid with coloured tile markers and placeholder body
nodes at the right positions.

Reads from: `01-architecture.md §4,7`, `03-levels-and-data.md §3–4`,
`04-godot-port-plan.md §3 steps 2–3`.

---

## What to build

### 2.1 Port `Grid<T>` and `EmergeTile`

Create `grid/Grid.cs`. This is nearly verbatim from the Unity version — it's a
plain C# class with no engine dependency:

```csharp
using Godot;
using System;

public class Grid<T>
{
    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public Vector3 Origin { get; }

    private T[,] _cells;

    public Grid(int width, int height, float cellSize, Vector3 origin)
    {
        Width = width; Height = height; CellSize = cellSize; Origin = origin;
        _cells = new T[width, height];
    }

    public Vector3 GetWorldCenter(int x, int z) =>
        Origin + new Vector3((x + 0.5f) * CellSize, 0, (z + 0.5f) * CellSize);

    public (int x, int z) GetCell(Vector3 worldPos) =>
        ((int)MathF.Floor((worldPos.X - Origin.X) / CellSize),
         (int)MathF.Floor((worldPos.Z - Origin.Z) / CellSize));

    public bool InBounds(int x, int z) => x >= 0 && x < Width && z >= 0 && z < Height;

    public T Get(int x, int z) => InBounds(x, z) ? _cells[x, z] : default;
    public T Get(Vector3 worldPos) { var (x,z) = GetCell(worldPos); return Get(x, z); }
    public void Set(int x, int z, T value) { if (InBounds(x, z)) _cells[x, z] = value; }

    // Orthogonal neighbours (N/E/S/W)
    public T[] GetAdjacentOrthogonal(int x, int z) => new[]
    {
        Get(x, z+1), Get(x+1, z), Get(x, z-1), Get(x-1, z)
    };
}
```

Create `grid/EmergeTile.cs`:

```csharp
public class EmergeTile
{
    public int X { get; }
    public int Z { get; }
    public G.BakedTile BakedTile { get; private set; }
    public bool IsWalkable { get; private set; }
    public bool IsPlaceable { get; private set; }

    public Body FloorBody { get; private set; }
    public Body SurfaceBody { get; private set; }

    public EmergeTile(int x, int z) { X = x; Z = z; }

    public void SetBakedTile(G.BakedTile type)
    {
        BakedTile = type;
        IsWalkable = type is G.BakedTile.Sand or G.BakedTile.Placeable_Static
                          or G.BakedTile.Water or G.BakedTile.Walkable_Static
                          or G.BakedTile.Plateable_Static or G.BakedTile.RaycastStaticPlaceable
                          or G.BakedTile.Null;
        IsPlaceable = type is G.BakedTile.Placeable_Static or G.BakedTile.Plateable_Static
                           or G.BakedTile.RaycastStaticPlaceable;
        EventBus.Instance?.EmitSignal(EventBus.SignalName.TileChanged, X, Z);
    }

    public void SetSurfaceBody(Body b) { SurfaceBody = b; }
    public void SetFloorBody(Body b) { FloorBody = b; }
    public void ClearSurfaceBody() { SurfaceBody = null; }
    public void ClearFloorBody() { FloorBody = null; }
}
```

### 2.2 Body base classes

Create `bodies/Body.cs` — the abstract base for everything on the grid:

```csharp
using Godot;

public abstract partial class Body : EmergeNode
{
    public EmergeTile OccupiedTile { get; protected set; }
    public string PrefabId { get; set; }

    protected Grid<EmergeTile> Grid;

    // Four lifecycle passes called by LevelLoader (not _Ready)
    public abstract void Init(Grid<EmergeTile> grid);
    public abstract void ActivateOnGrid();
    public abstract void AfterGridPopulated();
    public abstract void RemoveBody();

    protected void PlaceBody()
    {
        var tile = Grid.Get(GlobalPosition);
        if (tile == null) { GD.PrintErr($"{Name}: no tile at {GlobalPosition}"); return; }
        GlobalPosition = Grid.GetWorldCenter(tile.X, tile.Z);
        OccupiedTile = tile;
        RegisterOnTile(tile);
    }

    protected virtual void RegisterOnTile(EmergeTile tile) =>
        tile.SetSurfaceBody(this);
}
```

Create `bodies/SurfaceBody.cs` and `bodies/FloorBody.cs` as thin subclasses for
now — both just `override Init/ActivateOnGrid/AfterGridPopulated/RemoveBody` with
empty bodies so concrete types can inherit and override only what they need.

### 2.3 Extend `tools/extract_levels.py` to emit LDTK

> **Prerequisite:** the extractor already runs (`python tools/extract_levels.py`
> from the repo root) and produces `tools/level_export/*.grid.json` and
> `*.bodies.json`. This step extends it.

Add a function `emit_ldtk(scenario, grid_json, bodies_json) -> dict` that writes a
minimal `.ldtk` JSON file:

- One level per scenario (start with **HubWorld** only — smallest map, 8 bodies).
- **IntGrid layer `Tiles`**: value 1 = floor/placeable (BakedTile 8/3/6), value 2 =
  wall/blocked (7/2), value 3 = sand (1), value 4 = water (4), 0 = outside.
- **Entity layer `Bodies`**: one entity per entry in `bodies.json`, at
  `cx = x`, `cy = (height-1) - z` (z-flip), with custom fields
  `rotation` (int 0/90/180/270) and `body_type` (string).
- Level custom field `world_offset_x` / `world_offset_z` from the grid's `offset`.

Output the `.ldtk` to `tools/level_export/emerge.ldtk`. (A single LDTK project
file with one level per scenario is the target; do only HubWorld now.)

The LDTK JSON format specification is at `https://ldtk.io/json/` — the key
structures are `LdtkJson → levels[] → layerInstances[]` with `intGridCsv` for
IntGrid layers and `entityInstances[]` for Entity layers.

### 2.4 LDTK importer addon

Install the **`ldtk-importer`** Godot addon (community; search the Asset Library for
"LDtk Importer"). Enable it in `Project → Plugins`. Place the `.ldtk` file under
`res://levels/emerge.ldtk`. Confirm it imports without errors.

> Alternatively, skip the addon and write a minimal JSON parser — the `.ldtk` format
> is just JSON. Either path is valid; the addon is faster.

### 2.5 Level loader — `LevelLoader.cs`

Create `scenes/LevelLoader.cs` as a child of `World` in the main scene. Its job is
to reproduce the Unity `LevelManager.Init` sequence:

```
1. LoadGrid(scenarioName)          → build Grid<EmergeTile> from LDTK IntGrid layer
2. SpawnBodies(scenarioName)       → instance body scenes from LDTK Entities layer
3. Pass 1: foreach body → body.Init(grid)
4. Pass 2: foreach body → body.ActivateOnGrid()
5. Pass 3: foreach body → body.AfterGridPopulated()
```

The LDTK → Grid mapping:
- `cx = x`, `cy = (height-1) - z` (inverse of what the emitter wrote)
- IntGrid value 1 → `BakedTile.RaycastStaticPlaceable`; 2 → `RaycastStaticBlock`;
  3 → `Sand`; 4 → `Water`; 0 → `Null`.

The LDTK → world position mapping:
- `world_x = offset_x + (x + 0.5) * cellSize`
- `world_z = offset_z + (z + 0.5) * cellSize`
  where `x,z` are the logical grid coords (not the LDTK `cx,cy`).

```csharp
public partial class LevelLoader : Node3D
{
    [Export] private NodePath LevelRoot;
    [Export] private Res Res;

    public Grid<EmergeTile> Grid { get; private set; }
    private List<Body> _allBodies = new();

    public void LoadLevel(string scenarioName)
    {
        // 1. parse LDTK, build grid
        // 2. spawn bodies under LevelRoot
        // 3. run the three passes
        RunPasses();
    }

    private void RunPasses()
    {
        foreach (var b in _allBodies) b.Init(Grid);
        foreach (var b in _allBodies) b.ActivateOnGrid();
        foreach (var b in _allBodies) b.AfterGridPopulated();
    }
}
```

For now, instantiate a **placeholder** `PackedScene` for every entity regardless of
type — a simple `MeshInstance3D` box scaled to 0.8 units. Real body scenes come in
later phases.

### 2.6 Tile visualiser (debug only)

Add a `TileDebugVisualiser.cs` node (child of `LevelRoot`) that, after the grid is
built, creates a flat `MeshInstance3D` quad per tile coloured by `BakedTile` type:
- Placeable → grey
- Wall → dark red
- Sand → tan
- Water → blue

This is debug scaffolding — delete it in Phase 9 when real art replaces it.

---

## Testing this phase

### Manual smoke test (what you see)

1. Call `LevelLoader.LoadLevel("HubWorld")` from `Main._Ready`.
2. Run the project. You should see:
   - A **flat grid of coloured quads** matching the HubWorld ASCII map from
     `tools/level_export/HubWorld.map.txt` — walls are dark red, floor is grey.
   - **8 placeholder boxes** (the HubWorld bodies) positioned correctly over floor
     tiles.
   - No boxes sitting on wall tiles.

### Structural checks

3. **Init order** — add `GD.Print` to the start of each pass in `RunPasses()` and
   to each body's `Init`, `ActivateOnGrid`, `AfterGridPopulated`. Confirm the output
   log shows all `Init` calls before any `ActivateOnGrid`, and all `ActivateOnGrid`
   before any `AfterGridPopulated`.

4. **Tile occupancy** — after the three passes, assert in `LevelLoader`:
   ```csharp
   foreach (var b in _allBodies)
       Debug.Assert(b.OccupiedTile != null, $"{b.Name} has no tile after ActivateOnGrid");
   ```
   Zero assertion failures expected.

5. **Grid bounds** — assert `grid.Width == 35` and `grid.Height == 22` for HubWorld
   (from the extractor output).

6. **Z-flip correctness** — compare the debug quad grid visually with
   `HubWorld.map.txt`. The map prints high-Z first (top of the text = north/high Z).
   Confirm the wall pattern matches. Any mismatch means the z-flip is inverted
   somewhere.

### Regression

7. **Phase 1 autoloads still present** — confirm `EventBus`, `TriggerBus`,
   `TimeManager` nodes exist under `/root/` in Remote inspector. The tick signal
   should still fire (add the temporary print from Phase 1 test 3 if needed).
