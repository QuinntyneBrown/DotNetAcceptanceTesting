using System.Text.Json;
using Shared;
using Shared.Ccsds;
using Shared.Messages.Telemetry;

namespace TelemetryIngest.AcceptanceTests;

public class TelemetryIngestAcceptanceTests : IDisposable
{
    private readonly TelemetryIngestFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TelemetryIngestAcceptanceTests()
    {
        _factory = new TelemetryIngestFactory();
        // CreateClient boots the host, which starts the BackgroundService
        _factory.CreateClient();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task Telemetry_packet_is_parsed_and_published_to_correct_APID_channel()
    {
        // Arrange — build a CCSDS telemetry packet with APID 42
        ushort apid = 42;
        var dataField = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var rawPacket = CcsdsSpacePacket.BuildPacket(
            version: 0,
            isCommand: false,
            hasSecondaryHeader: false,
            apid: apid,
            sequenceFlags: SequenceFlag.Unsegmented,
            sequenceCount: 100,
            dataField: dataField);

        var messageReceived = new TaskCompletionSource<CcsdsTelemetryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(apid), msg =>
            {
                messageReceived.TrySetResult(msg);
                return Task.CompletedTask;
            });

        // Act — push raw packet into the in-memory receiver
        await _factory.PacketReceiver.Writer.WriteAsync(rawPacket);

        // Assert — verify the published PubSub message
        var published = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, published.VersionNumber);
        Assert.False(published.IsCommand);
        Assert.False(published.HasSecondaryHeader);
        Assert.Equal(42, published.Apid);
        Assert.Equal((byte)SequenceFlag.Unsegmented, published.SequenceFlags);
        Assert.Equal(100, published.SequenceCount);
        Assert.Equal(Convert.ToBase64String(dataField), published.DataFieldBase64);
    }

    [Fact]
    public async Task Telecommand_packet_is_parsed_with_correct_packet_type()
    {
        ushort apid = 200;
        var dataField = new byte[] { 0x01, 0x02, 0x03 };
        var rawPacket = CcsdsSpacePacket.BuildPacket(
            version: 0,
            isCommand: true,
            hasSecondaryHeader: true,
            apid: apid,
            sequenceFlags: SequenceFlag.Unsegmented,
            sequenceCount: 7,
            dataField: dataField);

        var messageReceived = new TaskCompletionSource<CcsdsTelemetryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(apid), msg =>
            {
                messageReceived.TrySetResult(msg);
                return Task.CompletedTask;
            });

        await _factory.PacketReceiver.Writer.WriteAsync(rawPacket);

        var published = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(published.IsCommand);
        Assert.True(published.HasSecondaryHeader);
        Assert.Equal(200, published.Apid);
        Assert.Equal(7, published.SequenceCount);
    }

    [Fact]
    public async Task Different_APIDs_route_to_different_channels()
    {
        ushort apid1 = 10;
        ushort apid2 = 20;

        var received1 = new TaskCompletionSource<CcsdsTelemetryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var received2 = new TaskCompletionSource<CcsdsTelemetryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(apid1), msg =>
            {
                received1.TrySetResult(msg);
                return Task.CompletedTask;
            });

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(apid2), msg =>
            {
                received2.TrySetResult(msg);
                return Task.CompletedTask;
            });

        var packet1 = CcsdsSpacePacket.BuildPacket(0, false, false, apid1,
            SequenceFlag.Unsegmented, 1, [0xAA]);
        var packet2 = CcsdsSpacePacket.BuildPacket(0, false, false, apid2,
            SequenceFlag.Unsegmented, 2, [0xBB]);

        await _factory.PacketReceiver.Writer.WriteAsync(packet1);
        await _factory.PacketReceiver.Writer.WriteAsync(packet2);

        var msg1 = await received1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var msg2 = await received2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(10, msg1.Apid);
        Assert.Equal(1, msg1.SequenceCount);
        Assert.Equal(20, msg2.Apid);
        Assert.Equal(2, msg2.SequenceCount);
    }

    [Fact]
    public async Task Idle_packets_are_not_published()
    {
        var received = false;

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(CcsdsSpacePacket.IdleApid), _ =>
            {
                received = true;
                return Task.CompletedTask;
            });

        // Idle APID = 0x7FF = 2047
        var idlePacket = CcsdsSpacePacket.BuildPacket(0, false, false, CcsdsSpacePacket.IdleApid,
            SequenceFlag.Unsegmented, 0, [0x00]);

        await _factory.PacketReceiver.Writer.WriteAsync(idlePacket);

        // Give the service time to process (and skip) the idle packet,
        // then send a real packet to prove the service is still running
        var realReceived = new TaskCompletionSource<CcsdsTelemetryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(1), msg =>
            {
                realReceived.TrySetResult(msg);
                return Task.CompletedTask;
            });

        var realPacket = CcsdsSpacePacket.BuildPacket(0, false, false, 1,
            SequenceFlag.Unsegmented, 1, [0xFF]);
        await _factory.PacketReceiver.Writer.WriteAsync(realPacket);

        await realReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(received, "Idle packets should not be published to PubSub");
    }

    [Fact]
    public async Task Multiple_packets_are_processed_in_sequence()
    {
        ushort apid = 55;
        var receivedCounts = new List<ushort>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
            Channels.TelemetryForApid(apid), msg =>
            {
                lock (receivedCounts)
                {
                    receivedCounts.Add(msg.SequenceCount);
                    if (receivedCounts.Count == 3)
                        allReceived.TrySetResult();
                }
                return Task.CompletedTask;
            });

        for (ushort seq = 0; seq < 3; seq++)
        {
            var packet = CcsdsSpacePacket.BuildPacket(0, false, false, apid,
                SequenceFlag.Unsegmented, seq, [0x01, 0x02]);
            await _factory.PacketReceiver.Writer.WriteAsync(packet);
        }

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, receivedCounts.Count);
        Assert.Equal([0, 1, 2], receivedCounts.Order().ToList());
    }
}
