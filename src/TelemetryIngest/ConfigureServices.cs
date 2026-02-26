using Shared.Messaging;
using Shared.Messaging.Redis;
using TelemetryIngest.Receivers;
using TelemetryIngest.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConfigureTelemetryIngestServices
{
    public static void AddTelemetryIngestServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisPubSubOptions>(configuration.GetSection("RedisPubSub"));
        services.Configure<UdpReceiverOptions>(configuration.GetSection("UdpReceiver"));

        services.AddSingleton<RedisPubSub>();
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RedisPubSub>());
        services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<RedisPubSub>());

        services.AddSingleton<IPacketReceiver, UdpPacketReceiver>();

        services.AddHostedService<TelemetryIngestService>();
    }
}
