# Phase 4 — Laser Core: Emitter → Beam → Target

**Goal:** A complete single-chain laser puzzle works end-to-end. The player walks
up to an `EmitterAlien`, presses interact to toggle it on, a beam shoots out,
hits a `LaserTarget`, and the target's triggered state changes (door opening
deferred to Phase 6). No reflection yet — that is Phase 5.

Reads from: `02-gameplay-systems.md §1`, `01-architecture.md §3`,
`04-godot-port-plan.md §2,3`.

---

## What to build

### 4.1 Interfaces

Create `bodies/interfaces/ILaserSource.cs` and `bodies/interfaces/ILaserHandler.cs`:

```csharp
using Godot;

public interface ILaserSource
{
    int Intensity { get; }
    Vector3 Origin { get; }
    Vector3 Direction { get; }  // unit vector on X/Z plane
}

public interface ILaserHandler
{
    bool IsLaserInteractable { get; }
    bool TryAddBeam(LaserBeam beam, Vector3 hitPoint);
    void RemoveBeam(LaserBeam beam);
    Vector3 GetEndPoint(Vector3 hitPoint);
}
```

### 4.2 `Machine` base class

Create `bodies/Machine.cs` extending `SurfaceBody`. Adds the 3-state enum and
interaction hook:

```csharp
public enum MachineState { Idle, Hovering, Working }

public abstract partial class Machine : SurfaceBody
{
    public MachineState State { get; private set; } = MachineState.Idle;

    // Override in subclass to respond to state transitions
    protected virtual void OnEnterIdle() {}
    protected virtual void OnEnterHovering() {}
    protected virtual void OnEnterWorking() {}
    protected virtual void OnExitIdle() {}
    protected virtual void OnExitHovering() {}
    protected virtual void OnExitWorking() {}

    public void SwitchState(MachineState next)
    {
        switch (State) {
            case MachineState.Idle:     OnExitIdle(); break;
            case MachineState.Hovering: OnExitHovering(); break;
            case MachineState.Working:  OnExitWorking(); break;
        }
        State = next;
        switch (State) {
            case MachineState.Idle:     OnEnterIdle(); break;
            case MachineState.Hovering: OnEnterHovering(); break;
            case MachineState.Working:  OnEnterWorking(); break;
        }
    }

    // Called by Player.InteractProbe
    public virtual void OnInteract(Player player) {}
    public virtual void OnCancel(Player player) {}
}
```

### 4.3 `LaserBeam.cs`

The beam is a node that does a raycast and draws a line. Key design change from
Unity: **do not raycast every frame**. Instead raycast on demand, triggered by
`Recompute()` which is called when:
- The beam's source activates or deactivates.
- A device in the beam's path is placed, removed, or rotated.

```csharp
using Godot;
using System.Collections.Generic;

public partial class LaserBeam : Node3D
{
    public int Intensity { get; private set; }
    public Vector3 Origin { get; private set; }
    public Vector3 Direction { get; private set; }

    private ILaserHandler _currentTarget;
    private MeshInstance3D _beamMesh; // stretched quad
    private static readonly uint LaserMask = 1; // physics layer 1

    public void Activate(int intensity, Vector3 origin, Vector3 direction)
    {
        Intensity = intensity; Origin = origin; Direction = direction;
        Visible = true;
        Recompute();
    }

    public void Deactivate()
    {
        Visible = false;
        _currentTarget?.RemoveBeam(this);
        _currentTarget = null;
    }

    public void Recompute()
    {
        _currentTarget?.RemoveBeam(this);
        _currentTarget = null;

        var query = PhysicsRayQueryParameters3D.Create(
            Origin, Origin + Direction * 100f, LaserMask);
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

        Vector3 endPoint = Origin + Direction * 100f;

        if (result.Count > 0)
        {
            var collider = result["collider"].As<Node>();
            var handler = collider?.GetParentOrNull<ILaserHandler>()
                       ?? collider as ILaserHandler;

            if (handler != null && handler.IsLaserInteractable)
            {
                var hitPoint = result["position"].As<Vector3>();
                if (handler.TryAddBeam(this, hitPoint))
                {
                    _currentTarget = handler;
                    endPoint = handler.GetEndPoint(hitPoint);
                }
            }
        }

        DrawBeam(Origin, endPoint);
    }

    private void DrawBeam(Vector3 from, Vector3 to)
    {
        // Position and scale a thin box along the beam axis
        var mid = (from + to) * 0.5f;
        var length = from.DistanceTo(to);
        GlobalPosition = mid;
        LookAt(to, Vector3.Up);
        Scale = new Vector3(0.05f, 0.05f, length);
    }
}
```

Create a simple `LaserBeam.tscn`:
```
LaserBeam (Node3D)           ← LaserBeam.cs
└── MeshInstance3D           ← BoxMesh, coloured by intensity (red/orange/white)
```

For now, use a single `StandardMaterial3D` with `emission_enabled = true` and a
colour driven by `Intensity` (1 = red, 2 = orange, 3 = white). The sinewave wobble
shader comes in Phase 9.

### 4.4 `EmitterAlien.cs`

A `Machine` + `ILaserSource`. Sits on a fixed tile (not hoverable in this phase —
the Hoverable component is Phase 5).

```csharp
using Godot;

public partial class EmitterAlien : Machine, ILaserSource
{
    [Export] public int Intensity { get; private set; } = 1;

    private LaserBeam _beam;
    private Vector3 _emitDir = Vector3.Forward; // -Z in local; rotated by node rotation

    public Vector3 Origin => GlobalPosition + Vector3.Up * 0.5f;
    public Vector3 Direction => -GlobalTransform.Basis.Z; // emits forward

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        _beam = GD.Load<PackedScene>("res://scenes/laser/LaserBeam.tscn")
                   .Instantiate<LaserBeam>();
        AddChild(_beam);
        _beam.Deactivate();
    }

    public override void OnInteract(Player player)
    {
        if (State == MachineState.Working)
            SwitchState(MachineState.Idle);
        else
            SwitchState(MachineState.Working);
    }

    protected override void OnEnterWorking() => _beam.Activate(Intensity, Origin, Direction);
    protected override void OnExitWorking() => _beam.Deactivate();
}
```

Wire player `OnInteract` to call the machine's `OnInteract` when the player presses
interact near a machine:

```csharp
// In Player.cs _PhysicsProcess (or _Input):
if (Input.IsActionJustPressed("interact"))
{
    foreach (var body in _interactProbe.GetOverlappingBodies())
    {
        if (body is Machine m) { m.OnInteract(this); break; }
    }
}
```

### 4.5 `LaserTarget.cs`

An `ILaserHandler` (not a `Machine` — it's fixed and not interactable by the
player). Receives a beam and updates its triggered state:

```csharp
using Godot;

public partial class LaserTarget : SurfaceBody, ILaserHandler
{
    [Export] public int RequiredIntensity = 1;

    public bool IsLaserInteractable => true;
    public bool IsTriggered { get; private set; }

    private readonly List<LaserBeam> _beams = new();

    public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
    {
        if (beam.Intensity < RequiredIntensity) return false;
        _beams.Add(beam);
        UpdateState();
        return true;
    }

    public void RemoveBeam(LaserBeam beam)
    {
        _beams.Remove(beam);
        UpdateState();
    }

    public Vector3 GetEndPoint(Vector3 hitPoint) => hitPoint;

    private void UpdateState()
    {
        bool hit = _beams.Count > 0;
        if (hit == IsTriggered) return;
        IsTriggered = hit;
        GD.Print($"LaserTarget {Name}: triggered={IsTriggered}");
        // Phase 6: call TriggerBus.Instance.UpdateSource(...)
    }
}
```

Add a `CollisionShape3D` (BoxShape3D 0.9×0.9×0.9) to `LaserTarget.tscn` on the
laser physics layer (layer 1) so the beam's raycast hits it.

### 4.6 Build a test level

In the LDTK project (or by hand in a scene), place:
- One `EmitterAlien` facing east (rotation Y = 90°) at grid (5, 5).
- One `LaserTarget` at grid (10, 5).
- Walkable tiles between them; wall tiles elsewhere.

Load this level in `Main._Ready`. The emitter must be in `Res.BodyScenes` as
`"EmitterAlien"` and `LaserTarget` as `"LaserTarget"` so `LevelLoader` can
instance them.

---

## Testing this phase

### Golden path

1. Run the project. Walk the player to the `EmitterAlien`.
2. Press interact. A red beam should **appear** shooting eastward from the emitter.
3. The beam **terminates at the `LaserTarget`** (not shooting off to infinity).
4. The Output panel prints `LaserTarget _: triggered=True`.
5. Press interact again. The beam **disappears**. Output prints `triggered=False`.

### Edge cases

6. **Direction** — rotate the `EmitterAlien` 90° increments in the LDTK data (0,
   90, 180, 270). Run the project each time and confirm the beam shoots in the
   expected cardinal direction (N/E/S/W).
7. **Blocked beam** — place a wall tile directly between emitter and target. The
   beam should be blocked by the wall's physics collision and **not** reach the
   target. (Requires wall tiles to have `StaticBody3D` + `CollisionShape3D` on
   layer 1 — add a mesh + collision to the tile debug visualiser for wall tiles.)
8. **Intensity mismatch** — set `RequiredIntensity = 2` on the target and
   `EmitterAlien.Intensity = 1`. Toggle the emitter on. The beam must reach the
   target geometry but `IsTriggered` stays false.

### No per-frame cost

9. In the Profiler, confirm `LaserBeam._Process` is **absent or zero** — the beam
   must not raycast every frame. `Recompute()` should only appear in the profiler
   during the frame that interact is pressed.

### Regression

10. Player movement and camera from Phase 3 still work.
11. Grid tiles still render (debug visualiser from Phase 2).
12. Tick fires, EventBus present (Phase 1).
