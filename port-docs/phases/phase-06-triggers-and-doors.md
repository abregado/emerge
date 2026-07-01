# Phase 6 — Trigger System & Doors

**Goal:** A complete mini-puzzle works: laser hits target → target sends a signal
through the trigger group → door opens. The `TriggerBus` autoload (stubbed in
Phase 1) is now fully wired. By the end of this phase you have a self-contained,
testable puzzle loop.

Reads from: `02-gameplay-systems.md §2`, `01-architecture.md §5`.

---

## What to build

### 6.1 Trigger interfaces

Create `bodies/interfaces/ITrigger.cs` and `bodies/interfaces/ITriggerable.cs`:

```csharp
public interface ITrigger
{
    string GroupId { get; }
    int TriggerWeight { get; }
    bool IsTriggered { get; }
}

// Matches TriggerBus.ITriggerable (already defined there)
// Re-export for convenience:
public interface ITriggerable : TriggerBus.ITriggerable { }
```

### 6.2 Wire `LaserTarget` into `TriggerBus`

Update `LaserTarget.cs` to implement `ITrigger` and register with `TriggerBus`:

```csharp
public partial class LaserTarget : SurfaceBody, ILaserHandler, ITrigger
{
    [Export] public int RequiredIntensity = 1;
    [Export] public string GroupId { get; private set; } = "";
    public int TriggerWeight => IsTriggered ? 1 : 0;
    public bool IsTriggered { get; private set; }

    public override void AfterGridPopulated()
    {
        if (!string.IsNullOrEmpty(GroupId))
            TriggerBus.Instance.RegisterSource(GroupId, GetInstanceId().GetHashCode());
    }

    private void UpdateState()
    {
        bool hit = _beams.Count > 0;
        if (hit == IsTriggered) return;
        IsTriggered = hit;
        if (!string.IsNullOrEmpty(GroupId))
            TriggerBus.Instance.UpdateSource(GroupId, GetInstanceId().GetHashCode(), TriggerWeight);
    }
    // ... rest unchanged from Phase 4
}
```

The `GroupId` comes from the LDTK entity field `trigger_groups` (a comma-separated
string). Parse it in `LevelLoader` when instancing the entity and set it via
`[Export]` before `Init`.

### 6.3 `Door.cs`

Create `bodies/Door.cs` implementing `ITriggerable`:

```csharp
using Godot;
using System.Collections.Generic;

public partial class Door : SurfaceBody, ITriggerable
{
    [Export] public string[] TriggerGroups = Array.Empty<string>();
    [Export] public int TriggerWeightNeeded = 1;
    [Export] public bool ReverseState = false;  // normally-open door

    private CollisionShape3D _collision;
    private bool _isOpen;

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        _collision = GetNode<CollisionShape3D>("CollisionShape3D");
    }

    public override void AfterGridPopulated()
    {
        foreach (var g in TriggerGroups)
            if (!string.IsNullOrEmpty(g))
                TriggerBus.Instance.RegisterConsumer(g, this);
    }

    public void ReceiveSignal(int strength)
    {
        bool shouldOpen = (strength >= TriggerWeightNeeded) != ReverseState;
        if (shouldOpen == _isOpen) return;
        SetOpen(shouldOpen);
    }

    private void SetOpen(bool open)
    {
        _isOpen = open;
        _collision.Disabled = open;
        OccupiedTile.IsWalkable = open;

        // Visual: hide/show door mesh (or play AnimationPlayer in Phase 9)
        GetNodeOrNull<Node3D>("View").Visible = !open;

        EventBus.Instance.EmitSignal(EventBus.SignalName.TileChanged, OccupiedTile.X, OccupiedTile.Z);
    }
}
```

**`Door.tscn` structure:**
```
Door (Node3D)             ← Door.cs
├── CollisionShape3D      ← BoxShape3D 1×2×0.2, player/physics layer
├── View (Node3D)
│   └── MeshInstance3D   ← door placeholder box (hidden when open)
└── (AnimationPlayer added in Phase 9)
```

The door occupies a tile; when open, `IsWalkable = true` and the collision is
disabled so the player can walk through.

### 6.4 `SignalCable` (visual only — optional for this phase)

`SignalCable` in Unity draws a glowing path between trigger and door as a
visualiser. Defer the visual to Phase 9. For this phase, add a stub:

```csharp
public partial class SignalCable : Node3D, ITriggerable
{
    [Export] public string[] TriggerGroups = Array.Empty<string>();

    public override void _Ready()
    {
        // AfterGridPopulated will register us
    }

    // Called by LevelLoader
    public void AfterGridPopulated()
    {
        foreach (var g in TriggerGroups)
            TriggerBus.Instance.RegisterConsumer(g, this);
    }

    public void ReceiveSignal(int strength)
    {
        GD.Print($"Cable {Name}: signal={strength}");
        // Visual particle flow added in Phase 9
    }
}
```

### 6.5 `TriggerIntermediate` / `ChargedIntermediate`

In the real game, `TriggerIntermediate` is a relay — it's both a trigger source
and a consumer (a button that, when stepped on, sends signal forward). Add a
simple stub for now:

```csharp
public partial class TriggerIntermediate : SurfaceBody, ITrigger, ITriggerable
{
    [Export] public string InputGroup = "";
    [Export] public string OutputGroup = "";
    public string GroupId => OutputGroup;
    public int TriggerWeight => IsTriggered ? 1 : 0;
    public bool IsTriggered { get; private set; }

    public override void AfterGridPopulated()
    {
        if (!string.IsNullOrEmpty(InputGroup))
            TriggerBus.Instance.RegisterConsumer(InputGroup, this);
        if (!string.IsNullOrEmpty(OutputGroup))
            TriggerBus.Instance.RegisterSource(OutputGroup, GetInstanceId().GetHashCode());
    }

    public void ReceiveSignal(int strength)
    {
        bool triggered = strength > 0;
        if (triggered == IsTriggered) return;
        IsTriggered = triggered;
        TriggerBus.Instance.UpdateSource(OutputGroup, GetInstanceId().GetHashCode(), TriggerWeight);
    }
}
```

### 6.6 LDTK entity fields for triggers

Ensure the LDTK entity schema for `LaserTarget`, `Door`, and `SignalCable` has:
- `trigger_groups` — string (comma-separated group ids, e.g. `"A,B"`)
- `trigger_weight_needed` — int (Door only)
- `reverse_state` — bool (Door only)

Extend `LevelLoader` to parse these fields and set them on instanced bodies before
calling `Init`:

```csharp
// In LevelLoader.SpawnBodies, for each entity:
if (entity.Identifier == "LaserTarget")
{
    var target = scene.Instantiate<LaserTarget>();
    target.GroupId = entity.FieldInstances["trigger_groups"].Value.ToString();
    // ...
}
```

### 6.7 Full puzzle test level

Update the test level to include:
- `EmitterAlien` at (3, 5) facing east.
- `Reflector` (movable) at (8, 5).
- `LaserTarget` at (8, 10), `GroupId = "PuzzleA"`.
- `Door` at (12, 5), `TriggerGroups = ["PuzzleA"]`, `TriggerWeightNeeded = 1`.

The puzzle: player picks up reflector, orients it to route beam north to the
target, beam triggers → door opens → player walks through.

---

## Testing this phase

### Golden path — full puzzle

1. Load the test level. Observe the door is **closed** (mesh visible, tile not
   walkable).
2. Pick up the reflector, rotate if needed, drop in the beam path. Emitter on.
3. Beam hits the target. `triggered=True` prints. **Door opens** (mesh hides,
   player can walk through).
4. Remove the reflector from the beam path. Door **closes** again.

### Multiple targets — AND logic

5. Create a second `LaserTarget` in group `"PuzzleA"` (same group). Set
   `Door.TriggerWeightNeeded = 2`.
6. Both targets must be hit simultaneously for the door to open. Confirm: one
   target hit → door stays closed; both hit → door opens.

### `ReverseState` door

7. Set `Door.ReverseState = true`. Door should start **open** and close when
   triggered. Player can walk through initially, not after the laser hits.

### Tile walkability

8. Confirm that when the door opens, `OccupiedTile.IsWalkable == true` and the
   player can physically walk through (not blocked by collision). When closed,
   `IsWalkable == false` and the player is blocked.

### TriggerBus isolation

9. Add a second unrelated door in group `"PuzzleB"`. Confirm that triggering group
   `"PuzzleA"` does **not** affect `"PuzzleB"` door state.

### Signal cable stub

10. Add a `SignalCable` in group `"PuzzleA"`. Confirm `ReceiveSignal` is called
    with the correct strength when the laser hits/misses the target. (Visual output
    in Phase 9.)

### Regression

11. Phase 5 carry/rotate still works.
12. Phase 4 emitter toggle, beam drawing still works.
13. Phase 3 player movement, Phase 2 grid loading intact.
