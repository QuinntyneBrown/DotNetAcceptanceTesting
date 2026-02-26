using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Shared;
using Shared.Messages.Events;

namespace ClientEventHub.Tests;

public class EventHubIntegrationTests : IAsyncLifetime
{
    private readonly ClientEventHubFactory _factory;
    private HubConnection _hubConnection = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EventHubIntegrationTests()
    {
        _factory = new ClientEventHubFactory();
    }

    public async Task InitializeAsync()
    {
        var client = _factory.CreateClient();

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{client.BaseAddress}hubs/events", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await _hubConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
        _factory.Dispose();
    }

    [Fact]
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

        Assert.Equal(Channels.MissionCreatedEvent, eventType);

        var deserialized = payload.Deserialize<MissionCreatedEvent>(JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(missionId, deserialized.MissionId);
        Assert.Equal("Artemis IV", deserialized.MissionName);
        Assert.Equal("KSC LC-39B", deserialized.LaunchSite);
    }

    [Fact]
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

        Assert.Equal(Channels.MissionUpdatedEvent, eventType);

        var deserialized = payload.Deserialize<MissionUpdatedEvent>(JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(missionId, deserialized.MissionId);
        Assert.Equal("Artemis IV - Revised", deserialized.MissionName);
    }

    [Fact]
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

        Assert.Equal(Channels.MissionDeletedEvent, eventType);

        var deserialized = payload.Deserialize<MissionDeletedEvent>(JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(missionId, deserialized.MissionId);
    }

    [Fact]
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
        Assert.False(received);
    }

    [Fact]
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
        Assert.Equal(1, receivedCount);

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
        Assert.Equal(1, receivedCount);
    }

    [Fact]
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

        Assert.Equal(Channels.MissionCreatedEvent, result1);
        Assert.Equal(Channels.MissionCreatedEvent, result2);
    }
}
