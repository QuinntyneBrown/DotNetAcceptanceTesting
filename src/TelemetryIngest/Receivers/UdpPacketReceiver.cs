using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace TelemetryIngest.Receivers;

public class UdpPacketReceiver : IPacketReceiver, IDisposable
{
    private readonly UdpClient _udpClient;

    public UdpPacketReceiver(IOptions<UdpReceiverOptions> options)
    {
        var settings = options.Value;
        _udpClient = new UdpClient(settings.Port);
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        var result = await _udpClient.ReceiveAsync(cancellationToken);
        return result.Buffer;
    }

    public void Dispose()
    {
        _udpClient.Dispose();
    }
}
