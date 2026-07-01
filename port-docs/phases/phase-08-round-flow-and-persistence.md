# Phase 8 — Round Flow, Scene Transitions & Persistence

**Goal:** The full level lifecycle works — elevator animation brings players in,
the round starts, the player solves the puzzle, the exit button triggers an
elevator-out animation, and the game loads the next scenario. Progress (current
level, story flags) survives a quit-and-relaunch.

Reads from: `01-architecture.md §4`, `02-gameplay-systems.md §7`,
`03-levels-and-data.md §5`, `04-godot-port-plan.md §3 step 9`.

---

## What to build

### 8.1 Round phase enum

Create `scenes/RoundPhase.cs`:

```csharp
public enum RoundPhase
{
    Init,
    PreRoundStart,  // elevator descends, player not controllable
    RoundStart,     // player takes control
    Play,           // normal gameplay
    PreRoundEnd,    // end condition met, elevator ascends
    RoundEnd        // scene transition fires
}
```

### 8.2 `LevelManager.cs`

Promote `LevelLoader` to a `LevelManager` (rename the file, or add a separate
`LevelManager` that wraps it). `LevelManager` owns the round phase machine and
coordinates the elevator sequence:

```csharp
using Godot;

public partial class LevelManager : Node3D
{
    public static LevelManager Instance { get; private set; }

    public RoundPhase Phase { get; private set; } = RoundPhase.Init;

    [Export] private LevelLoader Loader;
    [Export] private ElevatorHandler Elevators;
    [Export] private PlayerManager Players;

    private G.Scenarios _currentScenario;

    public override void _Ready() => Instance = this;

    public void LoadScenario(G.Scenarios scenario)
    {
        _currentScenario = scenario;
        ProgressManager.Instance.SetLastScenario(scenario.ToString());

        Phase = RoundPhase.Init;
        Loader.LoadLevel(scenario.ToString());

        Phase = RoundPhase.PreRoundStart;
        Players.SetControllable(false);
        Elevators.DescendPlayers(() => OnPreRoundStart());
    }

    private void OnPreRoundStart()
    {
        Phase = RoundPhase.RoundStart;
        EventBus.Instance.EmitSignal(EventBus.SignalName.RoundStart);
        Players.SetControllable(true);
        Phase = RoundPhase.Play;
    }

    public void OnEndLevelPressed()
    {
        if (Phase != RoundPhase.Play) return;
        Phase = RoundPhase.PreRoundEnd;
        Players.SetControllable(false);
        Elevators.AscendPlayers(() => OnRoundEnd());
    }

    private void OnRoundEnd()
    {
        Phase = RoundPhase.RoundEnd;
        ProgressManager.Instance.SaveStoryState(null);
        var next = GetNextScenario(_currentScenario);
        GetTree().ChangeSceneToFile(Main.Res.ScenarioPaths[next.ToString()]);
    }

    private G.Scenarios GetNextScenario(G.Scenarios current)
    {
        int next = (int)current + 1;
        if (Enum.IsDefined(typeof(G.Scenarios), next))
            return (G.Scenarios)next;
        return G.Scenarios.Credits;
    }
}
```

### 8.3 `ElevatorHandler.cs` and `Elevator.cs`

`ElevatorHandler` manages one or more `Elevator` nodes in the level. Each
`Elevator` is a platform that the player stands on while it animates down (scene
start) or up (scene end). This is a cosmetic system — the important contract is:

- `DescendPlayers(Action onComplete)` — plays the descent animation, then calls
  `onComplete`.
- `AscendPlayers(Action onComplete)` — plays the ascent animation, then calls
  `onComplete`.

Simple implementation (replace with a proper animation later in Phase 9):

```csharp
public partial class ElevatorHandler : Node3D
{
    [Export] private Elevator[] Elevators;

    public void DescendPlayers(Action onComplete)
    {
        // For now: immediately call back (no animation yet)
        // Real implementation: tween each elevator down, then call back
        CallDeferred(MethodName.CallOnComplete, Callable.From(onComplete));
    }

    public void AscendPlayers(Action onComplete)
    {
        CallDeferred(MethodName.CallOnComplete, Callable.From(onComplete));
    }

    private static void CallOnComplete(Callable cb) => cb.Call();
}
```

`Elevator.cs` — a `Node3D` that holds a player while animating. Add animation
in Phase 9. For now it's just a position node:

```csharp
public partial class Elevator : Node3D
{
    [Export] public Vector3 TopPosition;    // world pos at level top (elevator waiting)
    [Export] public Vector3 BottomPosition; // world pos at grid level
}
```

### 8.4 `EndLevelButton.cs`

A `SurfaceBody` that triggers the round end when interacted with (or auto-triggers
when all puzzles are solved, depending on the level):

```csharp
public partial class EndLevelButton : SurfaceBody, ITriggerable
{
    [Export] public string[] TriggerGroups = Array.Empty<string>();
    [Export] public bool AutoTrigger = false; // if true, fires when signal received

    private bool _activated;

    public override void AfterGridPopulated()
    {
        foreach (var g in TriggerGroups)
            TriggerBus.Instance.RegisterConsumer(g, this);
    }

    public void ReceiveSignal(int strength)
    {
        if (!AutoTrigger || _activated) return;
        if (strength >= 1) Activate();
    }

    public override void OnInteract(Player player)
    {
        if (!_activated) Activate();
    }

    private void Activate()
    {
        if (_activated) return;
        _activated = true;
        LevelManager.Instance.OnEndLevelPressed();
    }
}
```

### 8.5 Scene setup

Each scenario needs a Godot scene file (`scenes/levels/<Scenario>.tscn`). For now,
every level scene is the same `Main.tscn` with the LDTK data configured via an
export. Later you may have per-scenario scenes for décor.

Populate `Res.ScenarioPaths` in `Res.tres`:
```
"HubWorld" → "res://scenes/levels/HubWorld.tscn"
"FirstMachine" → "res://scenes/levels/FirstMachine.tscn"
... etc.
```

The `Main._Ready` bootstraps by reading `ProgressManager.GetLastScenario()` (or
defaults to `Entry` on first run):

```csharp
public override void _Ready()
{
    Res = _res;
    var last = ProgressManager.Instance.GetLastScenario();
    var scenario = string.IsNullOrEmpty(last) ? G.Scenarios.Entry
                   : Enum.Parse<G.Scenarios>(last);
    LevelManager.Instance.LoadScenario(scenario);
}
```

### 8.6 Persist last scenario on quit

`ProgressManager` saves to `user://story_progress.json` on:
- Every `# trigger` tag in narrative (Phase 7).
- `LevelManager.OnRoundEnd()`.
- `SceneTree.QuitRequested` signal (add in `Main._Ready`):

```csharp
GetTree().AutoAcceptQuit = false;
GetTree().QuitRequested += () => {
    ProgressManager.Instance.SaveStoryState(null);
    GetTree().Quit();
};
```

---

## Testing this phase

### Golden path — level start

1. Launch the project. Confirm the **elevator descends** (or, with the stub, that
   the player appears at the spawn position immediately after `LoadScenario`).
2. `RoundStart` event fires — add a temporary `GD.Print("round start")` in
   `EventBus.RoundStart` subscriber.
3. Player is **controllable** after the elevator finishes.

### Golden path — level end

4. Interact with `EndLevelButton` (or auto-trigger by solving the test puzzle).
5. Player becomes **not controllable**.
6. Elevator ascent stub fires, `OnRoundEnd` is called.
7. `GetTree().ChangeSceneToFile(...)` triggers — the next level scene loads (even
   if it's identical to the current one for now).

### Persistence

8. Load a level, reach `PreRoundEnd`, quit the process (`Alt+F4` or stop in Godot).
9. Relaunch. Confirm `ProgressManager.GetLastScenario()` returns the correct
   scenario and `LevelManager.LoadScenario` loads it.
10. Confirm story variable state (from Phase 7 `# trigger` tags) is also restored.

### Phase machine guard

11. While in `PreRoundEnd` (elevator ascending), call `OnEndLevelPressed` again.
    Confirm nothing happens (guard `if (Phase != Play)`).
12. Confirm player cannot move during `PreRoundStart` and `PreRoundEnd`.

### Scenario progression

13. Place `EndLevelButton` in the test level. Trigger it. Confirm the correct
    *next* scenario name appears in the `ChangeSceneToFile` call
    (print it before calling).

### Regression

14. Narrative (Phase 7) — story lockout still stops player movement.
15. Laser puzzle chain (Phases 4–6) still works within a level.
16. Phase 1 autoloads present after scene change (`EventBus`, `TriggerBus`,
    `TimeManager` should survive as autoloads).
