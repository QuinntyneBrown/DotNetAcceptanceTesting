namespace Shared.Messages.Queries;

public class GetMissionListQueryResponse
{
    public Guid CorrelationId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MissionListItem> Missions { get; set; } = new();
}

public class MissionListItem
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string LaunchSite { get; set; } = string.Empty;
    public DateTime ScheduledLaunch { get; set; }
}
