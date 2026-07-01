using Godot;
using System.Collections.Generic;

public partial class TriggerBus : Node
{
    public static TriggerBus Instance { get; private set; }

    public interface ITriggerable { void ReceiveSignal(int strength); }

    private class TriggerGroup
    {
        public Dictionary<int, int> Sources = new();
        public List<ITriggerable> Consumers = new();
    }

    private Dictionary<string, TriggerGroup> _groups = new();

    public override void _Ready() => Instance = this;

    public void RegisterSource(string groupId, int instanceId) =>
        GetOrCreate(groupId).Sources.TryAdd(instanceId, 0);

    public void RegisterConsumer(string groupId, ITriggerable consumer) =>
        GetOrCreate(groupId).Consumers.Add(consumer);

    public void UpdateSource(string groupId, int instanceId, int weight)
    {
        var g = GetOrCreate(groupId);
        g.Sources[instanceId] = weight;
        int total = 0;
        foreach (var w in g.Sources.Values) total += w;
        foreach (var c in g.Consumers) c.ReceiveSignal(total);
    }

    private TriggerGroup GetOrCreate(string id)
    {
        if (!_groups.TryGetValue(id, out var g))
            _groups[id] = g = new TriggerGroup();
        return g;
    }
}
