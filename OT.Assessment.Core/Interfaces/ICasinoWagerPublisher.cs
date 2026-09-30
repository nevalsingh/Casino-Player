using OT.Assessment.Core.Messaging;

namespace OT.Assessment.Core.Interfaces;

public interface ICasinoWagerPublisher
{
    Task PublishAsync(CasinoWagerEvent wager, CancellationToken cancellationToken);
}