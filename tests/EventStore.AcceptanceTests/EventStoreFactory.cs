using EventStore.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Shared.Messaging;
using Shared.Messaging.Redis;

namespace EventStore.AcceptanceTests;

public sealed class EventStoreFactory : IAsyncDisposable
{
    private IHost _host = default!;

    public InMemoryPubSub PubSub { get; private set; } = default!;
    public InMemoryEventRepository EventRepository { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        PubSub = new InMemoryPubSub(logger);
        EventRepository = new InMemoryEventRepository();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddEventStoreServices(context.Configuration);

                // Replace Redis with InMemoryPubSub
                services.RemoveAll<RedisPubSub>();
                services.RemoveAll<IMessagePublisher>();
                services.RemoveAll<IMessageSubscriber>();

                services.AddSingleton(PubSub);
                services.AddSingleton<IMessagePublisher>(PubSub);
                services.AddSingleton<IMessageSubscriber>(PubSub);

                // Replace Couchbase with InMemoryEventRepository
                services.RemoveAll<CouchbaseEventRepository>();
                services.RemoveAll<IEventRepository>();
                services.AddSingleton<IEventRepository>(EventRepository);
            })
            .Build();

        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
