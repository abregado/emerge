# Phase 9 — Remaining Devices, Indicators & VFX

**Goal:** Port all remaining body types, the `IIndicate` indicator system, item
carry mechanics, the remaining laser device variants (Splitter, Merger, AmpAlien),
and re-author the shaders and particle effects. After this phase the game is
feature-complete; only content population (all 15 levels) remains.

Reads from: `02-gameplay-systems.md §1,3,6`, `04-godot-port-plan.md §3 step 10`.

---

## What to build

### 9.1 Remaining laser devices

All three follow the same pattern as `Reflector` (Phase 5): `Machine` + `ILaserHandler`
with `ReflectionPoint` children. The differences are in beam arithmetic.

#### `Splitter.cs`

Receives one beam, outputs two beams at 90° to the input direction:

```csharp
public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
{
    // Determine input direction, find two output points (perpendicular)
    var inDir = WorldDirToLaserDir(-beam.Direction);
    var outA = TurnLeft(inDir);
    var outB = TurnRight(inDir);
    // Activate both output LaserBeams at same intensity
}
```

Helper: `TurnLeft(N)→W, (E)→N, (S)→E, (W)→S` etc.

#### `Merger.cs`

Accepts beams from two directions, outputs one beam at the merged intensity
(sum of inputs, capped at max):

```csharp
// _pendingBeams: Dictionary<LaserBeam, G.LaserDir>
// On TryAddBeam: add to _pendingBeams, recompute output
// Output intensity = sum of all pending beam intensities
// Output direction = the one ReflectionPoint whose InputDir is neither input
```

#### `AmpAlien.cs`

Receives one beam, outputs one beam at `Intensity + 1` in the same direction:

```csharp
public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
{
    _outBeam.Activate(beam.Intensity + 1, Origin, beam.Direction);
    return true;
}
```

Add `ReflectionPoint` children to each device scene configured with the appropriate
`InputDir`/`OutputDir` pairs.

### 9.2 Item system

Items (`G.ItemType`) are small objects the player carries in their hands.

**`ItemOrb.cs`** — a `SurfaceBody` that can be picked up:

```csharp
public partial class ItemOrb : SurfaceBody
{
    [Export] public G.ItemType ItemType;

    public override void OnInteract(Player player)
    {
        if (player.CarriedItem == G.ItemType.None)
        {
            player.GiveItem(ItemType);
            RemoveBody();
            QueueFree();
        }
    }
}
```

**Update `Player.cs`:**
- Add `G.ItemType CarriedItem` property.
- `GiveItem(type)`: swap the `HandSlot` MeshInstance3D to use the item mesh from
  `Res.ItemTypes[type]`.
- `TakeItem()`: return the item type, clear `HandSlot`.

**`SingleItemPedestal.cs`** — a `Machine` that accepts a specific item:

```csharp
public override void OnInteract(Player player)
{
    if (player.CarriedItem == RequiredItem)
    {
        player.TakeItem();
        // Trigger effect (usually unlocks something via TriggerBus)
        TriggerBus.Instance.UpdateSource(GroupId, GetInstanceId().GetHashCode(), 1);
        SwitchState(MachineState.Working);
    }
}
```

**`ItemDispenserTimed.cs`** — spawns an `ItemOrb` on the grid at an interval:

```csharp
public override void AfterGridPopulated()
{
    TimeManager.Instance.Schedule(SpawnItem, SpawnIntervalSeconds, Name);
}

private void SpawnItem()
{
    var orb = GD.Load<PackedScene>($"res://scenes/bodies/ItemOrb.tscn").Instantiate<ItemOrb>();
    orb.ItemType = DispensedType;
    GetParent().AddChild(orb);
    orb.GlobalPosition = Grid.GetWorldCenter(OccupiedTile.X, OccupiedTile.Z + 1);
    orb.Init(Grid); orb.ActivateOnGrid(); orb.AfterGridPopulated();
    TimeManager.Instance.Schedule(SpawnItem, SpawnIntervalSeconds, Name); // reschedule
}
```

**`EmitterAlien` repair:** Add `[Export] public bool NeedsRepair` and
`[Export] public G.ItemType RepairItem`. When `NeedsRepair` is true, `OnInteract`
only toggles if the player has the repair item; otherwise it triggers the repair
sequence (swap mesh, clear `NeedsRepair`, take item from player).

### 9.3 `KeyDevice.cs` and `KeyDeviceDetector.cs`

`KeyDevice` is a device the player carries (Hoverable) to a `KeyDeviceDetector`
socket. When dropped on the detector's tile:

```csharp
// KeyDeviceDetector.AfterGridPopulated checks if a KeyDevice is already on its tile;
// on TileChanged event or body placed event it re-evaluates.
public override void AfterGridPopulated()
{
    EventBus.Instance.BodyPlaced += OnBodyPlaced;
}

private void OnBodyPlaced()
{
    if (OccupiedTile.SurfaceBody is KeyDevice)
        TriggerBus.Instance.UpdateSource(GroupId, GetInstanceId().GetHashCode(), 1);
}
```

### 9.4 Security doors

`SecurityDoor.cs` is a `Door` variant with a different open/close condition (often
requires specific story progress rather than a laser trigger). Subclass `Door` and
override `ReceiveSignal` or connect to a `StoryVariableTrigger.Triggered` signal.

### 9.5 `IIndicate` indicator system

Create `indicators/IIndicator.cs`:

```csharp
public interface IIndicator
{
    void SetState(bool on);
    void SetState(int level);
}
```

Implement several concrete indicator nodes — these replace Unity's large
`IndicatorParts/` family:

| Indicator | Godot implementation |
|---|---|
| `LightIndicator` | `OmniLight3D` toggle, tweak `energy` via `Tween` |
| `MaterialSwapIndicator` | swap `MeshInstance3D.MaterialOverride` |
| `VisibilityIndicator` | toggle `Node3D.visible` |
| `ParticleIndicator` | `GPUParticles3D.emitting = on` |
| `AnimationIndicator` | `AnimationPlayer.play("on")` / `play("off")` |
| `MeshSwapIndicator` | swap `MeshInstance3D.Mesh` |

Each body exposes its current state by calling `SetState` on its list of
`IIndicator` children:

```csharp
// In Machine, after SwitchState:
protected void NotifyIndicators(bool active)
{
    foreach (var ind in GetChildren().OfType<IIndicator>())
        ind.SetState(active);
}
```

### 9.6 `HighlightHandler` — context prompts

`HighlightHandler` shows UI prompts near the body the player is looking at. In
Godot:

```csharp
// HighlightHandler.cs — child of GUI/HUD
// Each tick (or on OnRequestHighlightUpdate), ask the body under the player's
// interact probe for its context:

public interface IHighlightable
{
    string GetHighlightLabel(Player player); // e.g. "Lift", "Drop", "Rotate"
}
```

Draw the label as a `Label3D` node parented to the body (or as a world-space
`CanvasLayer` label following the body's screen position). Update on
`EventBus.HighlightUpdateRequested`.

### 9.7 Laser beam shader

Replace the placeholder box mesh with a proper beam visual:

- Geometry: a `MeshInstance3D` using `ImmediateMesh` (or a `QuadMesh` stretched
  along the beam axis).
- Shader: a `ShaderMaterial` in Godot shading language. Implement:
  - **Base colour** driven by a `uniform vec4 beam_color` (set from intensity).
  - **Sinewave wobble** via vertex displacement: `VERTEX.y += sin(UV.x * freq + TIME * speed) * amplitude`.
  - **Additive blending** (`render_mode blend_add`) for the glow effect.

For intensity 1 = red `(1.0, 0.1, 0.0)`, 2 = orange `(1.0, 0.5, 0.0)`,
3 = white `(1.0, 1.0, 1.0)`.

### 9.8 Elevator animations

Replace the stub `ElevatorHandler` with real animations:

- Each `Elevator` node has an `AnimationPlayer` with clips `"descend"` and
  `"ascend"`.
- `DescendPlayers`: play `"descend"` on all elevators, connect
  `animation_finished` to the `onComplete` callback.
- Player is parented to the elevator platform while it moves (reparent in
  `BeginElevator`, reparent back to `Players` in `EndElevator`).

### 9.9 `SignalCable` visual

Port the DOTween path animation: use a `Path3D` + `PathFollow3D` node with a
particle emitter. When `ReceiveSignal(strength > 0)`, set
`GPUParticles3D.emitting = true` and activate the `LightIndicator` along the cable.
Use `Tween` to animate a `PathFollow3D.progress_ratio` from 0→1 over ~1 second.

### 9.10 Wall tile colliders

Replace the debug visualiser quads with real geometry:

- Wall tiles (`BakedTile.RaycastStaticBlock`) → `StaticBody3D` +
  `CollisionShape3D` (BoxShape3D 1×2×1) + `MeshInstance3D` with a wall mesh.
- Floor tiles → flat mesh only (no collision needed for player; the
  `CharacterBody3D` gravity keeps the player grounded).
- Water/sand tiles → floor mesh with a different material.

This also means the laser raycast correctly hits walls without needing a debug
visualiser.

---

## Testing this phase

### Splitter

1. Place `EmitterAlien` → `Splitter` → two `LaserTarget` nodes (one N, one S of
   the splitter). Turn emitter on. Both targets must trigger simultaneously.
2. Pick up and rotate the splitter 90°. The two output beams rotate with it.

### Merger

3. Two `EmitterAlien` nodes (both intensity 1) → `Merger` → one `LaserTarget`
   requiring `intensity = 2`. Confirm target triggers only when both emitters are
   on.

### AmpAlien

4. `EmitterAlien (intensity 1)` → `AmpAlien` → `LaserTarget (requiredIntensity=2)`.
   With AmpAlien in path, target triggers. Remove AmpAlien → target un-triggers.

### Item carry

5. Walk over an `ItemOrb`. Player's hand mesh swaps to the item.
6. Walk to a `SingleItemPedestal` expecting that item. Interact — item is consumed,
   pedestal enters Working state.
7. Walk to a pedestal expecting a *different* item. Interact does nothing.

### Indicators

8. Toggle `EmitterAlien` on. Confirm `LightIndicator` brightens (or whatever
   indicator is attached to the emitter).
9. Open a door. Confirm a `VisibilityIndicator` on the door hides the door mesh.

### Highlight prompts

10. Walk near a hoverable device. Confirm "Lift" appears. While carrying, confirm
    "Drop" appears over valid tiles and nothing (or "Invalid") over walls.

### Laser shader

11. Beam is visible, additive-glowing, with a slight sinewave wobble on the edges.
12. Intensity 1 = red, 2 = orange, 3 = white.

### Wall colliders

13. The debug tile visualiser can be disabled. Wall tiles still block player
    movement (physical collision). Wall tiles still block lasers (physics layer 1).

### Regression

14. All previous phases work: carry/rotate (5), triggers/doors (6), narrative (7),
    scene transitions (8).
15. Time to load HubWorld (measured from `LevelLoader.LoadLevel` call to
    `RoundStart`) is under 2 seconds. Profile if over.
