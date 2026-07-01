using Godot;

public enum MachineState { Idle, Hovering, Working }

public abstract partial class Machine : SurfaceBody
{
    public MachineState State { get; private set; } = MachineState.Idle;

    protected virtual void OnEnterIdle() {}
    protected virtual void OnEnterHovering() {}
    protected virtual void OnEnterWorking() {}
    protected virtual void OnExitIdle() {}
    protected virtual void OnExitHovering() {}
    protected virtual void OnExitWorking() {}

    public void SwitchState(MachineState next)
    {
        switch (State) {
            case MachineState.Idle:     OnExitIdle();     break;
            case MachineState.Hovering: OnExitHovering(); break;
            case MachineState.Working:  OnExitWorking();  break;
        }
        State = next;
        switch (State) {
            case MachineState.Idle:     OnEnterIdle();     break;
            case MachineState.Hovering: OnEnterHovering(); break;
            case MachineState.Working:  OnEnterWorking();  break;
        }
    }

    public virtual void OnInteract(Player player)
    {
        var hoverable = GetNodeOrNull<Hoverable>("Hoverable");
        if (hoverable == null) return;

        if (State == MachineState.Idle && !hoverable.IsBeingCarried)
        {
            player.BeginCarry(this, hoverable);
            SwitchState(MachineState.Hovering);
        }
        else if (State == MachineState.Hovering && hoverable.IsBeingCarried)
        {
            var targetTile = Grid.Get(player.GlobalPosition);
            if (targetTile != null && targetTile.IsPlaceable && targetTile.SurfaceBody == null)
            {
                player.EndCarry();
                hoverable.EndHover(targetTile);
                SwitchState(MachineState.Idle);
            }
        }
    }

    public virtual void OnCancel(Player player)
    {
        var hoverable = GetNodeOrNull<Hoverable>("Hoverable");
        if (State == MachineState.Hovering && hoverable != null)
            hoverable.Rotate90();
    }
}
