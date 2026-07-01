using Godot;

public interface ILaserHandler
{
    bool IsLaserInteractable { get; }
    bool TryAddBeam(LaserBeam beam, Vector3 hitPoint);
    void RemoveBeam(LaserBeam beam);
    Vector3 GetEndPoint(Vector3 hitPoint);
}
