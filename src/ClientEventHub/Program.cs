using ClientEventHub.Hubs;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddClientEventHubServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapHub<EventHub>("/hubs/events");

app.Run();

public partial class Program { }
