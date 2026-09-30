namespace OT.Assessment.App.Contracts;

/// <summary>One row of a player's wager history.</summary>
public sealed record CasinoWagerHistoryItem(Guid WagerId, string Game, string Provider, decimal Amount, DateTime CreatedDate);

/// <summary>Paginated response for the player casino history endpoint.</summary>
public sealed record CasinoWagerHistoryResponse(
    IReadOnlyList<CasinoWagerHistoryItem> Data,
    int Page,
    int PageSize,
    int Total,
    int TotalPages);

/// <summary>One row of the top-spenders leaderboard.</summary>
public sealed record TopSpenderResponse(Guid AccountId, string Username, decimal TotalAmountSpend);