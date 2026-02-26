using System.Text.Json;
using NUnit.Framework;
using Shared;
using Shared.Messages.Events;

namespace EventStore.AcceptanceTests;

public class EventStoreAcceptanceTests
{
    private EventStoreFactory _factory = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [SetUp]
    public async Task SetUp()
    {
        _factory = new EventStoreFactory();
        await _factory.InitializeAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task MissionCreatedEvent_is_stored_with_correct_event_type_and_payload()
    {
        var missionId = Guid.NewGuid();

        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId,
            MissionName = "Artemis IV",
            LaunchSite = "KSC LC-39B",
            ScheduledLaunch = new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Lunar Gateway resupply module"
        });

        await WaitForEventsAsync(1);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].EventType, Is.EqualTo(Channels.MissionCreatedEvent));
        Assert.That(stored[0].Id, Does.StartWith($"{Channels.MissionCreatedEvent}:"));

        var payload = ((JsonElement)stored[0].Payload).Deserialize<MissionCreatedEvent>(JsonOptions);
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.MissionId, Is.EqualTo(missionId));
        Assert.That(payload.MissionName, Is.EqualTo("Artemis IV"));
        Assert.That(payload.LaunchSite, Is.EqualTo("KSC LC-39B"));
        Assert.That(payload.PayloadDescription, Is.EqualTo("Lunar Gateway resupply module"));
    }

    [Test]
    public async Task MissionUpdatedEvent_is_stored_with_correct_payload()
    {
        var missionId = Guid.NewGuid();

        await _factory.PubSub.PublishAsync(Channels.MissionUpdatedEvent, new MissionUpdatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId,
            MissionName = "Artemis IV - Revised",
            LaunchSite = "KSC LC-39A",
            ScheduledLaunch = new DateTime(2027, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Updated payload manifest"
        });

        await WaitForEventsAsync(1);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].EventType, Is.EqualTo(Channels.MissionUpdatedEvent));

        var payload = ((JsonElement)stored[0].Payload).Deserialize<MissionUpdatedEvent>(JsonOptions);
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.MissionId, Is.EqualTo(missionId));
        Assert.That(payload.MissionName, Is.EqualTo("Artemis IV - Revised"));
    }

    [Test]
    public async Task MissionDeletedEvent_is_stored()
    {
        var missionId = Guid.NewGuid();

        await _factory.PubSub.PublishAsync(Channels.MissionDeletedEvent, new MissionDeletedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId
        });

        await WaitForEventsAsync(1);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].EventType, Is.EqualTo(Channels.MissionDeletedEvent));

        var payload = ((JsonElement)stored[0].Payload).Deserialize<MissionDeletedEvent>(JsonOptions);
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.MissionId, Is.EqualTo(missionId));
    }

    [Test]
    public async Task Each_stored_event_receives_a_unique_ID()
    {
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Mission A"
        });

        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Mission B"
        });

        await WaitForEventsAsync(2);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(2));
        Assert.That(stored[0].Id, Is.Not.EqualTo(stored[1].Id));
    }

    [Test]
    public async Task Events_from_all_channels_are_stored()
    {
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "New Mission"
        });

        await _factory.PubSub.PublishAsync(Channels.MissionUpdatedEvent, new MissionUpdatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Updated Mission"
        });

        await _factory.PubSub.PublishAsync(Channels.MissionDeletedEvent, new MissionDeletedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid()
        });

        await WaitForEventsAsync(3);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(3));
        Assert.That(stored.Select(e => e.EventType).Distinct().Count(), Is.EqualTo(3));
        Assert.That(stored, Has.Some.Matches<EventStore.Models.StoredEvent>(
            e => e.EventType == Channels.MissionCreatedEvent));
        Assert.That(stored, Has.Some.Matches<EventStore.Models.StoredEvent>(
            e => e.EventType == Channels.MissionUpdatedEvent));
        Assert.That(stored, Has.Some.Matches<EventStore.Models.StoredEvent>(
            e => e.EventType == Channels.MissionDeletedEvent));
    }

    [Test]
    public async Task Command_messages_are_not_stored()
    {
        // Publish a command message — the EventStore should NOT be subscribed to command channels
        await _factory.PubSub.PublishAsync(Channels.CreateMissionCommand, new
        {
            CorrelationId = Guid.NewGuid(),
            MissionName = "Should Not Be Stored"
        });

        // Publish a real event to prove the service is running
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Real Event"
        });

        await WaitForEventsAsync(1);

        var stored = _factory.EventRepository.StoredEvents;
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].EventType, Is.EqualTo(Channels.MissionCreatedEvent));
    }

    private async Task WaitForEventsAsync(int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_factory.EventRepository.StoredEvents.Count < expectedCount
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
