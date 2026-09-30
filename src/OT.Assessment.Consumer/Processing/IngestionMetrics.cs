namespace OT.Assessment.Consumer.Processing;

public sealed class IngestionMetrics
{
    private long _messagesWritten;
    private long _currentBufferDepth;

    public long MessagesWritten => Interlocked.Read(ref _messagesWritten);

    public long CurrentBufferDepth => Interlocked.Read(ref _currentBufferDepth);

    public void AddWritten(int count) => Interlocked.Add(ref _messagesWritten, count);

    public void SetBufferDepth(int depth) => Interlocked.Exchange(ref _currentBufferDepth, depth);
}
