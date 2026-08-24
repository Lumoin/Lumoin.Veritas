namespace Lumoin.Veritas.Database;

/// <summary>
/// The outcome of one <see cref="VeritasEngine.IngestAsync"/> commit: how many triples the stream submitted and
/// how the journalled write-back landed. Already-present triples are filtered by the edit session, so the
/// submitted count is an upper bound on the net additions; an empty stream lands as
/// <see cref="WriteBackOutcome.NoOp"/>.
/// </summary>
/// <param name="TripleCount">The number of default-graph triples the stream submitted for commit.</param>
/// <param name="WriteBack">How the journalled write-back landed.</param>
public readonly record struct IngestReceipt(int TripleCount, WriteBackOutcome WriteBack);
