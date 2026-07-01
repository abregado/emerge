using Godot;

public partial class PlayerCamera : Camera3D
{
    [Export] private float Height = 18f;
    [Export] private float LerpSpeed = 5f;
    [Export] private float FovDeg = 60f;

    private Node3D _target;

    public void SetTarget(Node3D target) => _target = target;

    public override void _Ready()
    {
        Projection = ProjectionType.Perspective;
        Fov = FovDeg;
        RotationDegrees = new Vector3(-60f, 0f, 0f);
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (_target == null) return;
        var desired = _target.GlobalPosition + new Vector3(0, Height, Height * 0.5f);
        GlobalPosition = GlobalPosition.Lerp(desired, LerpSpeed * (float)delta);
    }
}
