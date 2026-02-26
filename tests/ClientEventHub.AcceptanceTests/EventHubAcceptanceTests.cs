using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;
using Shared;
using Shared.Messages.Events;

namespace ClientEventHub.AcceptanceTests;

public class EventHubAcceptanceTests
{
    private ClientEventHubFactory _factory = default!;
    private HubConnection _hubConnection = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ClientEventHubFactory();
        var client = _factory.CreateClient();

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{client.BaseAddress}hubs/events", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await _hubConnection.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _hubConnection.DisposeAsync();
        _factory.Dispose();
    }

    [Test]
    public async Task MissionCreatedEvent_is_forwarded_to_subscribed_SignalR_client()
    {
        // Arrange
        var received = new TaskCompletionSource<(string EventType, JsonElement Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (eventType, payload) =>
        {
            received.TrySetResult((eventType, payload));
        });

        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionCreatedEvent);

        var missionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act — simulate a downstream service publishing an event to PubSub
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = correlationId,
            MissionId = missionId,
            MissionName = "Artemis IV",
            LaunchSite = "KSC LC-39B",
            ScheduledLaunch = new DateTime(2027, 3, 15, 14, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Lunar Gateway resupply module"
        });

        // Assert — SignalR client receives the event
        var (eventType, payload) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(eventType, Is.EqualTo(Channels.MissionCreatedEvent));

        var deserialized = payload.Deserialize<MissionCreatedEvent>(JsonOptions);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.MissionId, Is.EqualTo(missionId));
        Assert.That(deserialized.MissionName, Is.EqualTo("Artemis IV"));
        Assert.That(deserialized.LaunchSite, Is.EqualTo("KSC LC-39B"));
    }

    [Test]
    public async Task MissionUpdatedEvent_is_forwarded_to_subscribed_SignalR_client()
    {
        var received = new TaskCompletionSource<(string EventType, JsonElement Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (eventType, payload) =>
        {
            received.TrySetResult((eventType, payload));
        });

        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionUpdatedEvent);

        var missionId = Guid.NewGuid();

        // Act
        await _factory.PubSub.PublishAsync(Channels.MissionUpdatedEvent, new MissionUpdatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId,
            MissionName = "Artemis IV - Revised",
            LaunchSite = "KSC LC-39A",
            ScheduledLaunch = new DateTime(2027, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            PayloadDescription = "Updated payload manifest"
        });

        // Assert
        var (eventType, payload) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(eventType, Is.EqualTo(Channels.MissionUpdatedEvent));

        var deserialized = payload.Deserialize<MissionUpdatedEvent>(JsonOptions);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.MissionId, Is.EqualTo(missionId));
        Assert.That(deserialized.MissionName, Is.EqualTo("Artemis IV - Revised"));
    }

    [Test]
    public async Task MissionDeletedEvent_is_forwarded_to_subscribed_SignalR_client()
    {
        var received = new TaskCompletionSource<(string EventType, JsonElement Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (eventType, payload) =>
        {
            received.TrySetResult((eventType, payload));
        });

        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionDeletedEvent);

        var missionId = Guid.NewGuid();

        // Act
        await _factory.PubSub.PublishAsync(Channels.MissionDeletedEvent, new MissionDeletedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = missionId
        });

        // Assert
        var (eventType, payload) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(eventType, Is.EqualTo(Channels.MissionDeletedEvent));

        var deserialized = payload.Deserialize<MissionDeletedEvent>(JsonOptions);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.MissionId, Is.EqualTo(missionId));
    }

    [Test]
    public async Task Client_does_not_receive_events_for_unsubscribed_channels()
    {
        var received = false;

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (_, _) =>
        {
            received = true;
        });

        // Subscribe only to MissionCreated
        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionCreatedEvent);

        // Publish to MissionDeleted (which this client is NOT subscribed to)
        await _factory.PubSub.PublishAsync(Channels.MissionDeletedEvent, new MissionDeletedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid()
        });

        await Task.Delay(500);
        Assert.That(received, Is.False);
    }

    [Test]
    public async Task Client_stops_receiving_after_unsubscribe()
    {
        var receivedCount = 0;

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (_, _) =>
        {
            Interlocked.Increment(ref receivedCount);
        });

        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionCreatedEvent);

        // First event — should be received
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "First"
        });

        await Task.Delay(500);
        Assert.That(receivedCount, Is.EqualTo(1));

        // Unsubscribe
        await _hubConnection.InvokeAsync("UnsubscribeFromEvent", Channels.MissionCreatedEvent);

        // Second event — should NOT be received
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Second"
        });

        await Task.Delay(500);
        Assert.That(receivedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Multiple_clients_subscribed_to_same_event_both_receive_it()
    {
        // Set up second connection
        await using var hubConnection2 = new HubConnectionBuilder()
            .WithUrl($"{_factory.CreateClient().BaseAddress}hubs/events", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await hubConnection2.StartAsync();

        var received1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _hubConnection.On<string, JsonElement>("ReceiveEvent", (eventType, _) =>
        {
            received1.TrySetResult(eventType);
        });

        hubConnection2.On<string, JsonElement>("ReceiveEvent", (eventType, _) =>
        {
            received2.TrySetResult(eventType);
        });

        await _hubConnection.InvokeAsync("SubscribeToEvent", Channels.MissionCreatedEvent);
        await hubConnection2.InvokeAsync("SubscribeToEvent", Channels.MissionCreatedEvent);

        // Act
        await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, new MissionCreatedEvent
        {
            CorrelationId = Guid.NewGuid(),
            MissionId = Guid.NewGuid(),
            MissionName = "Shared Event"
        });

        // Assert — both clients receive it
        var result1 = await received1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result2 = await received2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(result1, Is.EqualTo(Channels.MissionCreatedEvent));
        Assert.That(result2, Is.EqualTo(Channels.MissionCreatedEvent));
    }
}
