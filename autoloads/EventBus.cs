using Godot;

public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; }

    [Signal] public delegate void TickEventHandler(int tickNumber);

    [Signal] public delegate void TileChangedEventHandler(int x, int z);
    [Signal] public delegate void BodyPlacedEventHandler();

    [Signal] public delegate void TickUpdateUIEventHandler();
    [Signal] public delegate void PlayerActivityChangedEventHandler(int playerIndex, bool active);
    [Signal] public delegate void HighlightUpdateRequestedEventHandler();
    [Signal] public delegate void RoundStartEventHandler();
    [Signal] public delegate void RoundEndEventHandler();

    public override void _Ready() => Instance = this;
}
