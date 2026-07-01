using Godot;
using System;
using System.Collections.Generic;

public partial class TimeManager : Node
{
    public static TimeManager Instance { get; private set; }

    private float _accumulator;
    private int _tickNumber;

    private record Occurrence(Action Callback, int DueTick, string SenderId);
    private List<Occurrence> _scheduled = new();

    public override void _Ready() => Instance = this;

    public override void _Process(double delta)
    {
        _accumulator += (float)delta;
        if (_accumulator >= G.TICK_TIME)
        {
            _accumulator -= G.TICK_TIME;
            _tickNumber++;
            FireDue();
            EventBus.Instance.EmitSignal(EventBus.SignalName.Tick, _tickNumber);
        }
    }

    public void Schedule(Action callback, float delaySeconds, string senderId)
    {
        int dueTick = _tickNumber + Mathf.CeilToInt(delaySeconds / G.TICK_TIME);
        _scheduled.Add(new Occurrence(callback, dueTick, senderId));
    }

    public void CancelAll(string senderId) =>
        _scheduled.RemoveAll(o => o.SenderId == senderId);

    private void FireDue()
    {
        var due = _scheduled.FindAll(o => o.DueTick <= _tickNumber);
        _scheduled.RemoveAll(o => o.DueTick <= _tickNumber);
        foreach (var o in due) o.Callback();
    }
}
