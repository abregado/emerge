using Godot;

// Relay: receives a signal on InputGroup and re-emits on OutputGroup.
// Used for pressure plates and other stepped-on triggers.
public partial class TriggerIntermediate : SurfaceBody, ITrigger, ITriggerable
{
    [Export] public string InputGroup = "";
    [Export] public string OutputGroup = "";

    public string GroupId => OutputGroup;
    public int TriggerWeight => IsTriggered ? 1 : 0;
    public bool IsTriggered { get; private set; }

    public override void AfterGridPopulated()
    {
        if (!string.IsNullOrEmpty(InputGroup))
            TriggerBus.Instance.RegisterConsumer(InputGroup, this);
        if (!string.IsNullOrEmpty(OutputGroup))
            TriggerBus.Instance.RegisterSource(OutputGroup, GetInstanceId().GetHashCode());
    }

    public void ReceiveSignal(int strength)
    {
        bool triggered = strength > 0;
        if (triggered == IsTriggered) return;
        IsTriggered = triggered;
        if (!string.IsNullOrEmpty(OutputGroup))
            TriggerBus.Instance.UpdateSource(OutputGroup, GetInstanceId().GetHashCode(), TriggerWeight);
    }
}
