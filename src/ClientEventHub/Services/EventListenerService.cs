using ClientEventHub.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared;
using Shared.Messaging;

namespace ClientEventHub.Services;

public class EventListenerService : IHostedService
{
    private readonly IMessageSubscriber _subscriber;
    private readonly IHubContext<EventHub> _hubContext;
    private readonly ILogger<EventListenerService> _logger;

    public EventListenerService(
        IMessageSubscriber subscriber,
        IHubContext<EventHub> hubContext,
        ILogger<EventListenerService> logger)
    {
        _subscriber = subscriber;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in Channels.AllEventChannels)
        {
            await _subscriber.SubscribeAsync<object>(channel, async payload =>
            {
                _logger.LogInformation("Received event on {Channel}, forwarding to SignalR group", channel);

                await _hubContext.Clients.Group(channel)
                    .SendAsync("ReceiveEvent", channel, payload, cancellationToken);
            });

            _logger.LogInformation("EventListenerService subscribed to PubSub channel {Channel}", channel);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in Channels.AllEventChannels)
        {
            await _subscriber.UnsubscribeAsync(channel);
        }

        _logger.LogInformation("EventListenerService unsubscribed from all channels");
    }
}
