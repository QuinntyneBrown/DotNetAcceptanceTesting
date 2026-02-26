namespace Shared.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string channel, T message) where T : class;
}
