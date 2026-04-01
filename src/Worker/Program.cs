using Lab.Shared.Configuration;
using Lab.Shared.Logging;
using Lab.Persistence.DependencyInjection;
using Lab.Telemetry.DependencyInjection;
using Microsoft.Extensions.Options;
using Worker;
using Worker.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddPrimaryPersistence();
builder.Services.AddReadModelPersistence();
builder.Services.AddLabWorkerProcessing();
builder.Logging.AddLabOperationalFileLogging();

var host = builder.Build();
host.LogResolvedLabEnvironment();
host.Run();
