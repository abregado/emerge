# Godot 4.7 Port Guide — Unity → Godot

Target game: **2.5D top-down narrative puzzle game with controller support.**
This is a condensed map + best-practices digest. You have web access while using it — use the "Where to look" links to fetch full detail on demand.

Docs root: `https://docs.godotengine.org/en/stable/` (4.x = current stable).

---

## Best Practices digest (the index page, summarized)

- **OOP in Godot:** A scene *is* a class; nodes compose behavior. Prefer composition (child nodes) over deep inheritance. Scripts extend a node type.
- **Scene organization:** Build self-contained scenes with minimal external dependencies. **Signals go up, method calls go down** — children emit past-tense signals (`item_collected`), parents call child methods to drive them. Root structure: a `Main` node → `World` + `GUI` children.
- **Scenes vs scripts:** Use **scenes** for game-specific concepts (easier to edit, batched/cached via `PackedScene`, better perf). Use **scripts with `class_name`** for reusable tools and to register named types in the editor. A script can hold a scene as a `const` to act as a namespace.
- **Autoloads (singletons) vs nodes:** Reserve autoloads for *globally accessible, isolated* systems (save manager, audio bus, event bus, game state). Don't make everything an autoload — prefer scoped regular nodes when a system belongs to one scene.
- **Avoid nodes for everything:** For pure data/logic with no scene-tree needs, use a custom `Resource` or plain `RefCounted`/Object instead of a `Node` (lighter, no per-frame cost).
- **Interfaces:** Godot has no `interface` keyword. Approximate with: duck typing + `has_method()`, scripts with `class_name`, or `Resource` subtypes. Use `Object.has_signal()`/`has_method()` for capability checks.
- **Notifications:** Override `_notification(what)` for lifecycle hooks (`NOTIFICATION_READY`, `_PREDELETE`, `_WM_CLOSE_REQUEST`, pause, etc.) when the dedicated `_ready/_process` virtuals aren't enough.
- **Data preferences:** Prefer arrays/dictionaries and `Resource` files for structured data; export `Resource` fields for designer-editable data instead of hardcoding.
- **Logic preferences:** Keep imperative glue in scripts; push reusable structure into scenes/resources.
- **Project organization:** `snake_case` folders/files (PascalCase for C# files & node names). Group assets close to the scenes that use them; third-party in top-level `addons/`. Drop an empty `.gdignore` in folders Godot should skip. Stay lowercase to dodge case-sensitivity export bugs.
- **Version control:** Git. Commit `project.godot`, scenes, scripts, and `.import` files; the engine regenerates the `.godot/` (or `.import/`) cache — gitignore it. There's an official `.gitignore`/`.gitattributes` in the docs.

---

## Where to look (by porting topic)

| Need | Doc path under `tutorials/` |
|---|---|
| **Unity→Godot mental model** | `migrating/migrating_to_godot_from_unity.html` (terminology, GameObject→Node, prefab→scene) |
| **C# vs GDScript** | `scripting/c_sharp/` — C# is supported (.NET build); GDScript is the default/most-documented. Decide early. |
| **2.5D rendering** (2D look, 3D depth/sorting) | `3d/using_transparency.html`, `2d/2d_sprite_animation.html`, and search "2.5D" — common approach: 3D scene with orthographic/angled `Camera3D` + billboarded sprites, OR 2D with `y_sort`. |
| **Top-down 2D movement** | `2d/` + `physics/` — `CharacterBody2D`, `move_and_slide()`. For 2.5D in 3D: `CharacterBody3D`. |
| **Y-sorting / draw order** | 2D: `CanvasItem` `y_sort_enabled`. 3D depth handles itself. |
| **Controller / gamepad support** | `inputs/controllers_gamepads.html` + `inputs/input_examples.html` + `inputs/inputevent.html`. Define **Input Actions** in Project Settings → Input Map (abstracts keyboard+gamepad). Use `Input.get_vector()` for stick movement, `Input.is_action_pressed()`. |
| **UI controller navigation** | `ui/` — set `focus_neighbor`/focus mode on `Control` nodes so D-pad/stick navigates menus; configure `ui_up/down/left/right/accept`. |
| **Dialogue / narrative** | No built-in system. Use `RichTextLabel` (BBCode) for text; community plugins **Dialogic** or **Dialogue Manager** (AssetLib). Store lines as `Resource` or JSON. |
| **Localization / translations** | `i18n/internationalizing_games.html` (CSV/PO, `tr()`). |
| **Tilemaps (puzzle grids)** | `2d/using_tilemaps.html` — `TileMapLayer` (4.3+ replaces `TileMap`), `TileSet`, custom data layers for puzzle logic. |
| **Saving game state** | `io/saving_games.html` — `FileAccess` + JSON, or `ResourceSaver`/`Resource` for typed save data. Pair with a save-manager autoload. |
| **Signals (events)** | `scripting/gdscript/gdscript_basics.html#signals` — replaces Unity C# events/UnityEvents. |
| **Scene transitions / level loading** | `scripting/singletons_autoload.html`, `io/background_loading.html` (`ResourceLoader.load_threaded_request`). |
| **Audio** | `audio/` — `AudioStreamPlayer`, buses, music vs SFX routing. |
| **Animation / cutscenes** | `animation/` — `AnimationPlayer` (keyframe anything, incl. method calls for narrative beats), `AnimationTree` for state. |
| **Particles / VFX** | `2d/particle_systems_2d.html`, `3d/particles/`. |
| **Shaders** | `shaders/` — Godot shading language (GLSL-like), not ShaderGraph; `VisualShader` exists for node-graph. |
| **Exporting** | `export/` — per-platform; remember `.import` files & PCK case-sensitivity. |
| **Performance/optimization** | `performance/` — `_physics_process` vs `_process`, object pooling, `Resource` over `Node` for data. |

---

## Unity→Godot quick equivalents

- GameObject + Components → **Node** (each node is one "component"; compose by nesting).
- Prefab → **Scene** (`.tscn`, instanced via `PackedScene`).
- MonoBehaviour → **script extending a Node**; `Start()`→`_ready()`, `Update()`→`_process(delta)`, `FixedUpdate()`→`_physics_process(delta)`.
- ScriptableObject → **`Resource`** subclass (`extends Resource`, `class_name`).
- Singleton/Manager → **Autoload**.
- C# events / UnityEvent → **Signals**.
- Input Manager / new Input System → **Input Map actions** + `Input` singleton.
- Coroutines → **`await`** (signals/timers) or `Tween`.
- Rigidbody/Collider → `*Body2D/3D` + `*Shape*` collision nodes; player = `CharacterBody*`.
- Vector3.zero etc. → `Vector2`/`Vector3` (note: 2D Y is **down**).

---

## Decision checklist before coding

1. **GDScript or C#?** (C# eases Unity logic reuse; GDScript has best docs/tooling integration.)
2. **2.5D as true-3D or 2D-with-fake-depth?** Decides camera, sorting, and asset pipeline.
3. **Input Map actions defined first** — wire every action to keyboard *and* gamepad up front; never read raw keys.
4. **Pick a dialogue approach** (plugin vs custom Resource-driven) before building narrative content.
5. **Save format** (`Resource` typed save vs JSON) — affects all persistent puzzle/progress state.
