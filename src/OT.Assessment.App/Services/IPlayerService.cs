using OT.Assessment.App.Contracts;

namespace OT.Assessment.App.Services;

public interface IPlayerService
{
    Task<bool> PublishWagerAsync(CasinoWagerRequest request, CancellationToken cancellationToken);
}