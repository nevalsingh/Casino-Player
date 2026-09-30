using OT.Assessment.Core.Messaging;
using OT.Assessment.Core.Models;

namespace OT.Assessment.Core.Interfaces;

// <summary>Read-side queries backing the player GET endpoints. Implemented in Infrastructure over SQL Server.</summary>
public interface IPlayerWagerReadRepository
{
    Task<PagedResult<PlayerCasinoWager>> GetWagerPageAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopSpender>> GetTopSpendersAsync(int count, CancellationToken cancellationToken);
}