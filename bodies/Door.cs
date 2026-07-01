using Godot;
using System;

public partial class Door : SurfaceBody, ITriggerable
{
    [Export] public string[] TriggerGroups = Array.Empty<string>();
    [Export] public int TriggerWeightNeeded = 1;
    [Export] public bool ReverseState = false;

    private CollisionShape3D _collision;
    private Node3D _view;
    private bool _isOpen;

    public override void _Ready()
    {
        base._Ready();
        _view = new Node3D { Name = "View" };
        _view.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 2f, 0.2f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.3f, 0.1f) },
            Position = new Vector3(0, 1f, 0),
        });
        AddChild(_view);
    }

    public override void Init(Grid<EmergeTile> grid)
    {
        base.Init(grid);
        _collision = GetNode<CollisionShape3D>("CollisionShape3D");
    }

    public override void AfterGridPopulated()
    {
        foreach (var g in TriggerGroups)
            if (!string.IsNullOrEmpty(g))
                TriggerBus.Instance.RegisterConsumer(g, this);
        SetOpen(ReverseState);
    }

    public void ReceiveSignal(int strength)
    {
        bool shouldOpen = (strength >= TriggerWeightNeeded) != ReverseState;
        if (shouldOpen == _isOpen) return;
        SetOpen(shouldOpen);
    }

    private void SetOpen(bool open)
    {
        _isOpen = open;
        if (_collision != null) _collision.Disabled = open;
        OccupiedTile?.SetWalkable(open);
        if (_view != null) _view.Visible = !open;

        if (OccupiedTile != null)
            EventBus.Instance?.EmitSignal(EventBus.SignalName.TileChanged, OccupiedTile.X, OccupiedTile.Z);

        GD.Print($"[Door {Name}] open={open}");
    }
}
