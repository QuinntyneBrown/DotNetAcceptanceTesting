namespace Shared.Messaging;

public interface IMessageSubscriber
{
    Task SubscribeAsync<T>(string channel, Func<T, Task> handler) where T : class;
    Task UnsubscribeAsync(string channel);
}
