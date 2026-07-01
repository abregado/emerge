using Godot;

// One face of a reflector: maps an incoming laser direction to an outgoing one.
// InputDir is derived from WorldDirToLaserDir(-beam.Direction) i.e. the direction
// the beam came FROM (opposite of travel direction).
public partial class ReflectionPoint : Node3D
{
    [Export] public G.LaserDir InputDir;
    [Export] public G.LaserDir OutputDir;

    private LaserBeam _outBeam;

    public void Init()
    {
        _outBeam = GD.Load<PackedScene>("res://scenes/laser/LaserBeam.tscn")
                      .Instantiate<LaserBeam>();
        AddChild(_outBeam);
        _outBeam.Deactivate();
    }

    public void ActivateOutput(int intensity, Vector3 origin)
    {
        _outBeam.Activate(intensity, origin, LaserDirToVector(OutputDir));
    }

    public void DeactivateOutput() => _outBeam.Deactivate();

    public static Vector3 LaserDirToVector(G.LaserDir d) => d switch
    {
        G.LaserDir.N => new Vector3(0, 0,  1),
        G.LaserDir.E => new Vector3(1, 0,  0),
        G.LaserDir.S => new Vector3(0, 0, -1),
        G.LaserDir.W => new Vector3(-1, 0, 0),
        _            => Vector3.Zero
    };
}
