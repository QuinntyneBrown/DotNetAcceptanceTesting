using Microsoft.AspNetCore.SignalR;

namespace ClientEventHub.Hubs;

public class EventHub : Hub
{
    private readonly ILogger<EventHub> _logger;

    public EventHub(ILogger<EventHub> logger)
    {
        _logger = logger;
    }

    public async Task SubscribeToEvent(string eventType)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, eventType);
        _logger.LogInformation("Client {ConnectionId} subscribed to {EventType}",
            Context.ConnectionId, eventType);
    }

    public async Task UnsubscribeFromEvent(string eventType)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventType);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from {EventType}",
            Context.ConnectionId, eventType);
    }
}
