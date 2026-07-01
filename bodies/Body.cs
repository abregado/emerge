using Godot;

public abstract partial class Body : StaticBody3D
{
    public EmergeTile OccupiedTile { get; protected set; }
    public string PrefabId { get; set; }

    public Grid<EmergeTile> Grid { get; protected set; }

    public abstract void Init(Grid<EmergeTile> grid);
    public abstract void ActivateOnGrid();
    public abstract void AfterGridPopulated();
    public abstract void RemoveBody();

    protected void Log(string msg) => GD.Print($"[{Name}] {msg}");

    public void PlaceBody()
    {
        var tile = Grid.Get(GlobalPosition);
        if (tile == null) { GD.PrintErr($"{Name}: no tile at {GlobalPosition}"); return; }
        GlobalPosition = Grid.GetWorldCenter(tile.X, tile.Z);
        OccupiedTile = tile;
        RegisterOnTile(tile);
    }

    public void DetachFromTile()
    {
        OccupiedTile?.ClearSurfaceBody();
        OccupiedTile = null;
    }

    protected virtual void RegisterOnTile(EmergeTile tile) =>
        tile.SetSurfaceBody(this);
}
