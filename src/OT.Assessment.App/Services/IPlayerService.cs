using OT.Assessment.App.Contracts;

namespace OT.Assessment.App.Services;

public interface IPlayerService
{
    /// <summary>Returns true once the broker has confirmed the wager; false if it could not be queued.</summary>
    Task<bool> PublishWagerAsync(CasinoWagerRequest request, CancellationToken cancellationToken);

    /// <summary>Returns a page of a player's wagers, newest first.</summary>
    Task<CasinoWagerHistoryResponse> GetCasinoHistoryAsync(Guid playerId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Returns the top <paramref name="count"/> players by total spend, highest first.</summary>
    Task<IReadOnlyList<TopSpenderResponse>> GetTopSpendersAsync(int count, CancellationToken cancellationToken);
}
