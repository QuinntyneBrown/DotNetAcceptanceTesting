namespace Shared;

/// <summary>
/// PubSub channel names defined in the Interface Control Document.
/// All microservices reference these constants for channel routing.
/// </summary>
public static class Channels
{
    public const string CreateMissionCommand = nameof(CreateMissionCommand);
    public const string UpdateMissionCommand = nameof(UpdateMissionCommand);
    public const string DeleteMissionCommand = nameof(DeleteMissionCommand);

    public const string GetMissionQuery = nameof(GetMissionQuery);
    public const string GetMissionQueryResponse = nameof(GetMissionQueryResponse);
    public const string GetMissionListQuery = nameof(GetMissionListQuery);
    public const string GetMissionListQueryResponse = nameof(GetMissionListQueryResponse);

    public const string MissionCreatedEvent = nameof(MissionCreatedEvent);
    public const string MissionUpdatedEvent = nameof(MissionUpdatedEvent);
    public const string MissionDeletedEvent = nameof(MissionDeletedEvent);

    public static readonly string[] AllEventChannels =
    [
        MissionCreatedEvent,
        MissionUpdatedEvent,
        MissionDeletedEvent
    ];

    public const string TelemetryPrefix = "Telemetry";

    public static string TelemetryForApid(ushort apid) => $"{TelemetryPrefix}.{apid}";
}
