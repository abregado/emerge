using Godot;

public partial class EmitterAlien : Machine, ILaserSource
{
    [Export] public int Intensity { get; private set; } = 1;

    private LaserBeam _beam;

    public Vector3 Origin => GlobalPosition + Vector3.Up * 0.5f;
    public Vector3 Direction => -GlobalTransform.Basis.Z;

    public override void _Ready()
    {
        base._Ready();
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.8f, 0.2f) };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f), Material = mat },
            Position = new Vector3(0, 0.4f, 0)
        });
    }

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        _beam = GD.Load<PackedScene>("res://scenes/laser/LaserBeam.tscn").Instantiate<LaserBeam>();
        AddChild(_beam);
        _beam.Deactivate();
    }

    public override void OnInteract(Player player)
    {
        if (State == MachineState.Working)
            SwitchState(MachineState.Idle);
        else
            SwitchState(MachineState.Working);
    }

    protected override void OnEnterWorking() => _beam.Activate(Intensity, Origin, Direction);
    protected override void OnExitWorking()  => _beam.Deactivate();
}
