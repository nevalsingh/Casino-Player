using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Contracts;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;

namespace OT.Assessment.App.Services;

public sealed class PlayerService (ILogger<PlayerService> logger, ICasinoWagerPublisher publisher) : IPlayerService
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish wager {WagerId} to the broker", wagerEvent.WagerId);
            return false;
        }
    }
}