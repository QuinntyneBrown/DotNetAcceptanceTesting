namespace Shared.Messages.Commands;

public class DeleteMissionCommand
{
    public Guid CorrelationId { get; set; }
    public Guid MissionId { get; set; }
}
