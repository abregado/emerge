using Godot;

public partial class Hoverable : Node
{
    private Machine _owner;
    private Player _porter;
    private float _bobTime;
    private const float BobSpeed = 2f;
    private const float BobHeight = 0.15f;
    private const float CarryHeight = 1.0f;

    public bool IsBeingCarried => _porter != null;

    public override void _Ready() => _owner = GetParent<Machine>();

    public void BeginHover(Player porter)
    {
        _porter = porter;
        _owner.DetachFromTile();
        _bobTime = 0f;
    }

    public void EndHover(EmergeTile targetTile)
    {
        _porter = null;
        _owner.GlobalPosition = _owner.Grid.GetWorldCenter(targetTile.X, targetTile.Z);
        _owner.PlaceBody();
        GD.Print($"[Hoverable] placed on tile ({targetTile.X}, {targetTile.Z})");
        RecomputeNearbyBeams();
    }

    public void Rotate90()
    {
        _owner.RotateY(Mathf.Pi / 2f);
        if (_owner is IRotatable r) r.OnRotated();
        RecomputeNearbyBeams();
    }

    public override void _Process(double delta)
    {
        if (_porter == null) return;

        var porterPos = _porter.GlobalPosition;
        _owner.GlobalPosition = new Vector3(porterPos.X, CarryHeight, porterPos.Z);

        _bobTime += (float)delta * BobSpeed;
        var view = _owner.GetNodeOrNull<Node3D>("View");
        if (view != null)
            view.Position = new Vector3(0, Mathf.Sin(_bobTime) * BobHeight, 0);
    }

    private void RecomputeNearbyBeams()
    {
        foreach (var node in _owner.GetTree().GetNodesInGroup("active_beams"))
            (node as LaserBeam)?.Recompute();
    }
}
