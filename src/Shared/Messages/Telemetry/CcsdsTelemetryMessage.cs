namespace Shared.Messages.Telemetry;

public class CcsdsTelemetryMessage
{
    public byte VersionNumber { get; set; }
    public bool IsCommand { get; set; }
    public bool HasSecondaryHeader { get; set; }
    public ushort Apid { get; set; }
    public byte SequenceFlags { get; set; }
    public ushort SequenceCount { get; set; }
    public string DataFieldBase64 { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
}
