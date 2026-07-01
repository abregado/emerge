using Godot;
using System.Collections.Generic;

public partial class Reflector : Machine, ILaserHandler, IRotatable
{
    public bool IsLaserInteractable => true;

    private readonly List<ReflectionPoint> _points = new();
    private readonly Dictionary<LaserBeam, ReflectionPoint> _activePoints = new();

    public override void _Ready()
    {
        base._Ready();
        // Placeholder magenta box so reflector is visible
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.2f, 0.9f) };
        GetNode<Node3D>("View").AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            MaterialOverride = mat,
            Position = new Vector3(0, 0.4f, 0),
        });
    }

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        foreach (Node child in GetChildren())
            if (child is ReflectionPoint rp) { rp.Init(); _points.Add(rp); }
    }

    public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
    {
        var inDir = WorldDirToLaserDir(-beam.Direction);
        var point = _points.Find(p => p.InputDir == inDir);
        if (point == null) return false;

        _activePoints[beam] = point;
        point.ActivateOutput(beam.Intensity, GlobalPosition + Vector3.Up * 0.5f);
        return true;
    }

    public void RemoveBeam(LaserBeam beam)
    {
        if (_activePoints.TryGetValue(beam, out var p))
        {
            p.DeactivateOutput();
            _activePoints.Remove(beam);
        }
    }

    public Vector3 GetEndPoint(Vector3 hitPoint) => hitPoint;

    public void OnRotated()
    {
        var beams = new List<LaserBeam>(_activePoints.Keys);
        foreach (var b in beams) { RemoveBeam(b); b.Recompute(); }
    }

    // Returns the direction the beam came FROM (opposite of travel direction)
    private static G.LaserDir WorldDirToLaserDir(Vector3 d)
    {
        if (Mathf.Abs(d.Z) > Mathf.Abs(d.X))
            return d.Z > 0 ? G.LaserDir.N : G.LaserDir.S;
        return d.X > 0 ? G.LaserDir.E : G.LaserDir.W;
    }
}
