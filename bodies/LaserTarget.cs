using Godot;
using System.Collections.Generic;

public partial class LaserTarget : SurfaceBody, ILaserHandler, ITrigger
{
    [Export] public int RequiredIntensity = 1;
    [Export] public string GroupId { get; set; } = "";

    public bool IsLaserInteractable => true;
    public bool IsTriggered { get; private set; }
    public int TriggerWeight => IsTriggered ? 1 : 0;

    private readonly List<LaserBeam> _beams = new();

    public override void _Ready()
    {
        base._Ready();
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.8f, 0.8f) };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            MaterialOverride = mat,
            Position = new Vector3(0, 0.4f, 0)
        });
    }

    public override void AfterGridPopulated()
    {
        if (!string.IsNullOrEmpty(GroupId))
            TriggerBus.Instance.RegisterSource(GroupId, GetInstanceId().GetHashCode());
    }

    public bool TryAddBeam(LaserBeam beam, Vector3 hitPoint)
    {
        if (beam.Intensity < RequiredIntensity) return false;
        _beams.Add(beam);
        UpdateState();
        return true;
    }

    public void RemoveBeam(LaserBeam beam)
    {
        _beams.Remove(beam);
        UpdateState();
    }

    public Vector3 GetEndPoint(Vector3 hitPoint) => hitPoint;

    private void UpdateState()
    {
        bool hit = _beams.Count > 0;
        if (hit == IsTriggered) return;
        IsTriggered = hit;
        GD.Print($"[{Name}] triggered={IsTriggered}");
        if (!string.IsNullOrEmpty(GroupId))
            TriggerBus.Instance.UpdateSource(GroupId, GetInstanceId().GetHashCode(), TriggerWeight);
    }
}
