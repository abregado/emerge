using Godot;

public abstract partial class FloorBody : Body
{
    protected override void RegisterOnTile(EmergeTile tile) =>
        tile.SetFloorBody(this);

    public override void Init(Grid<EmergeTile> grid) { Grid = grid; }
    public override void ActivateOnGrid() { PlaceBody(); }
    public override void AfterGridPopulated() { }
    public override void RemoveBody()
    {
        OccupiedTile?.ClearFloorBody();
        QueueFree();
    }
}
