using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;
using StackExchange.Redis;

namespace Shared.Messaging.Redis;

public class RedisPubSub : IMessagePublisher, IMessageSubscriber
{
    private readonly ConnectionMultiplexer _connection;
    private readonly ISubscriber _subscriber;
    private readonly ILogger _logger;

    public RedisPubSub(IOptions<RedisPubSubOptions> options, ILogger logger)
    {
        _logger = logger;
        var settings = options.Value;
        _connection = ConnectionMultiplexer.Connect(settings.ConnectionString);
        _subscriber = _connection.GetSubscriber();
    }

    public async Task PublishAsync<T>(string channel, T message) where T : class
    {
        var envelope = new MessageEnvelope<T> { Payload = message };
        var json = JsonSerializer.Serialize(envelope);

        _logger.Debug("Publishing to {Channel}: {MessageType}", channel, typeof(T).Name);

        await _subscriber.PublishAsync(RedisChannel.Literal(channel), json);
    }

    public async Task SubscribeAsync<T>(string channel, Func<T, Task> handler) where T : class
    {
        await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), async (_, message) =>
        {
            try
            {
                var json = message.ToString();
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<T>>(json);
                if (envelope?.Payload is not null)
                {
                    await handler(envelope.Payload);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Handler error on channel {Channel}", channel);
            }
        });

        _logger.Debug("Subscribed to {Channel} for {MessageType}", channel, typeof(T).Name);
    }

    public async Task UnsubscribeAsync(string channel)
    {
        await _subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));
        _logger.Debug("Unsubscribed from {Channel}", channel);
    }
}
