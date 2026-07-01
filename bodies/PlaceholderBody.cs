using Godot;

public partial class PlaceholderBody : SurfaceBody
{
    public override void _Ready()
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.5f, 0.1f) };
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f), Material = mat },
            Position = new Vector3(0, 0.4f, 0)
        };
        AddChild(mesh);
    }
}
