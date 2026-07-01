using Godot;

// Stub — visual particle flow added in Phase 9.
public partial class SignalCable : SurfaceBody, ITriggerable
{
    [Export] public string[] TriggerGroups = System.Array.Empty<string>();

    public override void AfterGridPopulated()
    {
        foreach (var g in TriggerGroups)
            if (!string.IsNullOrEmpty(g))
                TriggerBus.Instance.RegisterConsumer(g, this);
    }

    public void ReceiveSignal(int strength) =>
        GD.Print($"[{Name}] cable signal={strength}");
}
