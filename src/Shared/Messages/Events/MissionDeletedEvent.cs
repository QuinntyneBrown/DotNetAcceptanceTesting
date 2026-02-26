namespace Shared.Messages.Events;

public class MissionDeletedEvent
{
    public Guid CorrelationId { get; set; }
    public Guid MissionId { get; set; }
}
