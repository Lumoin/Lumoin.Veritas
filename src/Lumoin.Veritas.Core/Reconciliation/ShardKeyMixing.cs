namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// How a shard policy derives the balancing bits it slices a shard index out of. Reconciliation item keys are
/// not uniformly distributed in every domain, so the mixing strategy is contractual: two replicas that shard
/// the same set must derive identical shard assignments, which they do only when they share both the shard-bit
/// count and this mixing. The strategy is a <c>record struct</c> carrying an <c>int</c> code so a deployment can
/// register a further strategy without a breaking enum edit, following the dynamic-enum idiom.
/// </summary>
/// <remarks>
/// <see cref="Identity"/> takes the leading bits of the key unchanged. It is balanced only when the key space is
/// already uniform — content-hash keys are, structural keys are not: under the frozen structural layout
/// <see cref="PrefixShardPolicy.ShardOf"/> slices its prefix from the subject's low byte, so a generation with
/// few distinct subjects concentrates into few shards. Identity is the strategy to pick when shard assignment
/// must line up with a literal wire key-prefix route. <see cref="Avalanche"/> runs the key through a finalizing
/// bit mix first, so the leading bits are uniform for any domain including structural; it is the safe default and
/// costs a handful of multiplies.
/// </remarks>
public readonly record struct ShardKeyMixing
{
    /// <summary>
    /// Initializes a mixing strategy from its stable code.
    /// </summary>
    /// <param name="code">The stable identifier distinguishing this strategy from the others.</param>
    public ShardKeyMixing(int code)
    {
        Code = code;
    }

    /// <summary>The stable identifier of this mixing strategy.</summary>
    public int Code { get; }

    /// <summary>Takes the leading key bits unchanged; balanced only over an already-uniform key space.</summary>
    public static ShardKeyMixing Identity { get; } = new(1);

    /// <summary>Runs the key through a finalizing bit mix first; balanced over any key space including structural.</summary>
    public static ShardKeyMixing Avalanche { get; } = new(2);
}
