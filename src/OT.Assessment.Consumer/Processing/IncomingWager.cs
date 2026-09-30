using OT.Assessment.Core.Messaging;

namespace OT.Assessment.Consumer.Processing;

public sealed record IncomingWager(ulong DeliveryTag, CasinoWagerEvent? Wager);
