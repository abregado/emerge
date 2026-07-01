using Godot;

public abstract partial class SurfaceBody : Body
{
    protected override void RegisterOnTile(EmergeTile tile) =>
        tile.SetSurfaceBody(this);

    public override void Init(Grid<EmergeTile> grid) { Grid = grid; }
    public override void ActivateOnGrid() { PlaceBody(); }
    public override void AfterGridPopulated() { }
    public override void RemoveBody()
    {
        OccupiedTile?.ClearSurfaceBody();
        QueueFree();
    }
}
