namespace OT.Assessment.Core.Models;

/// <summary>A single page of a player's casino wager history.</summary>
public sealed record PlayerCasinoWager(Guid WagerId, string Game, string Provider, decimal Amount, DateTime CreatedDate);

/// <summary>A player ranked by total spend.</summary>
public sealed record TopSpender(Guid AccountId, string Username, decimal TotalAmountSpend);

/// <summary>Generic paged result wrapper for list endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}