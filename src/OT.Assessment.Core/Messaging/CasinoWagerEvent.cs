namespace OT.Assessment.Core.Messaging;

public sealed record CasinoWagerEvent
{
    public required Guid WagerId { get; init; }
    public required string Theme { get; init; }
    public required string Provider { get; init; }
    public required string GameName { get; init; }
    public required Guid TransactionId { get; init; }
    public required Guid BrandId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Username { get; init; }
    public required Guid ExternalReferenceId { get; init; }
    public required Guid TransactionTypeId { get; init; }
    public required decimal Amount { get; init; }
    public required DateTimeOffset CreatedDateTime { get; init; }
    public required int NumberOfBets { get; init; }
    public required string CountryCode { get; init; }
    public required long Duration { get; init; }
}