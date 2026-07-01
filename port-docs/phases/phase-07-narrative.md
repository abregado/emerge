# Phase 7 — Narrative: inkgd, DialogManager & Cutscene System

**Goal:** Load and play an existing Ink story, display dialogue line-by-line in a
panel, present branching choices, and execute stage-direction tags (move actor,
swap camera, trigger gameplay events). By the end of this phase you can play
through at least one complete conversation/cutscene from the shipped content.

Reads from: `02-gameplay-systems.md §5`, `04-godot-port-plan.md §1 (Dialogue)`.

---

## What to build

### 7.1 Install inkgd

Install the **inkgd** addon via the Godot Asset Library or
`https://github.com/paulloz/godot-ink`. Enable it in `Project → Plugins`.

Confirm: drop a compiled Ink `.json` file (e.g. `Assets/Stories/hub_world.json`
from the Unity project) into `res://stories/` and load it with:

```gdscript
var story = InkStory.new()
story.loads_story_from_path("res://stories/hub_world.json")
```

If this works without errors, inkgd is working.

### 7.2 UI panels

Create under `GUI/DialogLayer` in `Main.tscn`:

**`DialogPanel.tscn`:**
```
DialogPanel (PanelContainer)
├── VBoxContainer
│   ├── SpeakerLabel (Label)    ← speaker name
│   └── TextLabel (RichTextLabel) ← dialogue text, bbcode enabled
└── (hidden until dialogue starts)
```

**`OptionsPanel.tscn`:**
```
OptionsPanel (VBoxContainer)
├── (dynamically populated with OptionButton instances)
└── (hidden until choices present)
```

**`OptionButton.tscn`:** a `Button` with a label, emits `choice_selected(index)`.

All panels start hidden (`visible = false`). `DialogManager` shows/hides them.

### 7.3 `DialogManager.cs`

Create `cutscene/DialogManager.cs`. This owns the inkgd story and drives the
panels. It is a node child of `GUI/DialogLayer`:

```csharp
using Godot;
using System.Collections.Generic;

public partial class DialogManager : Node
{
    [Export] private Control DialogPanel;
    [Export] private Label SpeakerLabel;
    [Export] private RichTextLabel TextLabel;
    [Export] private Control OptionsPanel;
    [Export] private PackedScene OptionButtonScene;

    private GodotObject _story; // inkgd InkStory
    private bool _dialogueActive;

    // Scene-object registries (populated by Init from level scene)
    private Dictionary<string, CutsceneActor> _actors = new();
    private Dictionary<string, Camera3D> _cameras = new();
    private Dictionary<string, Node3D> _destinations = new();

    public void Init()
    {
        // Discover named scene objects
        foreach (var actor in GetTree().GetNodesInGroup("cutscene_actors"))
            if (actor is CutsceneActor ca) _actors[ca.ActorId] = ca;
        foreach (var cam in GetTree().GetNodesInGroup("cutscene_cameras"))
            if (cam is Camera3D c) _cameras[c.Name] = c;
        foreach (var dest in GetTree().GetNodesInGroup("actor_destinations"))
            if (dest is Node3D d) _destinations[d.Name] = d;
    }

    public void PlayStory(string storyPath)
    {
        _story = GD.Load<GodotObject>("res://addons/inkgd/runtime/story.gd")
                    .Call("new").As<GodotObject>();
        _story.Call("loads_story_from_path", storyPath);
        _dialogueActive = true;
        PlayerManager.Instance.SetControllable(false);
        AdvanceStory();
    }

    public void AdvanceStory()
    {
        if (!_dialogueActive) return;
        if (!(bool)_story.Call("can_continue")) { ShowChoices(); return; }

        string line = (string)_story.Call("continue");
        var tags = _story.Call("current_tags").As<string[]>();
        ParseTags(tags);
        ShowLine(line.Trim());
    }

    private void ShowLine(string text)
    {
        DialogPanel.Visible = true;
        OptionsPanel.Visible = false;
        TextLabel.Text = text;
    }

    private void ShowChoices()
    {
        var choices = _story.Call("current_choices").As<string[]>();
        if (choices.Length == 0) { EndStory(); return; }

        OptionsPanel.Visible = true;
        foreach (Node c in OptionsPanel.GetChildren()) c.QueueFree();

        for (int i = 0; i < choices.Length; i++)
        {
            int idx = i;
            var btn = OptionButtonScene.Instantiate<Button>();
            btn.Text = choices[i];
            btn.Pressed += () => SelectChoice(idx);
            OptionsPanel.AddChild(btn);
        }
    }

    private void SelectChoice(int index)
    {
        _story.Call("choose_choice_index", index);
        OptionsPanel.Visible = false;
        AdvanceStory();
    }

    private void EndStory()
    {
        _dialogueActive = false;
        DialogPanel.Visible = false;
        OptionsPanel.Visible = false;
        PlayerManager.Instance.SetControllable(true);
    }

    public override void _Input(InputEvent ev)
    {
        if (_dialogueActive && !OptionsPanel.Visible &&
            ev.IsActionPressed("continue"))
            AdvanceStory();
    }

    // --- Tag dispatcher (port of DialogManager.ParseTags) ---

    private void ParseTags(string[] tags)
    {
        foreach (var tag in tags)
        {
            var parts = tag.Trim().Split(' ');
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "speaker":  SetSpeaker(parts.Length > 1 ? parts[1] : ""); break;
                case "focus":    SetFocus(parts.Length > 1 ? parts[1] : ""); break;
                case "camera":   SetCamera(parts.Length > 1 ? parts[1] : ""); break;
                case "appear":   if (parts.Length > 1) AppearActor(parts[1]); break;
                case "exit":     if (parts.Length > 1) ExitActor(parts[1]); break;
                case "goto":     if (parts.Length > 2) GotoActor(parts[1], parts[2]); break;
                case "anim":     if (parts.Length > 2) AnimActor(parts[1], parts[2]); break;
                case "trigger":  FireStoryTrigger(); break;
                case "color":    if (parts.Length > 1) SetTextColor(parts[1]); break;
                case "ambient":  SetSpeaker(""); break;
                // lookat, actorlook, lookweight, clearlook, action — add as needed
            }
        }
    }

    private void SetSpeaker(string id)
    {
        SpeakerLabel.Text = id;
    }

    private void SetFocus(string actorId)
    {
        SetSpeaker(actorId);
        // camera follows this actor — see SetCamera
    }

    private void SetCamera(string camId)
    {
        if (_cameras.TryGetValue(camId, out var cam))
            cam.MakeCurrent();
    }

    private void AppearActor(string id)
    {
        if (_actors.TryGetValue(id, out var a)) a.Appear();
    }

    private void ExitActor(string id)
    {
        if (_actors.TryGetValue(id, out var a)) a.Exit();
    }

    private void GotoActor(string actorId, string destId)
    {
        if (_actors.TryGetValue(actorId, out var a) &&
            _destinations.TryGetValue(destId, out var d))
            a.GoTo(d.GlobalPosition);
    }

    private void AnimActor(string actorId, string animName)
    {
        if (_actors.TryGetValue(actorId, out var a)) a.PlayAnim(animName);
    }

    private void FireStoryTrigger()
    {
        // Save story variable state to ProgressManager, fire StoryVariableTriggers
        ProgressManager.Instance?.SaveStoryState(_story);
        foreach (var t in GetTree().GetNodesInGroup("story_triggers"))
            (t as StoryVariableTrigger)?.Evaluate(_story);
    }

    private void SetTextColor(string colorName)
    {
        TextLabel.Modulate = colorName switch
        {
            "red" => Colors.Red,
            "blue" => Colors.CornflowerBlue,
            _ => Colors.White
        };
    }
}
```

### 7.4 `CutsceneActor.cs`

A node that can appear, exit, walk to a destination, and play animations:

```csharp
using Godot;

public partial class CutsceneActor : Node3D
{
    [Export] public string ActorId;
    private AnimationPlayer _anim;

    public override void _Ready()
    {
        AddToGroup("cutscene_actors");
        _anim = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
    }

    public void Appear() { Visible = true; PlayAnim("appear"); }
    public void Exit()   { PlayAnim("exit"); }

    public void GoTo(Vector3 worldPos)
    {
        // Simple lerp tween; replace with NavigationAgent3D if pathfinding needed
        var tween = CreateTween();
        tween.TweenProperty(this, "global_position", worldPos, 1.5f)
             .SetEase(Tween.EaseType.InOut);
    }

    public void PlayAnim(string name)
    {
        if (_anim != null && _anim.HasAnimation(name)) _anim.Play(name);
    }
}
```

### 7.5 `StoryVariableTrigger.cs`

An editor-placed node in the level that evaluates an Ink variable and fires a
Godot signal when its value changes:

```csharp
using Godot;

public partial class StoryVariableTrigger : Node
{
    [Export] public string VariableName;
    [Export] public string ExpectedValue = "true";

    [Signal] public delegate void TriggeredEventHandler();

    public void Evaluate(GodotObject story)
    {
        var val = story.Call("fetch_variable", VariableName).ToString();
        if (val == ExpectedValue) EmitSignal(SignalName.Triggered);
    }
}
```

### 7.6 `ProgressManager.cs`

Create `autoloads/ProgressManager.cs`. Add to autoloads. Persists story state:

```csharp
using Godot;
using System.Collections.Generic;
using System.Text.Json;

public partial class ProgressManager : Node
{
    public static ProgressManager Instance { get; private set; }

    private const string SavePath = "user://story_progress.json";
    private Dictionary<string, string> _storyVars = new();
    private string _lastScenario = "";

    public override void _Ready() => Instance = this;

    public void SaveStoryState(GodotObject story)
    {
        // inkgd: iterate variable_state
        // Store as string key/value pairs
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        var data = new { vars = _storyVars, last = _lastScenario };
        f.StoreString(JsonSerializer.Serialize(data));
    }

    public void LoadStoryState(GodotObject story)
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        // Deserialize and push vars back into story
    }

    public void SetLastScenario(string s) => _lastScenario = s;
    public string GetLastScenario() => _lastScenario;
}
```

---

## Testing this phase

### Unit: tag dispatcher

1. Write a unit test (or a debug scene) that:
   - Creates a `DialogManager` node.
   - Calls `ParseTags(new[] { "speaker Ara" })` directly.
   - Asserts `SpeakerLabel.Text == "Ara"`.

   Do the same for `camera`, `appear`, `exit`. No Ink required — test the
   dispatcher in isolation.

### Integration: play a story

2. Copy `Assets/Stories/hub_world.json` (or the simplest `.json` file in the Unity
   project) to `res://stories/`.
3. Call `DialogManager.Instance.PlayStory("res://stories/hub_world.json")` from a
   test button or from `Main._Ready`.
4. Confirm:
   - `DialogPanel` appears.
   - Lines of text advance when you press `continue`.
   - `SpeakerLabel` updates per `# speaker X` tags.
   - Choices appear in `OptionsPanel` when the story branches.
   - Selecting a choice advances to the correct branch.
   - After the last line, panels hide and player regains control.

### Camera tag

5. Add a `Camera3D` node named `"OverheadCam"` to the scene, in group
   `"cutscene_cameras"`. Add a `# camera OverheadCam` tag to a line in the story
   (or test with a hard-coded call). Confirm the camera switches.

### Trigger tag

6. Add a `StoryVariableTrigger` in the scene watching a variable that the test
   story sets at the `# trigger` tag. Confirm the `Triggered` signal fires.

### Player lock-out

7. Start a story. Confirm that while dialogue is active, **player movement stops**
   (calls `PlayerManager.SetControllable(false)`). After the story ends, confirm
   movement resumes.

### Save / load

8. Play a story up to a `# trigger` point. Quit and restart. Confirm `ProgressManager`
   restores the story variable state (Ink visit counts / var values) so the story
   can branch correctly on replay.

### Regression

9. Puzzle mechanics from Phases 4–6 still function (laser, carry, door).
10. Player movement and camera (Phase 3) intact.
