using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Lumoin.Veritas.Tests.Reconciliation;

/// <summary>
/// A deterministic corpus of distinct fixed-width projected reconciliation item keys — the semantic stand-in
/// for a replica's survivor or loss set, handed to the production surfaces through <see cref="Keys"/>. One
/// backing buffer holds every key; each key is a stable slice handle into it, mirroring how the production
/// partition serves its shard views.
/// </summary>
internal sealed class ItemKeyCorpus
{
    /// <summary>The splitmix64 stream increment: 2^64 divided by the golden ratio, made odd — the Weyl-sequence step that keeps successive states maximally spread through the state space (the same constant classic multiplicative hashing scales keys by).</summary>
    private const ulong GoldenIncrement = 0x9E3779B97F4A7C15UL;

    /// <summary>The first splitmix64 finalizer multiplier: odd (so the finalizer stays a bijection) and search-optimized for strict avalanche, so flipping any state bit flips each draw bit with probability near one half.</summary>
    private const ulong FinalizerMultiplierA = 0xBF58476D1CE4E5B9UL;

    /// <summary>The second splitmix64 finalizer multiplier, from the same avalanche-optimizing search; the paired xor-shifts feed high bits back down between the two upward-carrying multiplies.</summary>
    private const ulong FinalizerMultiplierB = 0x94D049BB133111EBUL;

    /// <summary>The corpus keys, each one a projected item of the width the corpus was built at.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Keys { get; }

    /// <summary>Wraps the built key handles.</summary>
    /// <param name="keys">The key handles.</param>
    private ItemKeyCorpus(IReadOnlyList<ReadOnlyMemory<byte>> keys)
    {
        Keys = keys;
    }

    /// <summary>Draws distinct uniform keys from a deterministic splitmix64 stream: each key is filled eight bytes per draw, deduplicated so a set-semantics consumer is never fed a repeat.</summary>
    /// <param name="count">The number of distinct keys.</param>
    /// <param name="seed">The stream seed.</param>
    /// <param name="keyWidth">The exact width of every key in bytes, a positive multiple of eight.</param>
    /// <returns>The corpus.</returns>
    public static ItemKeyCorpus Uniform(int count, ulong seed, int keyWidth)
    {
        byte[] backing = new byte[count * keyWidth];
        ReadOnlyMemory<byte>[] keys = new ReadOnlyMemory<byte>[count];
        HashSet<string> seen = [];
        ulong state = seed;
        int built = 0;
        Span<byte> candidate = stackalloc byte[keyWidth];
        while(built < count)
        {
            for(int offset = 0; offset < keyWidth; offset += sizeof(ulong))
            {
                state = SplitMix(state);
                BinaryPrimitives.WriteUInt64LittleEndian(candidate[offset..], state);
            }

            if(seen.Add(Convert.ToHexString(candidate)))
            {
                Memory<byte> slot = backing.AsMemory(built * keyWidth, keyWidth);
                candidate.CopyTo(slot.Span);
                keys[built] = slot;
                built++;
            }
        }

        return new ItemKeyCorpus(keys);
    }

    /// <summary>Builds real-layout structural keys over a small subject set: the subject cycles over <paramref name="distinctSubjects"/> values, keeping byte 0 — the shard prefix under identity mixing — to that few values, while the predicate counter keeps every key distinct. Packs subject into bits 0..31 and predicate into bits 32..63 of the low word (object in the high word, both words little-endian), exactly as the frozen structural projection and its content-key serialization do.</summary>
    /// <param name="count">The number of keys.</param>
    /// <param name="distinctSubjects">The number of distinct subject identifiers.</param>
    /// <param name="keyWidth">The exact width of every key in bytes, at least sixteen.</param>
    /// <returns>The corpus.</returns>
    public static ItemKeyCorpus StructuralShaped(int count, int distinctSubjects, int keyWidth)
    {
        byte[] backing = new byte[count * keyWidth];
        ReadOnlyMemory<byte>[] keys = new ReadOnlyMemory<byte>[count];
        for(int i = 0; i < count; i++)
        {
            uint subject = (uint)(i % distinctSubjects);
            uint predicate = (uint)(i / distinctSubjects);
            ulong low = subject | ((ulong)predicate << 32);
            Memory<byte> slot = backing.AsMemory(i * keyWidth, keyWidth);
            BinaryPrimitives.WriteUInt64LittleEndian(slot.Span[..8], low);
            BinaryPrimitives.WriteUInt64LittleEndian(slot.Span[8..], 0UL);
            keys[i] = slot;
        }

        return new ItemKeyCorpus(keys);
    }

    /// <summary>Composes this corpus followed by <paramref name="other"/> — the peer set that holds the survivors plus the loss.</summary>
    /// <param name="other">The corpus to append.</param>
    /// <returns>The composed corpus.</returns>
    public ItemKeyCorpus With(ItemKeyCorpus other)
    {
        ReadOnlyMemory<byte>[] keys = new ReadOnlyMemory<byte>[Keys.Count + other.Keys.Count];
        for(int i = 0; i < Keys.Count; i++)
        {
            keys[i] = Keys[i];
        }

        for(int i = 0; i < other.Keys.Count; i++)
        {
            keys[Keys.Count + i] = other.Keys[i];
        }

        return new ItemKeyCorpus(keys);
    }

    /// <summary>Advances a splitmix64 stream by one draw.</summary>
    /// <param name="state">The stream state.</param>
    /// <returns>The next draw.</returns>
    private static ulong SplitMix(ulong state)
    {
        ulong z = unchecked(state + GoldenIncrement);
        z = unchecked((z ^ (z >> 30)) * FinalizerMultiplierA);
        z = unchecked((z ^ (z >> 27)) * FinalizerMultiplierB);

        return z ^ (z >> 31);
    }
}
