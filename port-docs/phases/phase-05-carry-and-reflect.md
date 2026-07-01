# Phase 5 — Carry & Reflect: Hoverable, Reflector, and the Core Verb

**Goal:** The player can pick up a `Reflector` (or any hoverable device), carry
it around the grid, drop it on a valid tile, and rotate it 90° while carrying.
When a `Reflector` is placed in a live laser beam path, the beam bends correctly.
This is the **primary verb** of the game — it must feel good.

Reads from: `02-gameplay-systems.md §2,3`, `04-godot-port-plan.md §3 step 6`.

---

## What to build

### 5.1 `Hoverable.cs` component

Create `bodies/components/Hoverable.cs`. This is a component attached to a
`Machine` that grants it liftability:

```csharp
using Godot;

public partial class Hoverable : Node
{
    private Machine _owner;
    private Player _porter;
    private float _bobTime;
    private const float BobSpeed = 2f;
    private const float BobHeight = 0.15f;
    private const float CarryHeight = 1.0f; // world units above grid

    public bool IsBeingCarried => _porter != null;

    public override void _Ready() => _owner = GetParent<Machine>();

    public void BeginHover(Player porter)
    {
        _porter = porter;
        _owner.RemoveBody();  // detach from tile
        _bobTime = 0f;
    }

    public void EndHover(EmergeTile targetTile)
    {
        _porter = null;
        _owner.GlobalPosition = _owner.Grid.GetWorldCenter(targetTile.X, targetTile.Z);
        _owner.PlaceBody();   // re-register on new tile
        // Recompute any beams that pass through this tile or originate here
        RecomputeNearbyBeams();
    }

    public void Rotate90()
    {
        _owner.RotateY(Mathf.Pi / 2f);
        // If it's a reflector, re-derive its reflection points
        if (_owner is IRotatable r) r.OnRotated();
        RecomputeNearbyBeams();
    }

    public override void _Process(double delta)
    {
        if (_porter == null) return;

        // Follow the porter in X/Z
        var porterPos = _porter.GlobalPosition;
        _owner.GlobalPosition = new Vector3(porterPos.X, CarryHeight, porterPos.Z);

        // Bob the view child up/down
        _bobTime += (float)delta * BobSpeed;
        var view = _owner.GetNodeOrNull<Node3D>("View");
        if (view != null)
            view.Position = new Vector3(0, Mathf.Sin(_bobTime) * BobHeight, 0);
    }

    private void RecomputeNearbyBeams()
    {
        // Find all active LaserBeam nodes and tell them to recompute
        foreach (var beam in _owner.GetTree().GetNodesInGroup("active_beams"))
            (beam as LaserBeam)?.Recompute();
    }
}
```

Add `LaserBeam` to the `"active_beams"` group on `Activate` and remove on
`Deactivate`:

```csharp
// In LaserBeam.Activate:
AddToGroup("active_beams");
// In LaserBeam.Deactivate:
RemoveFromGroup("active_beams");
```

### 5.2 Update `Machine.OnInteract` to support hovering

In `Machine.cs`, refine the interact handler:

```csharp
public override void OnInteract(Player player)
{
    var hoverable = GetNodeOrNull<Hoverable>("Hoverable");
    if (hoverable == null) return;

    if (State == MachineState.Idle && !hoverable.IsBeingCarried)
    {
        player.BeginCarry(this, hoverable);
        SwitchState(MachineState.Hovering);
    }
    else if (State == MachineState.Hovering && hoverable.IsBeingCarried)
    {
        // Try to place on the tile under the player
        var targetTile = Grid.Get(player.GlobalPosition);
        if (targetTile != null && targetTile.IsPlaceable && targetTile.SurfaceBody == null)
        {
            player.EndCarry();
            hoverable.EndHover(targetTile);
            SwitchState(MachineState.Idle);
        }
    }
}

public override void OnCancel(Player player)
{
    var hoverable = GetNodeOrNull<Hoverable>("Hoverable");
    if (State == MachineState.Hovering && hoverable != null)
        hoverable.Rotate90();
}
```

### 5.3 Player carry state

Add carry tracking to `Player.cs`:

```csharp
private Machine _carriedMachine;
private Hoverable _carriedHoverable;

public void BeginCarry(Machine machine, Hoverable hoverable)
{
    _carriedMachine = machine;
    _carriedHoverable = hoverable;
    hoverable.BeginHover(this);
}

public void EndCarry()
{
    _carriedMachine = null;
    _carriedHoverable = null;
}
```

Update the interact/cancel input block in `_PhysicsProcess`:

```csharp
if (Input.IsActionJustPressed("cancel") && _carriedHoverable != null)
{
    _carriedHoverable.Rotate90();
}
else if (Input.IsActionJustPressed("interact"))
{
    if (_carriedHoverable != null)
    {
        // Try to drop on current tile
        _carriedMachine.OnInteract(this);
    }
    else
    {
        foreach (var body in _interactProbe.GetOverlappingBodies())
            if (body is Machine m) { m.OnInteract(this); break; }
    }
}
```

### 5.4 `IRotatable` interface

Create `bodies/interfaces/IRotatable.cs`:

```csharp
public interface IRotatable
{
    void OnRotated();
}
```

### 5.5 `ReflectionPoint` data class

Create `bodies/components/ReflectionPoint.cs`:

```csharp
using Godot;

// One face of a reflector: maps an incoming laser direction to an outgoing one.
public partial class ReflectionPoint : Node3D
{
    [Export] public G.LaserDir InputDir;
    [Export] public G.LaserDir OutputDir;

    private LaserBeam _outBeam;

    public void Init()
    {
        _outBeam = GD.Load<PackedScene>("res://scenes/laser/LaserBeam.tscn")
                      .Instantiate<LaserBeam>();
        AddChild(_outBeam);
        _outBeam.Deactivate();
    }

    public void ActivateOutput(int intensity, Vector3 origin)
    {
        var dir = LaserDirToVector(OutputDir);
        _outBeam.Activate(intensity, origin, dir);
    }

    public void DeactivateOutput() => _outBeam.Deactivate();

    public static Vector3 LaserDirToVector(G.LaserDir d) => d switch
    {
        G.LaserDir.N => new Vector3(0, 0, 1),
        G.LaserDir.E => new Vector3(1, 0, 0),
        G.LaserDir.S => new Vector3(0, 0, -1),
        G.LaserDir.W => new Vector3(-1, 0, 0),
        _ => Vector3.Zero
    };
}
```

### 5.6 `Reflector.cs`

A `Machine` + `ILaserHandler` + `IRotatable` with `ReflectionPoint` children:

```csharp
using Godot;
using System.Collections.Generic;

public partial class Reflector : Machine, ILaserHandler, IRotatable
{
    public bool IsLaserInteractable => true;
    private List<ReflectionPoint> _points = new();
    private readonly Dictionary<LaserBeam, ReflectionPoint> _activePoints = new();

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        foreach (Node child in GetChildren())
            if (child is ReflectionPoint rp) { rp.Init(); _points.Add(rp); }
    }

    public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
    {
        var inDir = WorldDirToLaserDir(-beam.Direction);
        var point = _points.Find(p => p.InputDir == inDir);
        if (point == null) return false;

        _activePoints[beam] = point;
        point.ActivateOutput(beam.Intensity, GlobalPosition + Vector3.Up * 0.5f);
        return true;
    }

    public void RemoveBeam(LaserBeam beam)
    {
        if (_activePoints.TryGetValue(beam, out var p))
        {
            p.DeactivateOutput();
            _activePoints.Remove(beam);
        }
    }

    public Vector3 GetEndPoint(Vector3 hitPoint) => hitPoint;

    public void OnRotated()
    {
        // Re-derive all active beam connections
        var beams = new List<LaserBeam>(_activePoints.Keys);
        foreach (var b in beams) { RemoveBeam(b); b.Recompute(); }
    }

    private G.LaserDir WorldDirToLaserDir(Vector3 d)
    {
        // Dominant axis on X/Z
        if (Mathf.Abs(d.Z) > Mathf.Abs(d.X))
            return d.Z > 0 ? G.LaserDir.N : G.LaserDir.S;
        return d.X > 0 ? G.LaserDir.E : G.LaserDir.W;
    }
}
```

**`Reflector.tscn` structure:**
```
Reflector (Node3D)              ← Reflector.cs
├── CollisionShape3D            ← BoxShape3D 0.9×0.9×0.9, laser layer
├── Hoverable                   ← Hoverable.cs
├── View (Node3D)               ← mesh goes here
│   └── MeshInstance3D          ← reflector placeholder box
└── ReflectionPoint_NE (Node3D) ← InputDir=N, OutputDir=E  (maps: beam from south → exits east)
    (add more ReflectionPoints for a diagonal reflector)
```

A basic reflector has two `ReflectionPoint` children — one mapping `S→E` (a beam
coming from the south exits east) and one mapping `W→N` (from the west exits
north), reflecting around the NE/SW diagonal. Rotate the whole node 90° to get
NW/SE variants.

### 5.7 Test level update

Extend the test level (from Phase 4):
- Add a `Reflector` between the emitter and the target, offset to the side.
- Emitter fires east, reflector at (8, 5) turns it north, target at (8, 10).
- The beam should reach the target only when the reflector is correctly rotated.

---

## Testing this phase

### Golden path — carry and drop

1. Walk up to the `Reflector`. Press interact. The reflector **lifts off the
   ground** (~1 unit up) and **bobs** gently.
2. Walk to a different placeable tile. Press interact. The reflector **snaps down**
   onto the new tile.
3. Verify the reflector's new tile in `Grid.Get(reflector.GlobalPosition)` is
   correct (add a `GD.Print` in `Hoverable.EndHover`).

### Golden path — rotate

4. Pick up the reflector. Press cancel. The reflector rotates 90°. Press cancel
   three more times — it should complete a full 360° back to the original
   orientation.
5. Drop it. Confirm the `ReflectionPoint.InputDir`/`OutputDir` values (printed in
   `OnRotated`) match the visual rotation.

### Golden path — beam routing

6. Place the reflector in the beam path (as in the test level). Turn the emitter
   on. The beam bends at the reflector and continues in the reflected direction,
   hitting the target.
7. Pick up the reflector while the emitter is on. The incoming beam must
   **immediately extend to infinity** (or hit a wall) when the reflector is
   removed. The target un-triggers.
8. Drop the reflector back. Beam re-routes, target triggers again.

### Edge cases

9. **Invalid drop** — carry a reflector and try to drop it on a wall tile or a
   tile already occupied by another body. The drop must be **rejected** (no state
   change, machine stays hovering).
10. **Tile vacated on pick-up** — confirm that after picking up the reflector,
    `OccupiedTile.SurfaceBody` is `null` (the tile is free for another body).
11. **Rotate then drop** — rotate the reflector 90° while carrying, then drop.
    The beam routing should reflect the new orientation immediately.

### Feel check

12. Carry speed matches player walk speed (no lag or lead). Bob animation looks
    smooth. Rotation snap is instant (no tween — add one in Phase 9 if desired).

### Regression

13. The emitter still toggles on/off with interact (Phase 4).
14. A direct emitter→target chain (no reflector) still works (Phase 4).
15. Player movement, camera, grid rendering all intact.
