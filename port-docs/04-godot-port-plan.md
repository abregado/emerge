# 04 — Godot 4.7 Port Plan

A concrete Unity→Godot mapping, the decisions you need to make, and a build order.
Pairs with your `godot-port-guide.md` (the API digest) at the repo root.

---

## 1. The decisions to make first

| Decision | Recommendation | Why |
|---|---|---|
| **GDScript or C#?** | **C#** for the gameplay/manager logic; GDScript fine for UI/glue. | Your code is C# already — `Body`, the trigger bus, the laser logic, the tick scheduler all port almost line-for-line. Re-use the Ink C# runtime too. |
| **True-3D or fake-depth 2D?** | **True 3D** (`Node3D`/`Camera3D`, orthographic-ish). | The game *is* 3D meshes with real lighting and a top-down camera; the grid is already X/Z. Going 2D would mean re-authoring all art. Keep 3D; LDTK stays a logical authoring tool, not the render layer. |
| **Dialogue system** | **inkgd** (Ink runtime for Godot) + re-implement the tag dispatcher. | Your narrative is already authored in Ink (`Assets/Stories/*.json`). inkgd runs those files; you only rewrite `DialogManager.ParseTags`. |
| **Levels** | **LDTK** for logical layout (tiles + entity placements), instanced into a 3D scene at load. | See `03-levels-and-data.md`. The `ldtk-importer` Godot addon reads `.ldtk`. |
| **Save format** | `FileAccess`+`JSON` to `user://` (mirror `ProgressData`). | Tiny save surface (story flags + current level). |

---

## 2. Unity → Godot concept map

| Unity (this project) | Godot 4.7 |
|---|---|
| `EmergeSceneManager` + `FindObjectOfType` wiring | A `Main` (`Node3D`) scene with child manager nodes; grab via `@onready`/groups. Keep the **explicit `Init()` order** (don't trust `_ready` order). |
| `Res` (ScriptableObject registry) | A **`Resource`** (`class_name Res`) holding `PackedScene` arrays + dictionaries; or an autoload. |
| `EventManager` (C# events) | An **autoload** event bus emitting Godot **signals**. |
| `TriggerHandler` (signal bus) | Keep verbatim as a node/autoload, or an event-bus signal `group_changed(id, strength)`. |
| `TimeManager` tick (0.2s) | One autoload with a `_process` accumulator firing `tick(n)`; or `Timer`. Scheduled `Occurrence`s → small structs in a sorted array, or `get_tree().create_timer()` + `await`. |
| `Body`/`SurfaceBody`/`Machine` hierarchy | Mirror as `Node3D` scripts (C#). Each body **type = a scene** (`.tscn`) with a `View` child node for visuals (same split you have now). |
| `Grid<EmergeTile>` | Plain C# class, unchanged. Keep X/Z world mapping. |
| `EmergeTile` flags + `OnTileChanged` | Same data; fire a signal instead of the static C# event. |
| `Hoverable` (lift/carry/rotate, Rigidbody) | `RigidBody3D`/`AnimatableBody3D` or just kinematic transform on a `Node3D`; reproduce the freeze-constraints by moving on X/Z only. |
| `LaserBeam` (LineRenderer + raycast) | `RayCast3D` (or manual `intersect_ray`) + a beam mesh/`MeshInstance3D` or shader quad. **Re-raycast on change, not every frame** (your TODO). `Sinewave` → vertex shader or `Curve`. |
| `BasePlayer`/`LocalPlayer` + Rewired | `CharacterBody3D` + **Input Map actions** (keyboard+gamepad) + `Input.get_vector()`. |
| `CameraDirector` + Cinemachine | `Camera3D` + **PhantomCamera** addon (target groups/blends) or a hand-rolled follow that averages player positions. |
| `DialogManager` + Ink-Unity | **inkgd** runtime; port `ParseTags` to GDScript/C#; actors/cameras/destinations = named nodes found at load (same as now). |
| `IIndicate` indicator family | Small scripts toggling `visible`/`AnimationPlayer`/`GPUParticles3D`/`Tween`. |
| DOTween | Godot **`Tween`** / `AnimationPlayer`. |
| ES3 (Easy Save) | `FileAccess` + `JSON`, or `ResourceSaver`. |
| Doozy UI | Godot `Control` UI + the event-bus autoload; `CanvasLayer` for HUD. |
| `Scenarios` enum → scene name (`Res`) | Same enum → `PackedScene`/path map; `get_tree().change_scene_to_packed()`. |
| URP materials/shaders | Godot `StandardMaterial3D` / `ShaderMaterial` (rewrite the laser/dissolve shaders in Godot shading language). |

---

## 3. Recommended build order

Build vertically — get one trivial level fully playable, then widen.

1. **Project skeleton.** Godot 4.7 (.NET), define **Input Map** actions
   (move/interact/cancel/continue/skip for keyboard *and* gamepad). Create the
   `Main → World + GUI` root, the manager autoloads (EventBus, TriggerBus, Time),
   and the `Res` resource.
2. **Grid + tiles.** Port `Grid<EmergeTile>` and `EmergeTile` (almost verbatim).
3. **LDTK import.** Add the `ldtk-importer` addon. Extend `tools/extract_levels.py`
   to emit `.ldtk` from the clean JSON (or hand-build one small level first).
   Write the loader that reads the IntGrid → builds the logical grid, and the
   Entities layer → instances body scenes at the right X/Z. Reproduce the
   **Init → ActivateOnGrid → AfterGridPopulated** multi-pass.
4. **Player.** `CharacterBody3D` movement on X/Z, top-down `Camera3D` follow.
5. **One device end-to-end.** `EmitterAlien` (source) → `LaserBeam` (RayCast3D +
   mesh) → `LaserTarget`. Get a beam drawing and hitting.
6. **Carry/rotate.** Port `Hoverable`; make `Reflector` liftable/rotatable and
   make it reflect a beam. This is the core verb — make it feel right.
7. **Triggers.** Port `TriggerHandler`; wire `LaserTarget → group → Door`. Now a
   full mini-puzzle works.
8. **Narrative.** inkgd + the tag dispatcher + `DialogPanel`/`OptionsPanel` UI;
   load one existing Ink story. Hook `# trigger` story-vars to `ProgressManager`.
9. **Round flow + scene transitions.** Elevators/phase machine, exit button →
   next scenario. `ProgressManager` save/load to `user://`.
10. **Port remaining devices** (Splitter, Merger, AmpAlien, pedestals, dispensers,
    doors variants) and the **indicator/VFX** layer. Re-author shaders.
11. **Migrate all levels** to LDTK (Option A from `03`, plus the scene-ripper for
    any stale/missing ones), polish camera, audio, lighting presets.

---

## 4. Things that will bite you (watch-outs)

- **Init order.** The game depends on the explicit `Init → ActivateOnGrid →
  AfterGridPopulated` passes across *all* bodies. Godot's `_ready` runs
  child-first and per-node — **do the passes manually from the level loader.**
- **Y-axis / handedness.** Unity is left-handed, Y-up; Godot 3D is right-handed,
  Y-up. The grid's X/Z survive, but rotations and `forward` (`transform.forward`
  in `EmitterAlien`) need checking. Centralise the LDTK↔world mapping (and the
  z-flip) in one function.
- **Per-frame raycasting.** `LaserBeam.Update` re-raycasts every frame for every
  beam (the code says it's "trash"). In the port, recompute beams **event-driven**
  (on device place/rotate/remove) — cleaner and faster.
- **Legacy/commented code.** `LevelManager.LoadSaveData`, `SaveData`, and
  `LevelEditor.GenerateSaveFromScene` are commented out. Don't port the dead
  body-serialization path — the LDTK loader replaces it.
- **Giant scenes are décor.** Don't try to convert the `.unity` files wholesale.
  Re-import the *meshes/materials* you need (via the `.blend`/FBX sources in
  `blendfiles/` and `Assets/Models`) and rebuild scenes around LDTK-driven layout.
- **Static C# events** (`EmergeTile.OnTileChanged`) — fine in C#, but if you mix
  GDScript, route them through the autoload event bus instead.

---

## 5. Asset sources you already have

- **`blendfiles/`** (repo root) and **`Assets/Models`** — original 3D source art;
  re-export to glTF for Godot.
- **`Assets/Stories/*.ink` + `*.json`** — narrative content, reusable as-is with
  inkgd.
- **`Assets/StreamingAssets/*/grid_data.json`** — level grids (already extracted).
- **`Assets/Audio`, `Assets/VFX`, `Assets/Materials`** — audio reusable directly;
  VFX/materials/shaders need re-authoring in Godot.
- **`tools/level_export/`** — the clean, engine-agnostic level data this analysis
  produced; the input to your LDTK pipeline.
