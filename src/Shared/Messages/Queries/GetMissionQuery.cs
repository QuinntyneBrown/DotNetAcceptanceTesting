namespace Shared.Messages.Queries;

public class GetMissionQuery
{
    public Guid CorrelationId { get; set; }
    public Guid MissionId { get; set; }
}
