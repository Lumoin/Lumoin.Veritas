using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The confirmation half of an agreed lineage baseline: the dataset state the committed baseline produced and
/// the term-dictionary epoch it was written under. The two facts are born together at the local durable commit
/// and are meaningful only together, so they travel as one value — a <see cref="LineageBaseline"/> either
/// carries a confirmation whole or carries none, and a half-confirmed baseline is unconstructible by shape.
/// </summary>
/// <param name="StateId">The dataset StateId the committed baseline produced.</param>
/// <param name="DictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
/// <remarks>
/// Equality is the synthesized record equality and is content-based in both members
/// (<see cref="NodeIdentifier"/> and <see cref="long"/> are numbers), so a confirmation decoded from bytes
/// equals the confirmation that was encoded — the property the containing baseline's comparison rests on.
/// A zero <see cref="StateId"/> and epoch are legitimate confirmed values (the empty dataset, the first
/// epoch); absence is expressed by the baseline's null confirmation, never by a zero sentinel.
/// </remarks>
public sealed record LineageConfirmation(NodeIdentifier StateId, long DictionaryEpoch);
