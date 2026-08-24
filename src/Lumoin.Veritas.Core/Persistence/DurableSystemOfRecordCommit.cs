namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The receipt of a <see cref="DurableSystemOfRecordStore.Persist"/>: the committed generation, the dictionary
/// epoch it was keyed to, and the term and triple counts that were persisted.
/// </summary>
/// <param name="Generation">The monotonic commit generation published.</param>
/// <param name="DictionaryEpoch">The dictionary epoch the generation was keyed to.</param>
/// <param name="TermCount">The number of terms persisted.</param>
/// <param name="TripleCount">The number of system-of-record triples persisted.</param>
public readonly record struct DurableSystemOfRecordCommit(long Generation, ulong DictionaryEpoch, int TermCount, int TripleCount);
