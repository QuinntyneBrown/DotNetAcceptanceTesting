using EventStore.Models;
using EventStore.Persistence;
using Shared;
using Shared.Messaging;

namespace EventStore.Services;

public class EventPersistenceService : IHostedService
{
    private readonly IMessageSubscriber _subscriber;
    private readonly IEventRepository _repository;
    private readonly ILogger<EventPersistenceService> _logger;

    public EventPersistenceService(
        IMessageSubscriber subscriber,
        IEventRepository repository,
        ILogger<EventPersistenceService> logger)
    {
        _subscriber = subscriber;
        _repository = repository;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in Channels.AllEventChannels)
        {
            await _subscriber.SubscribeAsync<object>(channel, async payload =>
            {
                var storedEvent = new StoredEvent
                {
                    Id = $"{channel}:{Guid.NewGuid()}",
                    EventType = channel,
                    Payload = payload,
                    StoredAtUtc = DateTime.UtcNow
                };

                await _repository.StoreAsync(storedEvent);

                _logger.LogInformation(
                    "Persisted event {EventType} with ID {EventId}",
                    channel, storedEvent.Id);
            });

            _logger.LogInformation(
                "EventPersistenceService subscribed to channel {Channel}", channel);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in Channels.AllEventChannels)
        {
            await _subscriber.UnsubscribeAsync(channel);
        }

        _logger.LogInformation("EventPersistenceService unsubscribed from all channels");
    }
}
