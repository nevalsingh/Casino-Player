namespace OT.Assessment.Core.Exceptions;

public sealed class NonRetryableWagerDataException(string message, Exception innerException)
    : Exception(message, innerException);