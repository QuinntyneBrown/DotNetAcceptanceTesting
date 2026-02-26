namespace Shared.Messages.Commands;

public class CreateMissionCommand
{
    public Guid CorrelationId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string LaunchSite { get; set; } = string.Empty;
    public DateTime ScheduledLaunch { get; set; }
    public string PayloadDescription { get; set; } = string.Empty;
}
