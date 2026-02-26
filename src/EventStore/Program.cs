using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddEventStoreServices(builder.Configuration);

var host = builder.Build();

host.Run();

public partial class Program { }
