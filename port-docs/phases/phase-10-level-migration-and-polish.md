# Phase 10 — Level Migration, Audio & Final Polish

**Goal:** All 15 scenarios are playable in Godot. Art, audio, lighting, and
camera polish are at shippable quality. The full game can be played from `Entry`
to `Credits` in one sitting.

Reads from: `03-levels-and-data.md §2,4`, `02-gameplay-systems.md §7`,
`04-godot-port-plan.md §5`.

---

## What to build

### 10.1 Migrate all 15 scenarios to LDTK

Work through each scenario in the order below (roughly smallest → largest, to
front-load debugging the pipeline before touching complex levels):

| Priority | Scenario | Grid size | Bodies |
|---|---|---|---|
| 1 | HubWorld | 35×22 | 8 (done in Phase 2) |
| 2 | MachineTestScene | 40×40 | 0 |
| 3 | DesertEntrance | 64×57 | 7 |
| 4 | EmptyScene | 79×75 | 0 |
| 5 | Entry | 81×57 | 53 |
| 6 | MessScene | 64×57 | 54 |
| 7 | BuildingScene_1 | 64×57 | 0 |
| 8 | Vaults | 200×200 | 40 |
| 9 | FirstMachine | 79×75 | 84 |
| 10 | LongMachine | 50×69 | 73 |
| 11 | EasyAmp | 68×66 | 71 |
| 12 | Accelerator | 41×66 | 51 |
| 13 | LastChoice | 62×88 | 0 |
| 14 | TestScene_1 | 105×87 | 0 |
| 15 | Credits | 1×1 | 0 |

**For each scenario:**

1. Run the extractor for that scenario (already done — check `tools/level_export/`).
2. Extend `tools/extract_levels.py` to emit that scenario's level into
   `emerge.ldtk`.
3. Add the Godot scene (`res://scenes/levels/<Scenario>.tscn`) and register its
   path in `Res.ScenarioPaths`.
4. Load in Godot, run the smoke test below.
5. For scenarios where `bodies.json` is **empty or missing** (BuildingScene_1,
   LastChoice, MachineTestScene, TestScene_1, EmptyScene, Credits), use Option B
   from `03-levels-and-data.md §4` (stream-parse the `.unity` scene YAML) to
   extract body placements, or author them by hand in LDTK (small levels only).

**Option B scene ripper** (for body-less scenarios): Extend the extractor with a
`rip_unity_scene(scene_path)` function that:
1. Reads the known GUID→type map from `Assets/Scripts/**/*.cs.meta` files.
2. Stream-parses the `.unity` YAML looking for `MonoBehaviour` components whose
   `m_Script.guid` matches a known body GUID.
3. For each match, walks up to the parent `Transform` to get
   `m_LocalPosition`/`m_LocalRotation`.
4. Outputs a `bodies.json`-compatible list.

### 10.2 3D art import

For each scenario, import the source meshes from `blendfiles/` and `Assets/Models/`
to Godot:

- Open each `.blend` file in Blender, export as **glTF 2.0** (`.glb`).
- Import into `res://art/<category>/`. Godot auto-generates `.import` files.
- Assign materials:
  - Opaque meshes → `StandardMaterial3D` with the albedo texture from
    `Assets/Materials/Textures/`.
  - Emissive elements (laser crystals, glowing panels) → enable Emission in
    `StandardMaterial3D`, drive colour from the indicator system (Phase 9).
  - The laser beam → custom `ShaderMaterial` (Phase 9 §9.7).

Replace placeholder meshes in each body scene (`*.tscn`) with the real
`MeshInstance3D` pointing to the imported glTF mesh.

### 10.3 Lighting

The Unity game uses `LightingPreset` ScriptableObjects for per-scene mood. In Godot:

- Each level scene has a `WorldEnvironment` node with an `Environment` resource.
- Create two or three `Environment` presets (desert daylight, cave/interior, hub)
  and assign per scenario.
- Key properties to port: `sky_color`, `ambient_light_color`, `ambient_light_energy`,
  `fog_enabled`, `fog_density`.
- `DirectionalLight3D` for the sun; tweak `shadow_enabled` and `shadow_bias`.

The Unity `LightingController` also did real-time fades during cutscenes. In Godot,
use `Tween` to animate `WorldEnvironment.environment.ambient_light_energy` and the
directional light's `energy`.

### 10.4 Camera polish

Replace the basic follow camera (Phase 3) with a more capable system:

**Option A — PhantomCamera addon** (recommended if available):
- Install the **PhantomCamera** addon.
- Per-level `PhantomCamera3D` nodes with target groups (player positions averaged).
- Cutscene cameras switch to named `PhantomCamera3D` nodes via the
  `priority` property (higher priority = active camera).

**Option B — hand-rolled:**
- Add damping to the existing `PlayerCamera` using `Tween` or `Lerp` with
  configurable `LerpSpeed` per scenario.
- For cutscenes, add a stack-based camera manager: `CameraManager.PushCamera(cam)`
  / `PopCamera()` where the top of the stack is the active camera.

In both cases, the `# camera X` tag in the narrative (Phase 7) must correctly
switch the active camera.

### 10.5 Audio

Audio files (`Assets/Audio/`) are already in formats Godot can import (`.ogg`,
`.wav`). Import them to `res://audio/`.

Create `AudioDirector.cs` as an autoload (mirrors `AudioDirector` in Unity):

```csharp
using Godot;
using System.Collections.Generic;

public partial class AudioDirector : Node
{
    public static AudioDirector Instance { get; private set; }

    [Export] private Godot.Collections.Dictionary<string, AudioStream> Clips = new();

    private AudioStreamPlayer3D _musicPlayer;

    public override void _Ready()
    {
        Instance = this;
        _musicPlayer = GetNode<AudioStreamPlayer3D>("MusicPlayer");
    }

    public void PlaySfx(string id, Vector3 worldPos)
    {
        if (!Clips.TryGetValue(id, out var clip)) return;
        var p = new AudioStreamPlayer3D();
        AddChild(p);
        p.Stream = clip;
        p.GlobalPosition = worldPos;
        p.Play();
        p.Finished += p.QueueFree;
    }

    public void PlayMusic(string id)
    {
        if (!Clips.TryGetValue(id, out var clip)) return;
        _musicPlayer.Stream = clip;
        _musicPlayer.Play();
    }
}
```

Wire SFX callsites:
- Laser on/off: `AudioDirector.Instance.PlaySfx("laser_activate", Origin)` in
  `EmitterAlien`.
- Device placed: in `Hoverable.EndHover`.
- Door open: in `Door.SetOpen`.
- Dialogue advance: in `DialogManager.AdvanceStory`.
- Elevator: in `ElevatorHandler`.

### 10.6 `LightingController` transitions

During cutscenes, the `# camera X` tag may also imply a lighting change.
Add a `LightingPreset` resource:

```csharp
[GlobalClass]
public partial class LightingPreset : Resource
{
    [Export] public Color AmbientColor = Colors.White;
    [Export] public float AmbientEnergy = 1f;
    [Export] public Color SunColor = Colors.White;
    [Export] public float SunEnergy = 1f;
    [Export] public bool FogEnabled = false;
    [Export] public float FogDensity = 0.01f;
}
```

`LightingController` autoload applies a preset to `WorldEnvironment` and
`DirectionalLight3D`, tweening over 1–2 seconds.

### 10.7 End-to-end play session

Play through the entire game from `Entry` to `Credits`. This is the final
integration test for all phases.

---

## Testing this phase

### Per-scenario smoke test (run for each of the 15 scenarios)

For each scenario, open the Godot editor, run the project with
`LevelManager.LoadScenario(<scenario>)` and confirm:

1. **Grid loads** — tile debug visualiser (or real art) matches the corresponding
   `.map.txt` in `tools/level_export/`.
2. **Bodies spawn** — the count of body nodes under `LevelRoot` matches the
   `bodies.json` count (print in `LevelLoader`).
3. **No orphan tiles** — no body is positioned on a wall tile (`assert
   OccupiedTile.IsPlaceable`).
4. **Player spawns** at the elevator/spawn position, not inside a wall.
5. **Round starts** — `RoundStart` event fires, player is controllable.

### Puzzle verification (for levels with gameplay bodies)

6. Play each puzzle level to completion without hints. If a puzzle is unsolvable,
   the body data is wrong (check against the `.bodies.json` and the Unity source).
7. Solving each puzzle and pressing `EndLevelButton` transitions to the next
   correct scenario.

### Narrative verification

8. Play through every Ink story file in order. Each `# speaker`, `# camera`,
   `# appear`, `# goto`, `# anim`, `# trigger` tag must execute without errors.
9. Choices branch correctly. Story variables are saved and recalled across scene
   loads.

### Audio

10. SFX plays at the correct world position (3D audio falloff — move the player
    away from a sound source and confirm it attenuates).
11. Music transitions between scenarios without clicks or interruptions.

### Performance

12. All scenarios run at **60 fps** on the target hardware.
    - Worst case: `TestScene_1` (105×87 grid, largest bodies array).
    - Use Godot `Profiler` to confirm `_PhysicsProcess` + `_Process` combined cost
      is under 4 ms/frame.
    - If slow: profile `LevelLoader.RunPasses` (should be under 100 ms on load,
      not in hot path). Profile `LaserBeam.Recompute` (should only fire on events).
13. Memory: open `Debugger → Monitor → Memory` for the largest level. No unbounded
    growth during 5 minutes of play.

### Regression (full game playthrough)

14. Play Entry → HubWorld → FirstMachine → ... → Credits in one session without
    restarting the process. Confirm:
    - No crashes.
    - No stuck states (door never opens, story never advances, etc.).
    - `ProgressManager` correctly tracks scenario across all transitions.
    - After Credits, the game returns to a sensible state (main menu or Entry).
