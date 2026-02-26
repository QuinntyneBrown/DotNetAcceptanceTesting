using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;

namespace Shared.Messaging;

public class InMemoryPubSub : IMessagePublisher, IMessageSubscriber
{
    private readonly ConcurrentDictionary<string, List<Func<string, Task>>> _subscribers = new();
    private readonly ILogger _logger;

    public InMemoryPubSub(ILogger logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<T>(string channel, T message) where T : class
    {
        var envelope = new MessageEnvelope<T> { Payload = message };
        var json = JsonSerializer.Serialize(envelope);

        _logger.Debug("Publishing to {Channel}: {MessageType}", channel, typeof(T).Name);

        if (_subscribers.TryGetValue(channel, out var handlers))
        {
            var snapshot = handlers.ToList();
            var tasks = snapshot.Select(async handler =>
            {
                try
                {
                    await handler(json);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Handler error on channel {Channel}", channel);
                }
            });
            await Task.WhenAll(tasks);
        }
    }

    public Task SubscribeAsync<T>(string channel, Func<T, Task> handler) where T : class
    {
        var handlers = _subscribers.GetOrAdd(channel, _ => new List<Func<string, Task>>());

        lock (handlers)
        {
            handlers.Add(async json =>
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<T>>(json);
                if (envelope?.Payload is not null)
                {
                    await handler(envelope.Payload);
                }
            });
        }

        _logger.Debug("Subscribed to {Channel} for {MessageType}", channel, typeof(T).Name);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string channel)
    {
        _subscribers.TryRemove(channel, out _);
        _logger.Debug("Unsubscribed from {Channel}", channel);
        return Task.CompletedTask;
    }
}
