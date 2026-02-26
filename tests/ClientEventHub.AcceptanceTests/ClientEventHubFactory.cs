using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Shared.Messaging;
using Shared.Messaging.Redis;

namespace ClientEventHub.AcceptanceTests;

public sealed class ClientEventHubFactory : WebApplicationFactory<Program>
{
    private InMemoryPubSub? _pubSub;

    public InMemoryPubSub PubSub =>
        _pubSub ?? throw new InvalidOperationException("Factory not initialized. Call CreateClient() first.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RedisPubSub>();
            services.RemoveAll<IMessagePublisher>();
            services.RemoveAll<IMessageSubscriber>();

            var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            var pubSub = new InMemoryPubSub(logger);
            _pubSub = pubSub;

            services.AddSingleton(pubSub);
            services.AddSingleton<IMessagePublisher>(pubSub);
            services.AddSingleton<IMessageSubscriber>(pubSub);
        });
    }
}
