using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Options;
using OT.Assessment.Infrastructure.HealthChecks;
using OT.Assessment.Infrastructure.RabbitMq;

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
    
    private static void AddRabbitMqCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddHostedService<RabbitMqTopologyInitializer>();
    }
}