using ClientEventHub.Services;
using Shared.Messaging;
using Shared.Messaging.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConfigureClientEventHubServices
{
    public static void AddClientEventHubServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.Configure<RedisPubSubOptions>(configuration.GetSection("RedisPubSub"));

        services.AddSingleton<RedisPubSub>();
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RedisPubSub>());
        services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<RedisPubSub>());

        services.AddHostedService<EventListenerService>();
    }
}
