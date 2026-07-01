# Phase 3 — Player & Camera

**Goal:** A controllable character that moves across the tile grid, turns smoothly,
and is tracked by a top-down orthographic camera. The player must respect
walkability (cannot step on wall tiles). Interaction and carry mechanics are
**not** in this phase — those come in Phase 5.

Reads from: `02-gameplay-systems.md §4`, `04-godot-port-plan.md §2`.

---

## What to build

### 3.1 Player scene — `Player.tscn`

Structure:
```
Player (CharacterBody3D)           ← Player.cs
├── CollisionShape3D               ← CapsuleShape3D, height=1.8, radius=0.3
├── View (Node3D)                  ← visuals only; logic stays on Player root
│   └── MeshInstance3D             ← placeholder capsule mesh (replace with real mesh later)
├── HandSlot (Node3D)              ← child where carried items attach (Phase 5)
└── InteractProbe (Area3D)         ← sphere of radius 0.8 in front of player; detects bodies
    └── CollisionShape3D
```

Place `Player.tscn` under `scenes/player/`.

### 3.2 `Player.cs`

```csharp
using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public int PlayerIndex = 0;
    [Export] private float Speed = G.PLAYER_SPEED;
    [Export] private float TurnSmooth = G.PLAYER_TURN_SPEED;

    private Grid<EmergeTile> _grid;
    private bool _controllable = true;

    // Called by LevelLoader after AfterGridPopulated
    public void Init(Grid<EmergeTile> grid) => _grid = grid;

    public void SetControllable(bool value) => _controllable = value;

    public override void _PhysicsProcess(double delta)
    {
        if (!_controllable) { Velocity = Vector3.Zero; MoveAndSlide(); return; }

        var input = Input.GetVector("move_west", "move_east", "move_north", "move_south");
        var dir = new Vector3(input.X, 0, input.Y).Normalized();

        Velocity = dir * Speed;
        MoveAndSlide();

        if (dir != Vector3.Zero)
        {
            var targetAngle = Mathf.Atan2(dir.X, dir.Z);
            Rotation = new Vector3(0,
                Mathf.LerpAngle(Rotation.Y, targetAngle, TurnSmooth * (float)delta),
                0);
        }
    }
}
```

**Walkability enforcement:** `MoveAndSlide` handles collision geometry, but the
grid also needs logical checking. After each `MoveAndSlide`, confirm the player's
new tile is walkable; if not, snap them back. In practice the mesh colliders on
wall tiles (added in Phase 9) will do this physically — for now, add a simple
tile-check fallback:

```csharp
// at end of _PhysicsProcess, after MoveAndSlide:
if (_grid != null)
{
    var tile = _grid.Get(GlobalPosition);
    if (tile != null && !tile.IsWalkable)
        GlobalPosition = _grid.GetWorldCenter(tile.X, tile.Z); // push back (crude)
}
```

### 3.3 Interaction probe

`InteractProbe` (the `Area3D` child) sits 0.6 units in front of the player (along
local -Z). Each physics frame, advance its position:

```csharp
_interactProbe.Position = -Transform.Basis.Z * 0.6f;
```

When the player presses `interact`, call a method on the first `Body` found in
`_interactProbe.GetOverlappingBodies()`. This wiring is used in Phase 4 (emitter
toggle) and Phase 5 (hover pick-up).

### 3.4 Camera — `PlayerCamera.tscn`

```
PlayerCamera (Camera3D)    ← PlayerCamera.cs
```

`PlayerCamera.cs` — a simple follow camera:

```csharp
using Godot;

public partial class PlayerCamera : Camera3D
{
    [Export] private float Height = 18f;
    [Export] private float LerpSpeed = 5f;
    [Export] private float FovDeg = 60f;   // or use orthographic

    private Node3D _target;

    public void SetTarget(Node3D target) => _target = target;

    public override void _Ready()
    {
        Projection = ProjectionType.Perspective;
        Fov = FovDeg;
        // Tilt down ~60° from horizontal (top-down-ish)
        RotationDegrees = new Vector3(-60, 0, 0);
    }

    public override void _Process(double delta)
    {
        if (_target == null) return;
        var desired = _target.GlobalPosition + new Vector3(0, Height, Height * 0.5f);
        GlobalPosition = GlobalPosition.Lerp(desired, LerpSpeed * (float)delta);
    }
}
```

Add `PlayerCamera` as a child of `World` in `Main.tscn`.

### 3.5 `PlayerManager.cs`

Create `scenes/PlayerManager.cs` as a child node of `Main`. Responsibilities:

- Spawn the player at the level's elevator/spawn position (hard-code a tile for now;
  the real elevator system comes in Phase 8).
- Hand the player the grid reference after `LevelLoader` finishes the three passes.
- Assign the camera target.
- Expose `SetLocalPlayerControllable(bool)` for narrative lock-out (Phase 7).

```csharp
public partial class PlayerManager : Node3D
{
    [Export] private PackedScene PlayerScene;
    [Export] private PlayerCamera Camera;

    private Player _player;

    public void Init(Grid<EmergeTile> grid, Vector3 spawnPos)
    {
        _player = PlayerScene.Instantiate<Player>();
        GetNode<Node3D>("/root/Main/World/Players").AddChild(_player);
        _player.GlobalPosition = spawnPos;
        _player.Init(grid);
        Camera.SetTarget(_player);
    }

    public void SetControllable(bool v) => _player?.SetControllable(v);
}
```

### 3.6 Spawn position

Hard-code a walkable spawn tile per level for now. In HubWorld the hub centre is
approximately grid (17, 11). In `LevelLoader.LoadLevel`, after the three passes:

```csharp
var spawnTile = Grid.Get(17, 11); // HubWorld centre (adjust per level)
playerManager.Init(Grid, Grid.GetWorldCenter(spawnTile.X, spawnTile.Z));
```

Real elevator spawn logic lands in Phase 8.

---

## Testing this phase

### Manual play test

1. Run the project. You should be able to **walk the player around** the HubWorld
   grid using WASD / left stick.
2. The player **cannot walk into wall tiles** — it stops at the boundary.
3. The camera **follows the player** smoothly.
4. The player **turns to face their movement direction**.

### Input coverage

5. Test keyboard (WASD) and, if available, a gamepad (left stick). Both should
   drive movement.
6. Press `interact` (E / South button) near a placeholder box — confirm no crash
   (the handler can be a no-op `GD.Print("interact")` stub for now).

### Camera framing

7. Walk to the north and south extents of the HubWorld grid. The camera should
   stay above the player and the grid should remain visible. Adjust `Height` and
   `LerpSpeed` exports until the feel is acceptable.

### Walkability

8. From the tile debug visualiser (Phase 2), identify a wall tile adjacent to the
   player spawn. Walk into it — the player must stop. Confirm they don't clip
   through.

### Performance baseline

9. Open the Godot **Profiler** (`Debugger → Profiler`). With the player moving,
   `_PhysicsProcess` for `Player` should cost well under 0.5 ms per frame.
   `_Process` for `PlayerCamera` similarly trivial. If either is high, investigate.

### Regression

10. Phase 2 grid and bodies load correctly and the tile visualiser is still visible.
11. Phase 1 autoloads (`EventBus`, `TriggerBus`, `TimeManager`) still present;
    tick fires at ~5 Hz.
