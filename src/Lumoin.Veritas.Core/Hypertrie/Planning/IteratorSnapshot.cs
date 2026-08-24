using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// A read-only snapshot of one iterator's current state, taken
/// at a planner consultation point. The planner inspects these
/// to make adaptive decisions but cannot mutate the underlying
/// iterator through the snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a snapshot.</b> Exposing the live
/// <see cref="TriejoinIterator"/> to the planner would let the
/// planner call <c>Open</c>, <c>Up</c>, <c>Next</c>, or
/// <c>Seek</c> mid-consultation and corrupt the driver's
/// state. A read-only snapshot record is cheaper than wrapping
/// each iterator behind an interface, immutable by default,
/// and makes the planner's exact input surface explicit.
/// </para>
/// <para>
/// <b>What the snapshot carries.</b> The minimum the planner
/// needs to pick a next variable: which pattern this iterator
/// is matching, which variable it is currently positioned at,
/// the key it is currently positioned on, whether it is at
/// end, and how many of its variables have already been bound.
/// Wider state — the descended path so far, the cursor's
/// internal position — is not exposed because no current
/// planner uses it; if a future adaptive planner needs more,
/// the snapshot grows.
/// </para>
/// <para>
/// <b>Lifetime.</b> A snapshot is valid only for the planner
/// consultation it was created for. The driver re-builds
/// fresh snapshots at each consultation point. Holding a
/// snapshot past the consultation produces stale data with
/// no warning.
/// </para>
/// </remarks>
[DebuggerDisplay("IteratorSnapshot Pattern={PatternIndex} Var={CurrentVariable.Id} Key={Key.Encoded} AtEnd={AtEnd}")]
public readonly record struct IteratorSnapshot(
    int PatternIndex,
    Variable CurrentVariable,
    TermId Key,
    bool AtEnd,
    int DescendedLevels);
