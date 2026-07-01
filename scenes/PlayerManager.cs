using Godot;

public partial class PlayerManager : Node3D
{
    [Export] private PackedScene PlayerScene;

    private Player _player;

    public void Init(Grid<EmergeTile> grid, Vector3 spawnPos)
    {
        _player = PlayerScene.Instantiate<Player>();
        GetNode<Node3D>("/root/Main/World/Players").AddChild(_player);
        _player.GlobalPosition = spawnPos;
        _player.Init(grid);
        GetNode<PlayerCamera>("/root/Main/World/PlayerCamera").SetTarget(_player);
        GD.Print($"Player spawned at {spawnPos}");
    }

    public void SetControllable(bool v) => _player?.SetControllable(v);
}
