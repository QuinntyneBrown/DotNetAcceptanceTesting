namespace Shared.Messaging;

public class MessageEnvelope<T> where T : class
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string MessageType { get; set; } = typeof(T).Name;
    public T Payload { get; set; } = default!;
    public Dictionary<string, string> Headers { get; set; } = new();
}
