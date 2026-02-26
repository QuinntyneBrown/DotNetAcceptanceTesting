using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddTelemetryIngestServices(builder.Configuration);

var app = builder.Build();

app.Run();

public partial class Program { }
