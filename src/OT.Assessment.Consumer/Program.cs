using OT.Assessment.Consumer.Processing;
using OT.Assessment.Core.Options;
using OT.Assessment.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRabbitMqConsumer(builder.Configuration);
builder.Services.AddSqlServerBatchWriter(builder.Configuration);

builder.Services.AddSingleton<IngestionMetrics>();
builder.Services.AddHostedService<CasinoWagerConsumerService>();
builder.Services.AddHostedService<ThroughputLoggerService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application started {time:yyyy-MM-dd HH:mm:ss}", DateTime.Now);

await host.RunAsync();

logger.LogInformation("Application ended {time:yyyy-MM-dd HH:mm:ss}", DateTime.Now);