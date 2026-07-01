using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public int PlayerIndex = 0;
    [Export] private float Speed = G.PLAYER_SPEED;
    [Export] private float TurnSmooth = G.PLAYER_TURN_SPEED;

    private Grid<EmergeTile> _grid;
    private bool _controllable = true;
    private Vector3 _lastSafePos;

    private Machine _carriedMachine;
    private Hoverable _carriedHoverable;

    public void BeginCarry(Machine machine, Hoverable hoverable)
    {
        _carriedMachine = machine;
        _carriedHoverable = hoverable;
        hoverable.BeginHover(this);
    }

    public void EndCarry()
    {
        _carriedMachine = null;
        _carriedHoverable = null;
    }

    public void Init(Grid<EmergeTile> grid)
    {
        _grid = grid;
        _lastSafePos = GlobalPosition;
    }

    public void SetControllable(bool value) => _controllable = value;

    public override void _Ready()
    {
        // Blue capsule so the player is visible
        var capsule = GetNode<MeshInstance3D>("View/MeshInstance3D");
        capsule.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.4f, 0.9f),
        };

        // Small yellow sphere on the front to show facing direction
        GetNode<Node3D>("View").AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Yellow },
            Position = new Vector3(0, 0.9f, -0.35f),
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_controllable) { Velocity = Vector3.Zero; MoveAndSlide(); return; }

        var input = Input.GetVector("move_west", "move_east", "move_north", "move_south");
        var dir = new Vector3(input.X, 0, input.Y).Normalized();

        Velocity = dir * Speed;
        MoveAndSlide();

        if (dir != Vector3.Zero)
        {
            var targetAngle = Mathf.Atan2(-dir.X, -dir.Z);
            Rotation = new Vector3(0,
                Mathf.LerpAngle(Rotation.Y, targetAngle, TurnSmooth * (float)delta),
                0);
        }

        if (_grid != null)
        {
            var tile = _grid.Get(GlobalPosition);
            if (tile == null || !tile.IsWalkable)
                GlobalPosition = _lastSafePos;
            else
                _lastSafePos = GlobalPosition;
        }

        if (Input.IsActionJustPressed("cancel") && _carriedHoverable != null)
        {
            _carriedHoverable.Rotate90();
        }
        else if (Input.IsActionJustPressed("interact"))
        {
            if (_carriedHoverable != null)
            {
                // Drop: delegate to the carried machine to validate and place
                _carriedMachine.OnInteract(this);
            }
            else
            {
                // Pick up / activate: raycast 1.5 units forward from chest height
                var origin = GlobalPosition + Vector3.Up * 0.9f;
                var fwd = -GlobalTransform.Basis.Z;
                var end = origin + fwd * 1.5f;
                var exclude = new Godot.Collections.Array<Rid>();
                exclude.Add(GetRid());
                var query = PhysicsRayQueryParameters3D.Create(origin, end, 1u, exclude);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                GD.Print($"[Player] interact — hit={hit.Count > 0}");
                if (hit.Count > 0)
                {
                    var collider = hit["collider"].As<GodotObject>();
                    GD.Print($"  collider: {(collider as Node)?.Name} ({collider?.GetType().Name})");
                    if (collider is Machine m) m.OnInteract(this);
                }
            }
        }
    }
}
