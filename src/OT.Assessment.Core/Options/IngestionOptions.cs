using System.ComponentModel.DataAnnotations;

namespace OT.Assessment.Core.Options;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Maximum number of messages accumulated before a batch is flushed to SQL Server.</summary>
    [Range(1, 100_000)] public int MaxBatchSize { get; init; } = 500;

    /// <summary>Maximum time a partial batch waits before being flushed, in milliseconds.</summary>
    [Range(1, 60_000)] public int MaxBatchDelayMs { get; init; } = 200;
}