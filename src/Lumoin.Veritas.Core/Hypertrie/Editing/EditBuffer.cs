using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Accumulates pending edits within an edit session before
/// commit. Each triple has at most one recorded edit at any
/// time; calling <see cref="Add"/> or <see cref="Remove"/> on a
/// triple that already has a recorded edit overwrites the prior
/// one (last write wins). Calling <see cref="Add"/> after
/// <see cref="Remove"/> for the same triple is therefore the
/// same as calling <see cref="Add"/> in isolation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a domain type.</b> The natural in-memory shape is a
/// dictionary, but exposing
/// <c>Dictionary&lt;EncodedTriple, EditKind&gt;</c> would leak
/// the implementation choice into every consumer that wants to
/// inspect the buffer (debuggers, tests, future replay tools)
/// and would invite ad-hoc ordering or representation changes
/// that are hard to reason about. The wrapper fixes the
/// observable surface and lets the storage choice evolve.
/// </para>
/// <para>
/// <b>Thread safety.</b> A buffer is owned by exactly one edit
/// session, which is owned by one logical caller. The buffer
/// itself is not thread-safe; concurrent access is a contract
/// violation. Multiple sessions hold their own buffers and
/// commit through a journal whose append delegate enforces
/// optimistic concurrency.
/// </para>
/// <para>
/// <b>Net delta.</b> The buffer holds the caller's intent, not
/// the structural delta. Whether a recorded
/// <see cref="EditKind.Add"/> for a triple actually changes the
/// graph depends on whether the triple was already present in
/// the base snapshot; that determination happens at commit time,
/// not in the buffer.
/// </para>
/// </remarks>
[DebuggerDisplay("EditBuffer Count={Count}")]
public sealed class EditBuffer
{
    private Dictionary<EncodedTriple, EditKind> Edits { get; } = [];

    /// <summary>The number of triples with a recorded edit.</summary>
    public int Count => Edits.Count;

    /// <summary>The triples currently scheduled for addition.</summary>
    public IEnumerable<EncodedTriple> PendingAdditions => Edits.Where(static kvp => kvp.Value == EditKind.Add).Select(static kvp => kvp.Key);

    /// <summary>The triples currently scheduled for removal.</summary>
    public IEnumerable<EncodedTriple> PendingRemovals => Edits.Where(static kvp => kvp.Value == EditKind.Remove).Select(static kvp => kvp.Key);

    /// <summary>
    /// Records that <paramref name="triple"/> should be added to
    /// the graph at commit. If a prior edit was recorded for the
    /// same triple, this call overwrites it.
    /// </summary>
    public void Add(EncodedTriple triple)
    {
        Edits[triple] = EditKind.Add;
    }

    /// <summary>
    /// Records that <paramref name="triple"/> should be removed
    /// from the graph at commit. If a prior edit was recorded for
    /// the same triple, this call overwrites it.
    /// </summary>
    public void Remove(EncodedTriple triple)
    {
        Edits[triple] = EditKind.Remove;
    }

    /// <summary>
    /// Removes any recorded edit for <paramref name="triple"/>,
    /// returning the buffer to the state where it has no opinion
    /// about that triple. Useful when a session computes
    /// cancellations explicitly.
    /// </summary>
    /// <returns><c>true</c> when an edit was removed; <c>false</c> when no edit was recorded.</returns>
    public bool ClearEdit(EncodedTriple triple)
    {
        return Edits.Remove(triple);
    }

    /// <summary>Removes every recorded edit.</summary>
    public void Clear()
    {
        Edits.Clear();
    }

    /// <summary>
    /// Returns the recorded edit for <paramref name="triple"/>,
    /// or <c>false</c> when no edit is recorded.
    /// </summary>
    public bool TryGetEdit(EncodedTriple triple, out EditKind kind)
    {
        return Edits.TryGetValue(triple, out kind);
    }

    /// <summary>
    /// Enumerates every recorded edit as a (triple, kind) pair.
    /// Order is not specified.
    /// </summary>
    public IEnumerable<KeyValuePair<EncodedTriple, EditKind>> EnumerateEdits()
    {
        return Edits;
    }
}
