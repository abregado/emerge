using Godot;

public partial class Main : Node3D
{
    public static Res Res { get; private set; }
    [Export] public string StartLevel = "HubWorld";
    [Export] private Res _res;

    public override void _Ready()
    {
        Res = _res;
        SetupEnvironment();
        var loader = GetNode<LevelLoader>("World/LevelLoader");
        loader.LoadLevel(StartLevel);
        var spawnPos = loader.Grid.GetWorldCenter(17, 11);
        GetNode<PlayerManager>("PlayerManager").Init(loader.Grid, spawnPos);
    }

    private void SetupEnvironment()
    {
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.12f, 0.12f, 0.12f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = Colors.White;
        env.AmbientLightEnergy = 0.5f;
        AddChild(new WorldEnvironment { Environment = env });
    }
}
