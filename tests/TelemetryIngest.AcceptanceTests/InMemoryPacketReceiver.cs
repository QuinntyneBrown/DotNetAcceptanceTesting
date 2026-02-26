using System.Threading.Channels;
using TelemetryIngest.Receivers;

namespace TelemetryIngest.AcceptanceTests;

public class InMemoryPacketReceiver : IPacketReceiver
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();

    public ChannelWriter<byte[]> Writer => _channel.Writer;

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
