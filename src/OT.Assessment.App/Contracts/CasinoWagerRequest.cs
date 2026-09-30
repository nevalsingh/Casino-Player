using System.ComponentModel.DataAnnotations;

namespace OT.Assessment.App.Contracts;

public class CasinoWagerRequest
{
    [Required] public Guid WagerId { get; init; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string Theme { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Provider { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string GameName { get; init; } = string.Empty;

    [Required] public Guid TransactionId { get; init; }

    [Required] public Guid BrandId { get; init; }

    [Required] public Guid AccountId { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string Username { get; init; } = string.Empty;

    [Required] public Guid ExternalReferenceId { get; init; }

    [Required] public Guid TransactionTypeId { get; init; }

    [Range(0, double.MaxValue)] public double Amount { get; init; }

    [Required] public DateTimeOffset CreatedDateTime { get; init; }

    [Range(1, int.MaxValue)] public int NumberOfBets { get; init; }

    [Required, StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; init; } = string.Empty;

    public string? SessionData { get; init; }

    [Range(0, long.MaxValue)] public long Duration { get; init; }
}