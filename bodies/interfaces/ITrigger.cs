public interface ITrigger
{
    string GroupId { get; }
    int TriggerWeight { get; }
    bool IsTriggered { get; }
}
