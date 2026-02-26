using EventStore.Persistence;
using EventStore.Services;
using Shared.Messaging;
using Shared.Messaging.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConfigureEventStoreServices
{
    public static void AddEventStoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisPubSubOptions>(configuration.GetSection("RedisPubSub"));
        services.Configure<CouchbaseOptions>(configuration.GetSection("Couchbase"));

        services.AddSingleton<RedisPubSub>();
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RedisPubSub>());
        services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<RedisPubSub>());

        services.AddSingleton<CouchbaseEventRepository>();
        services.AddSingleton<IEventRepository>(sp => sp.GetRequiredService<CouchbaseEventRepository>());

        services.AddHostedService<EventPersistenceService>();
    }
}
