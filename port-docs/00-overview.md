# Emerge — Codebase Overview (for the Godot 4.7 port)

> You wrote this game a while ago and have forgotten the details. This document
> set re-explains how it works, in plain language, so you can rebuild it in
> Godot. Start here, then read the numbered files in order.

## What the game is

A **2.5D, top-down, grid-based puzzle game with a narrative layer and
controller support.** You play characters that walk around a desert/facility,
**pick up and rotate "machines/devices" on a tile grid**, and route **laser
beams** through reflectors/splitters/amplifiers into targets. Solving a puzzle
flips **triggers**, which send **signals** down **cables** to open **doors** and
power things. Between and during puzzles, **cutscenes and branching dialogue**
(written in **Ink**) drive the story.

It's built in **Unity (URP)**, C#, and leans on a lot of third-party asset-store
packages. The whole game is rendered with real 3D meshes but played from a
fixed, mostly top-down camera — hence "2.5D".

## The mental model in one paragraph

Everything placed in the world is a **`Body`** sitting on a **grid of tiles**
(`EmergeTile`). A central **`EmergeSceneManager`** owns a set of **manager**
objects (level, players, camera, time, audio, events). On scene load the
`LevelManager` loads a baked **tile grid** from a JSON file, then walks the Unity
scene hierarchy and **initialises every `Body` and editor entity in a fixed
multi-pass order** (Init → ActivateOnGrid → AfterGridPopulated). Gameplay then
runs on a **0.2s tick** scheduler plus normal per-frame `Update()`s. Puzzle logic
is wired through a **trigger/signal bus** (`TriggerHandler`) and a global
**`EventManager`**. Story beats are played by the **`DialogManager`** reading
**Ink** stories and issuing tagged commands (move actor, swap camera, etc.).

## The big subsystems (and where they live)

| Subsystem | What it does | Key scripts |
|---|---|---|
| **Scene/Managers** | Top-level wiring & lifecycle | `Manager/EmergeSceneManager.cs`, `Manager/LevelManager.cs` |
| **Grid & Tiles** | The logical playfield | `Level/Grid/Grid.cs`, `Level/Grid/EmergeTile.cs` |
| **Bodies** | Anything on the grid (machines, devices, doors…) | `Bodies/BaseClasses/*` and subfolders |
| **Lasers** | Beam raycasting, reflection, intensity | `Bodies/LaserBeam.cs`, `Bodies/Devices/*`, `Bodies/Machines/EmitterAlien.cs` |
| **Triggers/Signals** | Puzzle wiring (button→cable→door) | `Manager/TriggerHandler.cs`, `Bodies/Interfaces/ITrigger*.cs`, `UtilComponents/EditorEntity/SignalCable.cs` |
| **Players/Input** | Movement, carrying, interacting | `Player/BasePlayer.cs`, `Player/LocalPlayer.cs` (Rewired) |
| **Narrative** | Cutscenes + branching dialogue | `Cutscene/DialogManager.cs` + `Cutscene/*`, **Ink** stories in `Assets/Stories/` |
| **Time** | Tick scheduler / delayed callbacks | `Manager/TimeManager.cs` |
| **Level authoring** | Editor-time grid baking | `Level/LevelEditor.cs`, `Level/LevelBuilder.cs`, `Manager/IOManager.cs` |
| **Persistence** | Save/load (ES3 JSON) | `Manager/IOManager.cs`, `Manager/ProgressManager.cs` |

## What is *your* code vs third-party

Your game code is almost entirely under **`Assets/Scripts/`** (~214 C# files)
plus the Ink stories in **`Assets/Stories/`** and the baked level data in
**`Assets/StreamingAssets/`**. Everything else under `Assets/` is third-party and
**does not need porting** — find a Godot-native equivalent instead:

- **Rewired** — input/controller manager → Godot **Input Map** actions.
- **Doozy (UIManager/Signals)** — UI screens, transitions, signal bus → Godot
  `Control` UI + autoload event bus.
- **Ink / Ink-Unity** — branching narrative runtime → **inkgd** (Ink for Godot)
  or the **Dialogue Manager** plugin (see `04-godot-port-plan.md`).
- **Cinemachine** — camera rigs/blends → Godot `Camera3D` + `PhantomCamera` addon
  or hand-rolled camera.
- **DOTween** — tweening → Godot built-in `Tween`.
- **Easy Save 3 (ES3)** — JSON serialisation of save data → Godot `FileAccess` +
  `JSON`, or `Resource` save files.
- **CodeMonkey utils, Grabbit, SimplestMeshBaker, TextMesh Pro, Easy Feedback,
  2d-extras** — editor/asset helpers, mostly irrelevant to runtime logic.

## How to read the rest of these docs

1. **`01-architecture.md`** — the object model, lifecycle, grid, and event system. The skeleton.
2. **`02-gameplay-systems.md`** — lasers, triggers, machines/devices, items, players, narrative, time. The flesh.
3. **`03-levels-and-data.md`** — exactly how a level is stored, **what I extracted**, and the **LDTK migration plan**.
4. **`04-godot-port-plan.md`** — a concrete Unity→Godot mapping and a recommended build order, with the decisions you need to make.

There is also a working extractor at **`tools/extract_levels.py`** and its output
under **`tools/level_export/`** (ASCII maps + clean JSON for every level).
