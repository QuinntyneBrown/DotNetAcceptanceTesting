namespace TelemetryIngest.Receivers;

public interface IPacketReceiver
{
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}
