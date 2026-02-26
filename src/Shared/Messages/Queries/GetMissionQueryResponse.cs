namespace Shared.Messages.Queries;

public class GetMissionQueryResponse
{
    public Guid CorrelationId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public MissionData? Mission { get; set; }
}

public class MissionData
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string LaunchSite { get; set; } = string.Empty;
    public DateTime ScheduledLaunch { get; set; }
    public string PayloadDescription { get; set; } = string.Empty;
}
