# Emerge → Godot 4.7 — Phased Port Plan

Ten phases, each delivering a vertical slice of working functionality. Work through
them in order; each phase's testing section verifies the new work and guards
against regressions from earlier phases.

## Phase summary

| Phase | Title | Deliverable | Key test |
|---|---|---|---|
| [01](phase-01-foundation.md) | Foundation | Project compiles; EventBus, TriggerBus, TimeManager autoloads; Res registry; Main scene structure. | Build clean; tick fires at 5 Hz. |
| [02](phase-02-grid-and-level-loading.md) | Grid & Level Loading | `Grid<EmergeTile>` built from LDTK; three-pass body init; tile debug visualiser. | HubWorld grid matches `.map.txt`; bodies on correct tiles; init order verified. |
| [03](phase-03-player-and-camera.md) | Player & Camera | `CharacterBody3D` moves on grid, respects walkability; top-down camera follows. | WASD + gamepad movement; cannot enter wall tiles; camera tracks. |
| [04](phase-04-laser-core.md) | Laser Core | `EmitterAlien` toggles beam; `LaserBeam` raycasts to `LaserTarget`; no per-frame raycast. | Beam appears/disappears; hits target; blocked by walls; profiler shows no per-frame raycast cost. |
| [05](phase-05-carry-and-reflect.md) | Carry & Reflect | `Hoverable` lift/drop/rotate; `Reflector` bends a live beam. | Pick up, rotate, drop on valid tile; beam re-routes on drop; invalid drop rejected. |
| [06](phase-06-triggers-and-doors.md) | Triggers & Doors | `TriggerBus` wired; `LaserTarget → Door` opens a full mini-puzzle. | Laser hits target → door opens; remove laser → door closes; multi-target AND logic. |
| [07](phase-07-narrative.md) | Narrative | inkgd runtime; `DialogManager` with full tag dispatcher; `ProgressManager` saves story vars. | Play a story end-to-end; choices branch; `# camera`, `# appear`, `# goto` execute; player locked during dialogue. |
| [08](phase-08-round-flow-and-persistence.md) | Round Flow & Save | Elevator in/out; round phase machine; `EndLevelButton` → scene transition; last scenario persists across quit. | Elevator descends, player plays, exit button transitions to next level; relaunch resumes correct scenario. |
| [09](phase-09-remaining-devices-and-vfx.md) | Devices & VFX | All remaining body types (Splitter, Merger, AmpAlien, items, security doors); `IIndicate` indicators; laser shader; wall colliders. | Splitter fans two beams; Merger needs two sources; item carry/consume; beam has sinewave wobble glow. |
| [10](phase-10-level-migration-and-polish.md) | Level Migration & Polish | All 15 scenarios in LDTK; real art; audio; lighting presets; 60 fps; full playthrough from Entry to Credits. | All scenario smoke tests pass; full playthrough without crashes; 60 fps on target hardware. |

## Key principles carried throughout

- **Explicit init order** — never rely on Godot `_ready` ordering. Run
  `Init → ActivateOnGrid → AfterGridPopulated` explicitly from `LevelLoader`.
- **Event-driven lasers** — `LaserBeam.Recompute()` fires on device events, not
  every frame.
- **LDTK is the authoring tool** — the `.unity` scene files are source-of-truth
  for art only. Gameplay data lives in LDTK (tile grid + entity placements).
- **C# for logic, GDScript for UI/glue** — port `Body`, `Grid`, `TriggerBus`,
  `TimeManager`, and all gameplay logic in C#. UI panels and editor helpers can
  use GDScript.
- **Test the verb first** — Phase 5 (carry/rotate) is the game's primary
  interaction. Spend extra time making it feel right before widening to all devices.

## Pre-requisites

- `tools/extract_levels.py` running successfully (outputs to `tools/level_export/`).
- Godot 4.7 stable with .NET enabled.
- inkgd addon installed before starting Phase 7.
- ldtk-importer addon installed before starting Phase 2.
- Blender available for glTF export before starting Phase 10.

## Cross-reference

These phase documents pair with:
- `../00-overview.md` — what the game is.
- `../01-architecture.md` — base classes, managers, grid, tick.
- `../02-gameplay-systems.md` — lasers, triggers, carry, narrative, items.
- `../03-levels-and-data.md` — LDTK migration strategy.
- `../04-godot-port-plan.md` — Unity→Godot concept map and watch-outs.
