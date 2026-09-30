using OT.Assessment.Core.Messaging;

namespace OT.Assessment.Core.Interfaces;

public interface ICasinoWagerBatchWriter
{
    /// <summary>
    /// Writes <paramref name="batch"/> in one transaction and returns the number of wagers actually inserted
    /// (duplicates by <c>WagerId</c> are skipped).
    /// </summary>
    Task<int> WriteAsync(IReadOnlyList<CasinoWagerEvent> batch, CancellationToken cancellationToken);
}