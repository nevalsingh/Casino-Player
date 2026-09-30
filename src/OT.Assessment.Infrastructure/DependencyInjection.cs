using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Options;
using OT.Assessment.Infrastructure.HealthChecks;
using OT.Assessment.Infrastructure.RabbitMq;
using OT.Assessment.Infrastructure.Redis;
using OT.Assessment.Infrastructure.Sql;
using StackExchange.Redis;

namespace OT.Assessment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMqPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMqCore(configuration);
        services.AddSingleton<RabbitMqPublisherChannels>();
        services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisherChannels>());
        services.AddSingleton<ICasinoWagerPublisher, CasinoWagerPublisher>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq");
        return services;
    }
    
    public static IServiceCollection AddRabbitMqConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMqCore(configuration);
        return services;
    }
    
    private static void AddRabbitMqCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddHostedService<RabbitMqTopologyInitializer>();
    }
    
    public static IServiceCollection AddSqlServerBatchWriter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICasinoWagerBatchWriter>(new CasinoWagerBatchWriter(GetConnectionString(configuration)));
        return services;
    }
    
    public static IServiceCollection AddSqlServerReadRepositories(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetConnectionString(configuration);
        // Registered by concrete type too, so decorators (e.g. the Redis cache) can wrap it.
        services.AddSingleton(new PlayerWagerReadRepository(connectionString));
        services.AddSingleton<IPlayerWagerReadRepository>(sp => sp.GetRequiredService<PlayerWagerReadRepository>());
        services.AddHealthChecks().AddCheck("sql", new SqlHealthCheck(connectionString));
        return services;
    }
    
    public static IServiceCollection AddRedisReadCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var configurationOptions = new ConfigurationOptions
            {
                EndPoints = { { options.HostName, options.Port } },
                User = string.IsNullOrEmpty(options.UserName) ? null : options.UserName,
                Password = string.IsNullOrEmpty(options.Password) ? null : options.Password,
                ClientName = "OT.Assessment.App",
                // Don't fail startup if Redis is down: the multiplexer keeps reconnecting in the background.
                AbortOnConnectFail = false,
                ConnectTimeout = options.OperationTimeoutMs * 4,
                SyncTimeout = options.OperationTimeoutMs,
                AsyncTimeout = options.OperationTimeoutMs,
            };
            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        if (services.All(d => d.ServiceType != typeof(PlayerWagerReadRepository)))
            throw new InvalidOperationException($"Call {nameof(AddSqlServerReadRepositories)} before {nameof(AddRedisReadCache)}.");

        services.Replace(ServiceDescriptor.Singleton<IPlayerWagerReadRepository>(sp => new CachedPlayerWagerReadRepository(
            sp.GetRequiredService<PlayerWagerReadRepository>(),
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<IOptions<RedisOptions>>(),
            sp.GetRequiredService<ILogger<CachedPlayerWagerReadRepository>>())));

        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis");
        return services;
    }
    
    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("DatabaseConnection")
        ?? throw new InvalidOperationException("Missing required configuration 'ConnectionStrings:DatabaseConnection'.");
}