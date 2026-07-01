using Godot;

public interface ILaserSource
{
    int Intensity { get; }
    Vector3 Origin { get; }
    Vector3 Direction { get; }  // unit vector on X/Z plane
}
