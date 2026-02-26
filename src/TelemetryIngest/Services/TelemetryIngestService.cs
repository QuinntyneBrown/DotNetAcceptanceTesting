using Shared;
using Shared.Ccsds;
using Shared.Messages.Telemetry;
using Shared.Messaging;
using TelemetryIngest.Receivers;

namespace TelemetryIngest.Services;

public class TelemetryIngestService : BackgroundService
{
    private readonly IPacketReceiver _receiver;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<TelemetryIngestService> _logger;

    public TelemetryIngestService(
        IPacketReceiver receiver,
        IMessagePublisher publisher,
        ILogger<TelemetryIngestService> logger)
    {
        _receiver = receiver;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryIngestService started, listening for CCSDS packets");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var rawPacket = await _receiver.ReceiveAsync(stoppingToken);

                var packet = CcsdsSpacePacket.Parse(rawPacket);

                if (packet.IsIdle)
                {
                    _logger.LogDebug("Idle packet received, skipping");
                    continue;
                }

                var message = new CcsdsTelemetryMessage
                {
                    VersionNumber = packet.VersionNumber,
                    IsCommand = packet.IsCommand,
                    HasSecondaryHeader = packet.HasSecondaryHeader,
                    Apid = packet.Apid,
                    SequenceFlags = (byte)packet.SequenceFlags,
                    SequenceCount = packet.SequenceCount,
                    DataFieldBase64 = Convert.ToBase64String(packet.GetDataFieldArray()),
                    ReceivedAtUtc = DateTime.UtcNow
                };

                var channel = Channels.TelemetryForApid(packet.Apid);

                _logger.LogInformation(
                    "Publishing CCSDS packet APID={Apid} Seq={SequenceCount} to {Channel}",
                    packet.Apid, packet.SequenceCount, channel);

                await _publisher.PublishAsync(channel, message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CCSDS packet");
            }
        }

        _logger.LogInformation("TelemetryIngestService stopped");
    }
}
