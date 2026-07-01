public class EmergeTile
{
    public int X { get; }
    public int Z { get; }
    public G.BakedTile BakedTile { get; private set; }
    public bool IsWalkable { get; private set; }
    public bool IsPlaceable { get; private set; }

    public Body FloorBody { get; private set; }
    public Body SurfaceBody { get; private set; }

    public EmergeTile(int x, int z) { X = x; Z = z; }

    public void SetBakedTile(G.BakedTile type)
    {
        BakedTile = type;
        IsWalkable = type is G.BakedTile.Sand or G.BakedTile.Placeable_Static
                          or G.BakedTile.Water or G.BakedTile.Walkable_Static
                          or G.BakedTile.Plateable_Static or G.BakedTile.RaycastStaticPlaceable
                          or G.BakedTile.Null;
        IsPlaceable = type is G.BakedTile.Placeable_Static or G.BakedTile.Plateable_Static
                           or G.BakedTile.RaycastStaticPlaceable;
        EventBus.Instance?.EmitSignal(EventBus.SignalName.TileChanged, X, Z);
    }

    public void SetWalkable(bool value)
    {
        IsWalkable = value;
        EventBus.Instance?.EmitSignal(EventBus.SignalName.TileChanged, X, Z);
    }

    public void SetSurfaceBody(Body b) { SurfaceBody = b; }
    public void SetFloorBody(Body b) { FloorBody = b; }
    public void ClearSurfaceBody() { SurfaceBody = null; }
    public void ClearFloorBody() { FloorBody = null; }
}
