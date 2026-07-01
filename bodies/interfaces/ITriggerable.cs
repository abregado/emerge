// Re-export TriggerBus.ITriggerable at top level for convenience.
// Any class implementing ITriggerable also satisfies TriggerBus.RegisterConsumer.
public interface ITriggerable : TriggerBus.ITriggerable { }
