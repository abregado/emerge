using Godot;

public partial class EmergeNode : Node3D
{
    protected void Log(string msg, G.LogLevel level = G.LogLevel.Info)
    {
        if (level <= G.CurrentLogLevel) GD.Print($"[{Name}] {msg}");
    }
}
