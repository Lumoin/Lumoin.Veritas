namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// The phase a <see cref="EvalFrameKind.GroupBy"/> cursor frame is in. The object constructor groups an
/// input sequence in two ordered passes — bucket every item by each member pair's key, then evaluate each
/// group's value — so the resident frame advances seed → bucketing → valuing → done across its turns. The
/// led path-step form <c>path{ ... }</c> first runs an extra source-evaluation turn that replaces the seed
/// input with the source's result.
/// </summary>
internal enum GroupByPhase
{
    /// <summary>Seed from source: the led path-step form's first turn, which consumes the evaluated grouping-source result on the results stack and uses it as the seed input in place of the focus.</summary>
    SeedFromSource,

    /// <summary>Seed: consume the evaluated focus, normalize it to items, and initialise the buckets and cursors.</summary>
    Seed,

    /// <summary>Bucketing: evaluate each member pair's key under each item's rebound focus and bucket the item.</summary>
    Bucketing,

    /// <summary>Valuing: evaluate each group pair's value under the grouped sub-sequence's rebound focus and collect the member.</summary>
    Valuing
}
