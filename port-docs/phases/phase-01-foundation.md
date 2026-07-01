# Phase 1 — Foundation: Project Skeleton, Autoloads & Registry

**Goal:** A compiling Godot 4.7 (.NET) project with the correct manager/autoload
hierarchy, global constants, and the `Res` registry. Nothing visible yet — this is
pure scaffolding that every later phase builds on.

---

## What to build

### 1.1 Godot project setup

- Create a new Godot 4.7 project with **.NET / C#** enabled.
- Set the renderer to **Forward+** (closest to URP; you can downgrade later if
  mobile targets emerge).
- Add an empty `project.godot` with:
  - `config/name = "Emerge"`
  - `display/window/size/viewport_width = 1920`
  - `display/window/size/viewport_height = 1080`
- Configure **Input Map actions** now, before anything else touches input:

  | Action | Keys | Gamepad |
  |---|---|---|
  | `move_north` | W, Arrow Up | Left stick up |
  | `move_south` | S, Arrow Down | Left stick down |
  | `move_east` | D, Arrow Right | Left stick right |
  | `move_west` | A, Arrow Left | Left stick left |
  | `interact` | E, Space | South button (Cross/A) |
  | `cancel` | Q, Escape | West button (Square/X) |
  | `continue` | Return, Space | South button |
  | `text_skip` | Ctrl | East button (Circle/B) |

### 1.2 Global constants — `G.cs`

Create `autoloads/G.cs`. Mirror the key enums from the Unity `Utils/G.cs`:

```csharp
public static class G
{
    public const float TICK_TIME = 0.2f;
    public const float PLAYER_SPEED = 4f;
    public const float PLAYER_TURN_SPEED = 10f;

    public enum Scenarios
    {
        Entry, HubWorld, FirstMachine, EasyAmp, Accelerator,
        LongMachine, MessScene, Vaults, DesertEntrance,
        BuildingScene_1, LastChoice, Credits
        // add others as needed
    }

    public enum BakedTile
    {
        Null = 0, Sand = 1, Block = 2, Placeable_Static = 3,
        Water = 4, Walkable_Static = 5, Plateable_Static = 6,
        RaycastStaticBlock = 7, RaycastStaticPlaceable = 8
    }

    public enum SurfaceBodyType
    {
        Door, LaserTarget, EmitterAlien, Reflector, Splitter,
        Merger, AmpAlien, ItemOrb, SecurityDoor, KeyDevice,
        TriggerIntermediate, ChargedIntermediate, EndLevelButton
        // extend as needed
    }

    public enum ItemType { None, CrystalFlat, CrystalPyramid, CrystalCube, Wand, EmitterRing }
    public enum GridDir { N, NE, E, SE, S, SW, W, NW }
    public enum LaserDir { N, E, S, W }
    public enum LogLevel { None, Error, Warn, Info, Debug }

    public static LogLevel CurrentLogLevel = LogLevel.Warn;
}
```

Do not add this script as an autoload — it is a static class used by reference.

### 1.3 Event bus autoload — `EventBus.cs`

Create `autoloads/EventBus.cs`. This replaces the Unity `EventManager`. Add it to
`Project → Autoloads` as `EventBus`.

```csharp
using Godot;

public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; }

    // Tick signal (fired by TimeManager)
    [Signal] public delegate void TickEventHandler(int tickNumber);

    // Tile / grid events
    [Signal] public delegate void TileChangedEventHandler(int x, int z);
    [Signal] public delegate void BodyPlacedEventHandler();

    // UI / gameplay events
    [Signal] public delegate void TickUpdateUIEventHandler();
    [Signal] public delegate void PlayerActivityChangedEventHandler(int playerIndex, bool active);
    [Signal] public delegate void HighlightUpdateRequestedEventHandler();
    [Signal] public delegate void RoundStartEventHandler();
    [Signal] public delegate void RoundEndEventHandler();

    public override void _Ready() => Instance = this;
}
```

### 1.4 Trigger bus autoload — `TriggerBus.cs`

Create `autoloads/TriggerBus.cs`. Add to autoloads as `TriggerBus`. This is a
direct port of `TriggerHandler` (see `02-gameplay-systems.md §2`):

```csharp
using Godot;
using System.Collections.Generic;

public partial class TriggerBus : Node
{
    public static TriggerBus Instance { get; private set; }

    private class TriggerGroup
    {
        public Dictionary<int, int> Sources = new(); // instanceId → weight
        public List<ITriggerable> Consumers = new();
    }

    private Dictionary<string, TriggerGroup> _groups = new();

    public override void _Ready() => Instance = this;

    public void RegisterSource(string groupId, int instanceId) =>
        GetOrCreate(groupId).Sources.TryAdd(instanceId, 0);

    public void RegisterConsumer(string groupId, ITriggerable consumer) =>
        GetOrCreate(groupId).Consumers.Add(consumer);

    public void UpdateSource(string groupId, int instanceId, int weight)
    {
        var g = GetOrCreate(groupId);
        g.Sources[instanceId] = weight;
        int total = 0;
        foreach (var w in g.Sources.Values) total += w;
        foreach (var c in g.Consumers) c.ReceiveSignal(total);
    }

    private TriggerGroup GetOrCreate(string id)
    {
        if (!_groups.TryGetValue(id, out var g))
            _groups[id] = g = new TriggerGroup();
        return g;
    }

    public interface ITriggerable { void ReceiveSignal(int strength); }
}
```

### 1.5 Time manager autoload — `TimeManager.cs`

Create `autoloads/TimeManager.cs`. Add to autoloads as `TimeManager`.

```csharp
using Godot;
using System;
using System.Collections.Generic;

public partial class TimeManager : Node
{
    public static TimeManager Instance { get; private set; }

    private float _accumulator;
    private int _tickNumber;

    private record Occurrence(Action Callback, int DueTick, string SenderId);
    private List<Occurrence> _scheduled = new();

    public override void _Ready() => Instance = this;

    public override void _Process(double delta)
    {
        _accumulator += (float)delta;
        if (_accumulator >= G.TICK_TIME)
        {
            _accumulator -= G.TICK_TIME;
            _tickNumber++;
            FireDue();
            EventBus.Instance.EmitSignal(EventBus.SignalName.Tick, _tickNumber);
        }
    }

    public void Schedule(Action callback, float delaySeconds, string senderId)
    {
        int dueTick = _tickNumber + Mathf.CeilToInt(delaySeconds / G.TICK_TIME);
        _scheduled.Add(new Occurrence(callback, dueTick, senderId));
    }

    public void CancelAll(string senderId) =>
        _scheduled.RemoveAll(o => o.SenderId == senderId);

    private void FireDue()
    {
        var due = _scheduled.FindAll(o => o.DueTick <= _tickNumber);
        _scheduled.RemoveAll(o => o.DueTick <= _tickNumber);
        foreach (var o in due) o.Callback();
    }
}
```

### 1.6 Res resource — `Res.cs`

Create `resources/Res.cs` as a `Resource` subclass (not an autoload — it's a
`Resource` asset loaded by `Main`):

```csharp
using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Res : Resource
{
    [Export] public Godot.Collections.Dictionary<string, PackedScene> BodyScenes = new();
    [Export] public Godot.Collections.Dictionary<string, string> ScenarioPaths = new();
    // item type data, laser presets etc. added in later phases
}
```

Create a `Res.tres` asset in the editor and populate the `ScenarioPaths` dictionary
(empty for now, will be filled in Phase 8).

### 1.7 Base node classes

Create `base/EmergeNode.cs` (replaces `EmergeMonoBase`):

```csharp
using Godot;

public partial class EmergeNode : Node3D
{
    protected void Log(string msg, G.LogLevel level = G.LogLevel.Info)
    {
        if (level <= G.CurrentLogLevel) GD.Print($"[{Name}] {msg}");
    }
}
```

### 1.8 Scene root structure

Create the main scene `scenes/Main.tscn`:

```
Main (Node3D)
├── World (Node3D)         ← 3D gameplay world
│   ├── LevelRoot (Node3D) ← instantiated level content goes here
│   └── Players (Node3D)
└── GUI (CanvasLayer)      ← all UI
    ├── HUD (Control)
    └── DialogLayer (Control)
```

`Main` is not a manager — it just holds the scene tree structure. The managers are
autoloads (EventBus, TriggerBus, TimeManager) accessed as singletons. Leave a stub
`Main.cs` that loads `Res.tres` and exposes it as a static reference:

```csharp
public partial class Main : Node3D
{
    public static Res Res { get; private set; }
    [Export] private Res _res;
    public override void _Ready() => Res = _res;
}
```

---

## Testing this phase

**There is nothing to run yet**, but you can verify the foundation statically:

1. **Compile check** — open the project in Godot, click **Build** (or `dotnet build`
   in a terminal). Zero errors expected. A clean build proves all class names,
   `[Signal]` delegates, and `[Export]` types are valid.

2. **Autoload check** — run the project. In the Godot **Remote** inspector, expand
   `/root/`. Confirm `EventBus`, `TriggerBus`, and `TimeManager` are all present as
   top-level nodes.

3. **Tick check** — add a temporary one-liner to `Main._Ready`:
   ```csharp
   EventBus.Instance.Tick += n => GD.Print($"tick {n}");
   ```
   Run for 2 seconds; you should see `tick 1` through `tick 10` (±1) in the Output
   panel. Remove it after confirming.

4. **Input check** — confirm all Input Map actions appear in
   `Project → Project Settings → Input Map`. Test at least two bindings with a
   gamepad or keyboard.

5. **No regressions** — this phase introduces no gameplay; there is nothing to
   break. The only failure mode is a compile error or missing autoload.
