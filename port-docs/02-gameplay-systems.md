# 02 — Gameplay Systems

How the actual puzzle mechanics work. Read `01-architecture.md` first.

---

## 1. Lasers (the core puzzle mechanic)

Lasers are the central toy: a **source** emits a beam, it **raycasts** through the
world, **devices** reflect/split/amplify/merge it, and **targets** detect it.

### The beam — `LaserBeam` (`Bodies/LaserBeam.cs`)

A `LaserBeam` is a component (usually a child of a source/reflector) that:

- holds an **intensity** (1..N), an origin, and a direction;
- on `Activate(intensity, origin, direction)` configures N `LineRenderer`s +
  `Sinewave` wobble using a **`LaserLevelPreset`** (colour gradient, width, noise,
  points-per-unit) pulled from `Res.laserLevelPresets[intensity-1]`;
- every `Update()` (while active) does **`DoRaycast()`**: a `Physics.Raycast`
  along its direction against the laser-blocking layers. On hit it looks for an
  **`ILaserHandler`** on the collider and:
  - if it's a *new* handler, calls `TryAddBeam(beam, hitPoint)`;
  - asks the handler `GetEndPoint(hitPoint)` for where to terminate the visible
    beam (so beams visually stop at the reflection point, not the collider face);
  - registers with `LaserSoundGlobalController` for positional audio.
- `Deactivate()` tears down line renderers and tells the current target
  `RemoveBeam(this)`.

> **Port note:** this re-raycasts **every frame for every beam** (the code itself
> flags it as "trash / probably a perf issue"). In Godot, prefer raycasting only
> when something in the beam path changes (on device move/rotate), or use a
> `RayCast3D` node and react to `is_colliding()` changes. Beams = `Line2D`/3D mesh
> or a stretched quad; the wobble is a vertex/shader effect.

### Laser interfaces (`Bodies/Interfaces/`)

- **`ILaserSource`** — something that originates a beam (`EmitterAlien`). Exposes
  `laserSource`, `intensity`, `origin`, `direction`.
- **`ILaserHandler`** — something a beam can hit and interact with. Key members:
  `bool isLaserInteractable`, `bool TryAddBeam(beam, hitPoint)`,
  `void RemoveBeam(beam)`, `Vector3 GetEndPoint(hitPoint)`.

### Source — `EmitterAlien` (`Bodies/Machines/EmitterAlien.cs`)

A `Machine` + `ILaserSource`. Player toggles it on/off (it enters `Working` and
calls `laserSource.Activate`). It can be **broken** and need a `repairItem`
(`EmitterRing`) carried by the player to fix. Visual state (lights, crystal
material, particles) is themed by the intensity's `LaserLevelPreset`.

### Reflector — `Reflector` (`Bodies/Devices/Reflector.cs`)

A `Machine` + `ILaserHandler` carrying **`ReflectionPoint`** children. Each
reflection point maps an input direction (`dirA`) to an output direction
(`dirB`). When a beam hits, `TryAddBeam` figures out which side it came from
(`LevelUtils.GetHitDirection`), finds the matching point, and **activates that
point's own output `LaserBeam`** in the reflected direction at the same
intensity. Rotating the reflector (cancel button while hovering) re-derives all
connections. **Splitter / Merger / AmpAlien** are variations on this same
"receive beam → emit one or more beams" pattern (split into two, merge two into
one, increase intensity).

### Target — `LaserTarget` (`Bodies/SurfaceBodies/LaserTarget.cs`)

An `ILaserHandler` that, when it receives a beam of sufficient intensity, fires a
trigger/signal (see §2) — this is how "hit the target with the beam" becomes
"open the door".

**Mental flow of a laser puzzle:**
`EmitterAlien` (source) → beam → `Reflector`/`Splitter`/`Amp` (player arranges &
rotates these) → `LaserTarget` → trigger signal → `Door` opens.

---

## 2. Triggers & signals (puzzle wiring)

This is a small **weighted signal bus**, separate from `EventManager`.

### Concepts

- **`ITrigger`** — a *source* of signal (`SetStateOn/Off`, `IsTriggered`).
- **`ITriggerable`** — a *consumer* of signal: `SendSignal(int strength)` and
  `AddGroup(string groupId)`.
- **Trigger groups** — string ids. A trigger contributes a *weight* to its
  group; a triggerable belongs to one or more groups and receives the **summed
  strength** of all its groups.

### `TriggerHandler` (`Manager/TriggerHandler.cs`)

Owns a `Dictionary<string, TriggerGroup>`. API:

```csharp
Register(groupId, ITrigger)        // a source joins a group
Register(groupId, ITriggerable)    // a consumer joins a group
UpdateTriggerSignal(trigger, groupId, weight)   // a source changed -> recompute
```

On any update it sums the group's trigger weights and pushes the total to every
triggerable in the group via `SendSignal(total)`. A triggerable in multiple
groups gets the sum across them.

### Examples

- **`Door`** (`Bodies/SurfaceBodies/Door.cs`) is an `ITriggerable`. It registers
  itself to its `triggerGroups`, and `SendSignal(strength)` opens it when
  `strength >= triggerWeightNeeded` (with a `reverseState` option for
  normally-open doors). Opening a door flips its tile's `isWalkable` and toggles
  the collider.
- **`SignalCable`** (`UtilComponents/EditorEntity/SignalCable.cs`) is an
  `ITriggerable` that just **visualises** signal flow (particles + glowing
  material along a `DOTweenPath`) — it's the on-screen "wire" between a trigger
  and the thing it powers.
- **`TriggerableTarget`** (`Bodies/Components/TriggerableTarget.cs`) auto-adds its
  owner to a group keyed by its own instance id.

**Port note:** this maps to a small autoload (`TriggerBus`) keyed by group id,
emitting a Godot signal `signal_changed(group_id, strength)`, with consumers
connecting to it. Or keep the explicit register/push design verbatim — it's clean.

---

## 3. Machines, devices, items

- **Devices** (`Bodies/Devices/`): `Reflector`, `Splitter`, `Merger`, `AmpAlien`,
  `Pallet`, `KeyDevice`, `MobileItemSpawner`, `AlienPallet`. These are the
  **player-manipulable** pieces (lift, carry, rotate, drop).
- **Machines** (`Bodies/Machines/`): `EmitterAlien`, `SingleItemPedestal`,
  `ItemDispenserTimed(+Triggerable)`, `KeyDeviceDetector`. Often **fixed**
  fixtures that produce/consume items or beams.
- **Items** (`G.ItemType`: `CrystalFlat/Pyramid/Cube/Wand`, `EmitterRing`). Items
  are carried by players in their "hands" (a swapped mesh/material, see §4) and
  consumed by machines. `ItemType` data (mesh/material) lives in `Res.itemTypes`
  (a `ScriptableObject` array indexed by the enum).

### Carrying / moving — `Hoverable` (`Bodies/Components/Hoverable.cs`)

A `Hoverable` (requires a `Machine` + `Rigidbody`) is what makes a device
**liftable**. Interaction flow:

1. Player presses interact near an idle, empty `Machine` with a `Hoverable`.
2. `Machine.SwitchState(Hovering)` → `Hoverable.BeginHoverState()`: the body
   detaches from its grid tile (`RemoveBody`), the rigidbody unfreezes on the
   X/Z plane, and the player becomes the **porter** (the body follows the player).
3. While hovering, `Update()` bobs the `view` up and down.
4. Player presses interact again over a placeable tile → `EndHoverState()`:
   snaps back down, `PlaceBody()` re-registers on the new tile, rigidbody freezes.
5. Cancel button while hovering = **rotate 90°** (which, for a `Reflector`,
   re-routes its beams).

This "lift / carry / rotate / drop on a valid tile" is the **primary verb** of
the game and must feel good in the port.

---

## 4. Players & input

- **`BasePlayer`** (`Player/BasePlayer.cs`) — abstract. Holds `carriedItem`,
  `_playerIndex`, an `Animator`, and helpers to give/take items (swaps the
  "Hands" mesh & material from `Res.itemTypes`), add/remove itself from the camera
  target group, and assign/revoke a carried `Hoverable` follower.
- **`LocalPlayer`** (`Player/LocalPlayer.cs`) — the concrete keyboard/controller
  player. Movement is `CharacterController`-style on the X/Z plane with a turn-
  smooth; interaction probes a small sphere in front of the player for an
  `IInputReceiver`/`Machine`.
- **Input** uses **Rewired** (third-party). Actions are named strings (e.g.
  `"Continue"`, `"TextSkip"`, interact/cancel). `PlayerManager` switches between a
  **UI input mode** and an **in-game input mode** (e.g. during cutscenes input is
  taken away with `SetLocalPlayerControllability(false)`).

**Port note:** replace Rewired with Godot **Input Map actions** wired to keyboard
*and* gamepad up front. Player = `CharacterBody3D` (or `CharacterBody2D` if you go
2D) with `move_and_slide()`; carried-item is a swapped `MeshInstance3D`/sprite.

---

## 5. Narrative: cutscenes + Ink dialogue

The story layer is substantial. Stories are authored in **Ink**
(`Assets/Stories/*.ink`, compiled to `*.json`) and played by:

### `DialogManager` (`Cutscene/DialogManager.cs`)

- Loads a compiled Ink story (`new Story(inkFiles[n].text)`), advances it line by
  line, shows text in a `DialogPanel`, and presents branching `Choice`s in an
  `OptionsPanel`.
- Reads **Ink tags** on each line as **stage directions**, dispatched in
  `ParseTags()`. The tag vocabulary (this is effectively your cutscene scripting
  language):

  | Tag | Effect |
  |---|---|
  | `# speaker X` / `# focus X` | set speaker nameplate/theme (focus also swaps camera) |
  | `# ambient` / `# describe` | narration styles |
  | `# anim X hook` | play an actor animation |
  | `# appear X` / `# exit X` | actor enter/leave |
  | `# goto X dest time` | walk actor to a named `ActorGotoDestination` |
  | `# lookat X target time` / `# actorlook X Y` / `# lookweight X w` / `# clearlook` | gaze control + camera target weights |
  | `# camera X` | switch to a named `CutsceneCamera` |
  | `# action X act` | run a named `ActorAction` on an actor |
  | `# color X` | tint the text |
  | `# trigger` | save story vars & fire `StoryVariableTrigger`s (gameplay hook) |

- Actors/cameras/destinations are **GameObjects in the scene** discovered by name
  at `Init` (`CutsceneActor`, `CutsceneCamera`, `ActorGotoDestination`).
- **Story variables/visits** (Ink globals like `level_2_complete`,
  `two_players`, `john_intro_complete`…) are cached and round-tripped through
  `ProgressManager` so progress persists across scenes.

### Supporting cutscene pieces (`Cutscene/`)

`CutsceneActor` (+ `ActorParts/Actor*` actions: goto, lookat, anim, appear,
bounce-appear), `CutsceneCamera`, `CutscenePanel`, `DialogPanel`, `OptionsPanel`,
`DialogOptionButton`. Editor entities like `StoryVariableSetter` /
`StoryVariableTrigger` / `CutsceneButtonZone` let level objects start cutscenes or
react to story flags.

**Port note:** this is the single biggest "find a plugin" decision. Options:
- **inkgd** — runs your existing compiled Ink `.json` in Godot almost as-is, and
  you re-implement the tag dispatcher (`ParseTags`) in GDScript. **Lowest-rewrite
  path — recommended**, since your content is already in Ink.
- **Dialogue Manager** (Godot plugin) — nicer Godot integration but you'd rewrite
  all narrative content in its format.

---

## 6. Indicators / VFX glue

`UtilComponents/IndicatorParts/` is a large family implementing **`IIndicate`**
(`SetState(bool)` / `SetState(int)`): light fades, material/mesh swaps, particle
bursts, tween moves, text changes, etc. Bodies expose state changes (door open,
emitter working, point reflecting) by driving an `IIndicate`. Think of it as a
uniform, swappable "show this state" adapter. In Godot these become small scripts
toggling visibility/`AnimationPlayer`/`GPUParticles`/`Tween`.

---

## 7. Misc systems worth knowing

- **`LightingController` / `LightingPreset`** — per-scene mood lighting.
- **`ElevatorHandler` / `Elevator`** — players arrive/leave levels in elevators;
  drives round start/end timing.
- **`HighlightHandler`** + `HighlightStatePair[] GetHighlightStates(player)` on
  bodies — context prompts ("Lift", "Drop", "Rotate", "Repair", "Toggle") shown
  near the body the player is looking at, recomputed on
  `OnRequestHighlightUpdate`.
- **`RandomizerTarget*`** (`UtilComponents/`) + `RandomizerEditor` — editor-time
  scatter/rotate/scale randomisation of décor (the recent "randomize level parts"
  commit). Purely cosmetic; **not needed for the port's logic.**
