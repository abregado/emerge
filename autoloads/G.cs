public static class G
{
    public const float TICK_TIME = 0.2f;
    public const float PLAYER_SPEED = 4f;
    public const float PLAYER_TURN_SPEED = 10f;

    public enum Scenarios
    {
        Entry, HubWorld, FirstMachine, EasyAmp, Accelerator,
        LongMachine, MessScene, Vaults, DesertEntrance,
        BuildingScene_1, LastChoice, Credits
    }

    public enum BakedTile
    {
        Null = 0, Sand = 1, Block = 2, Placeable_Static = 3,
        Water = 4, Walkable_Static = 5, Plateable_Static = 6,
        RaycastStaticBlock = 7, RaycastStaticPlaceable = 8
    }

    public enum SurfaceBodyType
    {
        Door, LaserTarget, EmitterAlien, Reflector, Splitter,
        Merger, AmpAlien, ItemOrb, SecurityDoor, KeyDevice,
        TriggerIntermediate, ChargedIntermediate, EndLevelButton
    }

    public enum ItemType { None, CrystalFlat, CrystalPyramid, CrystalCube, Wand, EmitterRing }
    public enum GridDir { N, NE, E, SE, S, SW, W, NW }
    public enum LaserDir { N, E, S, W }
    public enum LogLevel { None, Error, Warn, Info, Debug }

    public static LogLevel CurrentLogLevel = LogLevel.Warn;
}
