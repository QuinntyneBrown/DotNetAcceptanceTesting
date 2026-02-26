namespace EventStore.Models;

public class StoredEvent
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public object Payload { get; set; } = default!;
    public DateTime StoredAtUtc { get; set; }
}
