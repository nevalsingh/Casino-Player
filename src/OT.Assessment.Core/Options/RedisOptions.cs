using System.ComponentModel.DataAnnotations;

namespace OT.Assessment.Core.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required] public string HostName { get; init; } = "localhost";
    [Range(1, 65535)] public int Port { get; init; } = 6379;

    /// <summary>Optional ACL user; leave empty for a Redis instance without auth (the local docker-compose one).</summary>
    public string? UserName { get; init; }

    /// <summary>Optional password; leave empty for a Redis instance without auth.</summary>
    public string? Password { get; init; }

    /// <summary>Prefix for every key this app writes, so it can share a Redis instance safely.</summary>
    [Required] public string KeyPrefix { get; init; } = "ot:";

    /// <summary>How long a top-spenders result is served from cache before it is re-read from SQL Server.</summary>
    [Range(1, 3600)] public int TopSpendersTtlSeconds { get; init; } = 10;

    /// <summary>Per-operation timeout. Kept short: a slow cache must never be slower than just asking SQL Server.</summary>
    [Range(50, 10_000)] public int OperationTimeoutMs { get; init; } = 500;
}