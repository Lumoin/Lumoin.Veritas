namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Whether the hypertrie system of record is held resident at all times or materialised only on demand — the
/// operator's choice between an index that is always ready and a warm, columnar-primary start. Correctness and
/// access-control security are identical under both: an access-controlled query always evaluates on the trie (the
/// columnar path has no per-candidate consultation point), and the columnar view answers every shape it serves
/// with results identical to the trie's.
/// </summary>
public enum HypertrieResidency
{
    /// <summary>
    /// The trie is built up front and serves every shape it is the home of — single-pattern lookups, per-pattern
    /// self-joins (<c>?x :q ?x</c>), cyclic shapes without a self-index, and access-controlled queries. The index
    /// is ready the instant the engine is online; the cost is building and holding it even for a read generation a
    /// warm columnar view could answer.
    /// </summary>
    Eager = 0,

    /// <summary>
    /// The trie is deferred: a present columnar view answers the columnar-capable shapes that would otherwise go to
    /// the trie, and the trie is materialised only when a query genuinely needs it — a per-pattern self-join, a
    /// cyclic shape without a self-index, or (always) an access-controlled query. A warm-loaded read generation
    /// without access control may never build it. The cost the operator accepts: a cold-start build on the first
    /// query that does need the trie, so there may be no trie index for those shapes while the system comes online.
    /// </summary>
    Deferred = 1,
}
