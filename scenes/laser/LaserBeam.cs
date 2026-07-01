using Godot;

public partial class LaserBeam : Node3D
{
    public int Intensity { get; private set; }
    public Vector3 Origin { get; private set; }
    public Vector3 Direction { get; private set; }

    private ILaserHandler _currentTarget;
    private MeshInstance3D _beamMesh;
    private static readonly uint LaserMask = 2; // layer 2: laser-hittable bodies only

    public override void _Ready()
    {
        _beamMesh = GetNode<MeshInstance3D>("MeshInstance3D");
        Visible = false;
    }

    public void Activate(int intensity, Vector3 origin, Vector3 direction)
    {
        Intensity = intensity;
        Origin = origin;
        Direction = direction;
        _beamMesh.MaterialOverride = MakeMaterial(intensity);
        Visible = true;
        AddToGroup("active_beams");
        Recompute();
    }

    public void Deactivate()
    {
        Visible = false;
        _currentTarget?.RemoveBeam(this);
        _currentTarget = null;
        RemoveFromGroup("active_beams");
    }

    public void Recompute()
    {
        _currentTarget?.RemoveBeam(this);
        _currentTarget = null;

        var query = PhysicsRayQueryParameters3D.Create(
            Origin, Origin + Direction * 100f, LaserMask);
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

        Vector3 endPoint = Origin + Direction * 100f;

        if (result.Count > 0)
        {
            var collider = result["collider"].As<Node>();
            var handler = collider?.GetParentOrNull<ILaserHandler>()
                       ?? collider as ILaserHandler;

            if (handler != null && handler.IsLaserInteractable)
            {
                var hitPoint = result["position"].As<Vector3>();
                if (handler.TryAddBeam(this, hitPoint))
                {
                    _currentTarget = handler;
                    endPoint = handler.GetEndPoint(hitPoint);
                }
            }
        }

        DrawBeam(Origin, endPoint);
    }

    private void DrawBeam(Vector3 from, Vector3 to)
    {
        var mid = (from + to) * 0.5f;
        var length = from.DistanceTo(to);
        GlobalPosition = mid;
        LookAt(to, Vector3.Up);
        Scale = new Vector3(0.05f, 0.05f, length);
    }

    private static StandardMaterial3D MakeMaterial(int intensity)
    {
        var color = intensity switch
        {
            1 => new Color(1f, 0.1f, 0.1f),
            2 => new Color(1f, 0.5f, 0.1f),
            _ => Colors.White,
        };
        return new StandardMaterial3D
        {
            EmissionEnabled = true,
            Emission = color,
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }
}
