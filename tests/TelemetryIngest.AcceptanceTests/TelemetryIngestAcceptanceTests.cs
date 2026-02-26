using NUnit.Framework;
using Shared;
using Shared.Ccsds;
using Shared.Messages.Telemetry;

namespace TelemetryIngest.AcceptanceTests;

public class TelemetryIngestAcceptanceTests
{
    private TelemetryIngestFactory _factory = default!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new TelemetryIngestFactory();
        await _factory.InitializeAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
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

        Assert.That(published.VersionNumber, Is.EqualTo(0));
        Assert.That(published.IsCommand, Is.False);
        Assert.That(published.HasSecondaryHeader, Is.False);
        Assert.That(published.Apid, Is.EqualTo(42));
        Assert.That(published.SequenceFlags, Is.EqualTo((byte)SequenceFlag.Unsegmented));
        Assert.That(published.SequenceCount, Is.EqualTo(100));
        Assert.That(published.DataFieldBase64, Is.EqualTo(Convert.ToBase64String(dataField)));
    }

    [Test]
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

        Assert.That(published.IsCommand, Is.True);
        Assert.That(published.HasSecondaryHeader, Is.True);
        Assert.That(published.Apid, Is.EqualTo(200));
        Assert.That(published.SequenceCount, Is.EqualTo(7));
    }

    [Test]
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

        Assert.That(msg1.Apid, Is.EqualTo(10));
        Assert.That(msg1.SequenceCount, Is.EqualTo(1));
        Assert.That(msg2.Apid, Is.EqualTo(20));
        Assert.That(msg2.SequenceCount, Is.EqualTo(2));
    }

    [Test]
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

        // Send a real packet to prove the service is still running
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

        Assert.That(received, Is.False, "Idle packets should not be published to PubSub");
    }

    [Test]
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

        Assert.That(receivedCounts, Has.Count.EqualTo(3));
        Assert.That(receivedCounts.Order().ToList(), Is.EqualTo(new List<ushort> { 0, 1, 2 }));
    }
}
