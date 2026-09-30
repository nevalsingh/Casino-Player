using OT.Assessment.App.Contracts;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;

namespace OT.Assessment.App.Services;

public sealed class PlayerService(
    ILogger<PlayerService> logger,
    ICasinoWagerPublisher publisher,
    IPlayerWagerReadRepository readRepository) : IPlayerService
{
    public async Task<bool> PublishWagerAsync(CasinoWagerRequest request, CancellationToken cancellationToken)
    {
        var wagerEvent = new CasinoWagerEvent
        {
            WagerId = request.WagerId,
            Theme = request.Theme,
            Provider = request.Provider,
            GameName = request.GameName,
            TransactionId = request.TransactionId,
            BrandId = request.BrandId,
            AccountId = request.AccountId,
            Username = request.Username,
            ExternalReferenceId = request.ExternalReferenceId,
            TransactionTypeId = request.TransactionTypeId,
            Amount = (decimal)request.Amount,
            CreatedDateTime = request.CreatedDateTime,
            NumberOfBets = request.NumberOfBets,
            CountryCode = request.CountryCode,
            Duration = request.Duration,
        };

        try
        {
            await publisher.PublishAsync(wagerEvent, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to publish wager {WagerId} to the broker", wagerEvent.WagerId);
            return false;
        }
    }

    public async Task<CasinoWagerHistoryResponse> GetCasinoHistoryAsync(Guid playerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await readRepository.GetWagerPageAsync(playerId, page, pageSize, cancellationToken);

        return new CasinoWagerHistoryResponse(
            result.Data.Select(w => new CasinoWagerHistoryItem(w.WagerId, w.Game, w.Provider, w.Amount, w.CreatedDate)).ToList(),
            result.Page,
            result.PageSize,
            result.Total,
            result.TotalPages);
    }

    public async Task<IReadOnlyList<TopSpenderResponse>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        var topSpenders = await readRepository.GetTopSpendersAsync(count, cancellationToken);
        return topSpenders.Select(s => new TopSpenderResponse(s.AccountId, s.Username, s.TotalAmountSpend)).ToList();
    }
}
