using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// Partitions a reconciliation item key space into <c>2^k</c> shards by the leading bits of each item's
/// (optionally mixed) key. Uniform keys land in balanced, duplicate-free shards by construction, which is what
/// lets a multi-block repair reach be split into many small add-only reconciliation sessions, each well under
/// the session's quadratic copy-validate cost.
/// </summary>
/// <remarks>
/// <para>
/// The shard assignment is a pure function of the item key and the policy fields alone — no clock, no replica
/// identity, no iteration order. This is load-bearing: a repair peels the symmetric difference shard by shard,
/// so an item present on both replicas must be assigned the same shard on both, or its contributions would land
/// in different difference streams and fail to cancel. Two replicas therefore reconcile a sharded generation
/// only when they hold a byte-identical policy — same <see cref="ShardBitCount"/>, same <see cref="Mixing"/>.
/// </para>
/// <para>
/// This type holds no pooled memory and is safe to share. <see cref="Partition"/> is the allocating operation;
/// its result owns a rental and is disposable. Note that <see cref="Partition"/> materializes the whole key set
/// into ONE pooled snapshot buffer, so it inherits the monolithic single-buffer item bound
/// (<see cref="MaximumPartitionItems"/> at a given key width) regardless of shard count; carrying reach past
/// that bound needs a streaming, per-shard-rented partition, which is future work.
/// </para>
/// </remarks>
public sealed class PrefixShardPolicy
{
    /// <summary>The largest shard-bit count the policy accepts, capping the shard-index bookkeeping at 65536 shards.</summary>
    public const int MaximumShardBitCount = 16;

    /// <summary>The fewest key bytes <see cref="ShardOf"/> reads; every reconciliation key in play is at least this wide.</summary>
    public const int MinimumKeyWidth = 8;

    /// <summary>
    /// The multiplicative-hashing constant: two to the sixty-fourth divided by the golden ratio, made odd.
    /// The golden ratio is the hardest real to approximate by fractions (its continued fraction is all ones),
    /// so stepping or scaling by it spreads structured inputs as evenly as a 64-bit word admits — the classic
    /// multiplicative-hash scramble applied before the finalizer, seeding diffusion across both key words.
    /// </summary>
    private const ulong GoldenMultiplier = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// The first finalizer multiplier of the splitmix64 avalanche stage. The digits have no closed form: they
    /// come from a randomized search minimizing avalanche bias — flipping any single input bit must flip each
    /// output bit with probability as near one half as the search could reach, measured over every
    /// input-bit/output-bit pair. Oddness is the structural requirement: an odd multiplier is invertible
    /// modulo two to the sixty-fourth, so the whole finalizer stays a bijection and two distinct keys can
    /// never mix to the same balancing bits.
    /// </summary>
    private const ulong FinalizerMultiplierA = 0xBF58476D1CE4E5B9UL;

    /// <summary>
    /// The second finalizer multiplier, from the same avalanche-optimizing search as
    /// <see cref="FinalizerMultiplierA"/>. Two multiply rounds are needed because multiplication carries
    /// entropy only upward through the bits; the xor-with-right-shift steps between them (the searched shift
    /// triple 30, 27, 31 in <see cref="MixedBits"/>) feed high bits back down, completing the avalanche so
    /// the leading bits the shard index is sliced from see every input bit.
    /// </summary>
    private const ulong FinalizerMultiplierB = 0x94D049BB133111EBUL;

    /// <summary>
    /// Initializes a shard policy over <paramref name="shardBitCount"/> shards' worth of leading bits, derived
    /// under <paramref name="mixing"/>.
    /// </summary>
    /// <param name="shardBitCount">The base-two logarithm of the shard count, in the inclusive range zero through <see cref="MaximumShardBitCount"/>. Zero is the single-shard identity.</param>
    /// <param name="mixing">How the balancing bits are derived from a key.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="shardBitCount"/> is outside its range.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mixing"/> is not a defined strategy.</exception>
    public PrefixShardPolicy(int shardBitCount, ShardKeyMixing mixing)
    {
        if(shardBitCount is < 0 or > MaximumShardBitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(shardBitCount), shardBitCount, $"The shard-bit count must be between zero and {MaximumShardBitCount}.");
        }

        if(mixing.Code != ShardKeyMixing.Identity.Code && mixing.Code != ShardKeyMixing.Avalanche.Code)
        {
            throw new ArgumentException("The mixing strategy must be a defined value.", nameof(mixing));
        }

        ShardBitCount = shardBitCount;
        Mixing = mixing;
    }

    /// <summary>The base-two logarithm of the shard count.</summary>
    public int ShardBitCount { get; }

    /// <summary>How the balancing bits are derived from a key.</summary>
    public ShardKeyMixing Mixing { get; }

    /// <summary>The number of shards this policy partitions into, which is two raised to <see cref="ShardBitCount"/>.</summary>
    public int ShardCount => 1 << ShardBitCount;

    /// <summary>This policy's typed identity declaration — the fields shard assignment is a function of — which a sharded reconciliation exchanges with its peer and compares for equality before consuming any session result.</summary>
    public ShardPolicyFingerprint Fingerprint => new(ShardBitCount, Mixing);

    /// <summary>
    /// The largest item count a single <see cref="Partition"/> snapshot can hold at <paramref name="keyWidth"/>
    /// bytes per key: the whole set is copied into one pooled buffer, so <c>itemCount * keyWidth</c> must fit a
    /// single array. Sharding does not lift this — it precedes the shard split — so a generation past this bound
    /// needs a streaming, per-shard-rented partition instead. At the 16-byte structural width this is 134,217,724.
    /// </summary>
    /// <param name="keyWidth">The exact width of every key, at least <see cref="MinimumKeyWidth"/>.</param>
    /// <returns>The maximum number of items one partition snapshot can hold at that width.</returns>
    public static int MaximumPartitionItems(int keyWidth)
    {
        return Array.MaxLength / keyWidth;
    }

    /// <summary>
    /// Returns the shard index in the range zero through <see cref="ShardCount"/> exclusive that
    /// <paramref name="itemKey"/> belongs to. Pure and deterministic: the same key always yields the same index.
    /// </summary>
    /// <param name="itemKey">The projected item key, at least <see cref="MinimumKeyWidth"/> bytes.</param>
    /// <returns>The shard index the key belongs to.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemKey"/> is narrower than <see cref="MinimumKeyWidth"/>.</exception>
    public int ShardOf(ReadOnlySpan<byte> itemKey)
    {
        if(itemKey.Length < MinimumKeyWidth)
        {
            throw new ArgumentException($"An item key must be at least {MinimumKeyWidth} bytes to shard.", nameof(itemKey));
        }

        //A zero-bit policy is the single-shard identity: every key lands in shard zero without touching the bits.
        if(ShardBitCount == 0)
        {
            return 0;
        }

        ulong bits = Mixing.Code == ShardKeyMixing.Avalanche.Code ? MixedBits(itemKey) : LeadingBits(itemKey);

        return (int)(bits >> (64 - ShardBitCount));
    }

    /// <summary>
    /// Partitions <paramref name="itemKeys"/> into per-shard views over one pooled snapshot buffer. The bytes
    /// are copied once, shard-contiguous, so each shard view is a stable, owned enumeration decoupled from the
    /// caller's memory — the pinned snapshot an add-only session needs. The result owns its rental and must be
    /// disposed after every shard session it feeds has been constructed.
    /// </summary>
    /// <remarks>
    /// The whole key set is snapshotted into a single pooled buffer, so the partition inherits the monolithic
    /// single-buffer item bound (<see cref="MaximumPartitionItems"/>) regardless of shard count. Exceeding it
    /// throws a named <see cref="InvalidOperationException"/> rather than surfacing a raw overflow.
    /// </remarks>
    /// <param name="itemKeys">The projected item keys to partition, each exactly <paramref name="keyWidth"/> bytes.</param>
    /// <param name="keyWidth">The exact width of every key, which must be at least <see cref="MinimumKeyWidth"/>.</param>
    /// <param name="pool">The pool the snapshot buffer rents from; never disposed by the partition.</param>
    /// <returns>A disposable partition exposing one item enumeration per shard.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemKeys"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="keyWidth"/> is below <see cref="MinimumKeyWidth"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when any key's length differs from <paramref name="keyWidth"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the item count exceeds <see cref="MaximumPartitionItems"/> for the width, i.e. the whole set would not fit one snapshot buffer.</exception>
    public PrefixShardPartition Partition(IReadOnlyCollection<ReadOnlyMemory<byte>> itemKeys, int keyWidth, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(itemKeys);
        ArgumentNullException.ThrowIfNull(pool);

        if(keyWidth < MinimumKeyWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(keyWidth), keyWidth, $"The key width must be at least {MinimumKeyWidth} bytes.");
        }

        int total = itemKeys.Count;

        //The partition snapshots the WHOLE set into one pooled buffer before splitting it into shard views, so
        //total * keyWidth must fit a single array. Name the limit here instead of letting the checked multiply
        //below surface a raw OverflowException; sharding does not defer this bound (it precedes the split), so
        //reaching past it needs a streaming, per-shard-rented partition (see the wiring notes).
        int maximumItems = MaximumPartitionItems(keyWidth);
        if(total > maximumItems)
        {
            throw new InvalidOperationException($"A prefix-shard partition snapshots the whole key set into one pooled buffer, so it holds at most {maximumItems} items at a {keyWidth}-byte width; {total} were supplied. Partitioning beyond this bound needs a streaming, per-shard-rented partition.");
        }

        int shardCount = ShardCount;

        //The three per-shard scratch tables (occupancy counts, start offsets, placement cursors) ride ONE
        //pooled rental as int windows: the scratch never escapes the call, so the injected pool is its
        //allocation home. Pool memory arrives dirty, so the windows are cleared before the tally. The rental
        //is using-scoped, so any throw below returns it.
        using IMemoryOwner<byte> scratchOwner = pool.Rent(3 * shardCount * sizeof(int));
        Span<int> scratch = MemoryMarshal.Cast<byte, int>(scratchOwner.Memory.Span)[..(3 * shardCount)];
        scratch.Clear();
        Span<int> counts = scratch[..shardCount];
        Span<int> starts = scratch.Slice(shardCount, shardCount);
        Span<int> cursors = scratch.Slice(2 * shardCount, shardCount);

        //Counting pass: tally each shard's occupancy so the snapshot can be laid out shard-contiguous in one
        //allocation and each shard view sized exactly.
        foreach(ReadOnlyMemory<byte> key in itemKeys)
        {
            if(key.Length != keyWidth)
            {
                throw new ArgumentException($"Every item key must be exactly {keyWidth} bytes.", nameof(itemKeys));
            }

            counts[ShardOf(key.Span)]++;
        }

        //Prefix-sum the counts into shard start offsets (in items) and give each shard its handle run. The
        //handle runs are the partition's OWNED RESULT structure, not scratch: their element type carries an
        //object reference, so they cannot ride the byte pool, and they live exactly as long as the partition.
        //Every run a not-yet-constructed partition will be handed is allocated BEFORE the snapshot rental, so
        //an out-of-memory failure on any of these news cannot orphan that pooled buffer.
        ReadOnlyMemory<byte>[][] shards = new ReadOnlyMemory<byte>[shardCount][];
        int running = 0;
        for(int s = 0; s < shardCount; s++)
        {
            starts[s] = running;
            shards[s] = new ReadOnlyMemory<byte>[counts[s]];
            running += counts[s];
        }

        //Rent last, once every array it feeds already exists. Rent at least one byte so an empty key set — the
        //Rent(0) some pools reject, and exactly the sparse-survivor case this rung targets — still yields a valid
        //empty snapshot. This mirrors SketchPersistence.PersistSketch's zero-budget rent guard.
        IMemoryOwner<byte> owner = pool.Rent(Math.Max(1, checked(total * keyWidth)));

        //Placement pass: copy each key into its shard's contiguous run and record the slice handle. A per-shard
        //cursor walks the run; recomputing the shard is cheaper than a second bookkeeping rental.
        Memory<byte> buffer = owner.Memory;
        foreach(ReadOnlyMemory<byte> key in itemKeys)
        {
            int shard = ShardOf(key.Span);
            int slot = starts[shard] + cursors[shard];
            Memory<byte> destination = buffer.Slice(slot * keyWidth, keyWidth);
            key.CopyTo(destination);
            shards[shard][cursors[shard]] = destination;
            cursors[shard]++;
        }

        return new PrefixShardPartition(owner, shards, total);
    }

    /// <summary>Reads the key's leading eight bytes big-endian, so the "leading" prefix bits are the high bits — aligning the shard index with a literal wire key-prefix route over the same bytes.</summary>
    /// <param name="key">The item key, at least <see cref="MinimumKeyWidth"/> bytes.</param>
    /// <returns>The unmixed balancing bits.</returns>
    private static ulong LeadingBits(ReadOnlySpan<byte> key)
    {
        return BinaryPrimitives.ReadUInt64BigEndian(key);
    }

    /// <summary>Derives uniformly distributed balancing bits from the key through a finalizing bit mix, so the leading bits are balanced for any key domain including structural.</summary>
    /// <param name="key">The item key, at least <see cref="MinimumKeyWidth"/> bytes.</param>
    /// <returns>The mixed balancing bits.</returns>
    private static ulong MixedBits(ReadOnlySpan<byte> key)
    {
        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(key);
        ulong high = key.Length >= 16 ? BinaryPrimitives.ReadUInt64LittleEndian(key[8..]) : 0UL;

        //A finalizing mix: fold both words with the golden-ratio multiplier, then run the splitmix64 finalizer so
        //every input bit avalanches into the high bits the caller slices the shard index from.
        ulong h = unchecked((low * GoldenMultiplier) ^ (high + GoldenMultiplier + (low << 6) + (low >> 2)));
        h ^= h >> 30;
        h = unchecked(h * FinalizerMultiplierA);
        h ^= h >> 27;
        h = unchecked(h * FinalizerMultiplierB);
        h ^= h >> 31;

        return h;
    }
}
