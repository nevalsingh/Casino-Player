using System.ComponentModel.DataAnnotations;

namespace OT.Assessment.Core.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    [Required] public string UserName { get; init; } = "guest";
    [Required] public string Password { get; init; } = "guest";
    public string VirtualHost { get; init; } = "/";

    [Required] public string Exchange { get; init; } = "ot.casino";
    [Required] public string RoutingKey { get; init; } = "casino.wager";
    [Required] public string Queue { get; init; } = "casino-wagers";
    [Required] public string DeadLetterExchange { get; init; } = "ot.casino.dlx";
    [Required] public string DeadLetterQueue { get; init; } = "casino-wagers.dlq";

    /// <summary>Number of long-lived, confirm-enabled channels the API publishes over.</summary>
    [Range(1, 64)] public int PublisherChannelCount { get; init; } = 8;

    /// <summary>Consumer prefetch count; also sizes the consumer's internal bounded channel.</summary>
    [Range(1, 10_000)] public ushort PrefetchCount { get; init; } = 1000;
}