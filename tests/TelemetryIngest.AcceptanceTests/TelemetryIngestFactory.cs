using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Shared.Messaging;
using Shared.Messaging.Redis;
using TelemetryIngest.Receivers;

namespace TelemetryIngest.AcceptanceTests;

public sealed class TelemetryIngestFactory : WebApplicationFactory<Program>
{
    private InMemoryPubSub? _pubSub;
    private InMemoryPacketReceiver? _packetReceiver;

    public InMemoryPubSub PubSub =>
        _pubSub ?? throw new InvalidOperationException("Factory not initialized. Call CreateClient() first.");

    public InMemoryPacketReceiver PacketReceiver =>
        _packetReceiver ?? throw new InvalidOperationException("Factory not initialized. Call CreateClient() first.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace Redis with InMemoryPubSub
            services.RemoveAll<RedisPubSub>();
            services.RemoveAll<IMessagePublisher>();
            services.RemoveAll<IMessageSubscriber>();

            var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            var pubSub = new InMemoryPubSub(logger);
            _pubSub = pubSub;

            services.AddSingleton(pubSub);
            services.AddSingleton<IMessagePublisher>(pubSub);
            services.AddSingleton<IMessageSubscriber>(pubSub);

            // Replace UDP receiver with InMemoryPacketReceiver
            services.RemoveAll<IPacketReceiver>();

            var packetReceiver = new InMemoryPacketReceiver();
            _packetReceiver = packetReceiver;

            services.AddSingleton<IPacketReceiver>(packetReceiver);
        });
    }
}
