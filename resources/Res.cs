using Godot;

[GlobalClass]
public partial class Res : Resource
{
    [Export] public Godot.Collections.Dictionary<string, PackedScene> BodyScenes = new();
    [Export] public Godot.Collections.Dictionary<string, string> ScenarioPaths = new();
}
