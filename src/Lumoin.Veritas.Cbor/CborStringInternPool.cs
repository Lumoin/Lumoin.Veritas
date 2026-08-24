using System;
using System.Collections.Generic;
using System.Text;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Intern pool for CBOR text-string values. When supplied to a
/// <see cref="CborReader"/> at construction, <see cref="CborReader.ReadTextString"/>
/// consults the pool before decoding each UTF-8 text string; repeats
/// of the same content return the same <see cref="string"/> instance,
/// skipping both the UTF-8 → UTF-16 decode and the allocation.
/// </summary>
/// <remarks>
/// <para>
/// Designed for workloads that decode many similar documents in
/// sequence — CARv1 / AT Protocol record streams, JSON-LD context
/// dictionaries, etc. — where map keys repeat heavily across blocks.
/// Lookup is span-keyed via
/// <see cref="Dictionary{TKey,TValue}.GetAlternateLookup{TAlternateKey}"/>
/// so the cache-hit path allocates nothing.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> One pool per thread or per logical stream.
/// A concurrent variant can be added if a real workload calls for it.
/// </para>
/// <para>
/// The pool grows monotonically. For unbounded streams with adversarial
/// content the pool can become a memory-pressure source; callers that
/// pin the pool to known schemas (e.g. preregister AT Protocol record
/// keys) avoid this.
/// </para>
/// </remarks>
public sealed class CborStringInternPool
{
    /// <summary>
    /// Default cap on the per-string byte length for entries the pool
    /// will cache. Strings longer than this skip the pool entirely; they
    /// pay the normal decode path but do not pollute the cache with
    /// likely-unique content (post text, URLs, base64 payloads).
    /// Map keys in JSON-LD, AT Protocol records, and CBOR-LD term tables
    /// are well under this length.
    /// </summary>
    public const int DefaultMaxInternedByteLength = 48;

    /// <summary>
    /// Default cap on the number of entries the pool will hold. Once the
    /// pool reaches this size, further first-sightings skip the pool and
    /// are decoded fresh. Cap chosen well above the AT Protocol
    /// record-key working set (~30 keys) plus typical content addresses
    /// (CIDs, DIDs) per repository; raise it for richer schemas.
    /// </summary>
    public const int DefaultMaxEntries = 4096;

    private readonly Dictionary<byte[], string> entries;
    private readonly Dictionary<byte[], string>.AlternateLookup<ReadOnlySpan<byte>> alternateLookup;
    private readonly int maxByteLength;
    private readonly int maxEntries;

    /// <summary>Initialises a new empty intern pool with the default size caps.</summary>
    public CborStringInternPool()
        : this(DefaultMaxInternedByteLength, DefaultMaxEntries)
    {
    }

    /// <summary>
    /// Initialises a new empty intern pool with explicit caps on the
    /// per-entry byte length and the total entry count.
    /// </summary>
    /// <param name="maxByteLength">Maximum UTF-8 byte length to intern; longer strings bypass the pool.</param>
    /// <param name="maxEntries">Maximum number of entries to retain; once full, further first-sightings bypass the pool.</param>
    public CborStringInternPool(int maxByteLength, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxByteLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        entries = new Dictionary<byte[], string>(Utf8ByteArrayComparer.Instance);
        alternateLookup = entries.GetAlternateLookup<ReadOnlySpan<byte>>();
        this.maxByteLength = maxByteLength;
        this.maxEntries = maxEntries;
    }

    /// <summary>Gets the number of unique strings currently interned.</summary>
    public int Count => entries.Count;

    /// <summary>
    /// Gets the per-entry byte-length cap. Strings longer than this
    /// bypass the pool entirely (no lookup attempted, no cache addition).
    /// </summary>
    public int MaxByteLength => maxByteLength;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="utf8Bytes"/> is already
    /// present in the pool.
    /// </summary>
    public bool Contains(ReadOnlySpan<byte> utf8Bytes) => alternateLookup.ContainsKey(utf8Bytes);

    /// <summary>
    /// Returns a cached <see cref="string"/> for <paramref name="utf8Bytes"/>
    /// if present, or <c>null</c> on miss. Callers wanting a single
    /// hash-only fast path use this; the inserted value can be added
    /// with <see cref="AddDecoded"/> after a strict decode.
    /// </summary>
    public string? TryGet(ReadOnlySpan<byte> utf8Bytes)
    {
        return alternateLookup.TryGetValue(utf8Bytes, out string? existing) ? existing : null;
    }

    /// <summary>
    /// Stores <paramref name="decoded"/> in the pool keyed by
    /// <paramref name="utf8Bytes"/>. Skips strings whose byte length
    /// exceeds the per-entry cap or whose addition would breach the
    /// total-entry cap.
    /// </summary>
    /// <param name="utf8Bytes">The UTF-8 source bytes (must match <paramref name="decoded"/>).</param>
    /// <param name="decoded">The already-decoded string instance to cache.</param>
    /// <returns><c>true</c> if the entry was added; <c>false</c> if it was rejected by a cap.</returns>
    public bool AddDecoded(ReadOnlySpan<byte> utf8Bytes, string decoded)
    {
        if(utf8Bytes.Length > maxByteLength || entries.Count >= maxEntries)
        {
            return false;
        }

        byte[] keyCopy = utf8Bytes.ToArray();
        entries[keyCopy] = decoded;
        return true;
    }

    /// <summary>
    /// Returns the canonical <see cref="string"/> for the given UTF-8
    /// bytes. First sighting decodes UTF-8 (non-validating) and copies
    /// the bytes into the pool if size caps permit; subsequent sightings
    /// of the same content return the cached instance.
    /// </summary>
    /// <param name="utf8Bytes">The UTF-8 encoded text-string content.</param>
    /// <returns>The canonical string instance.</returns>
    public string Intern(ReadOnlySpan<byte> utf8Bytes)
    {
        if(alternateLookup.TryGetValue(utf8Bytes, out string? existing))
        {
            return existing;
        }

        string value = Encoding.UTF8.GetString(utf8Bytes);
        if(utf8Bytes.Length <= maxByteLength && entries.Count < maxEntries)
        {
            byte[] keyCopy = utf8Bytes.ToArray();
            entries[keyCopy] = value;
        }

        return value;
    }

    /// <summary>
    /// Pre-seeds the pool with a known string. Useful for application
    /// schemas where the key set is fixed (e.g. AT Protocol record
    /// field names) — interning the keys at startup makes every
    /// subsequent lookup a hit. Preseeding bypasses the per-entry
    /// length and entry-count caps so callers can guarantee specific
    /// strings are always present.
    /// </summary>
    /// <param name="value">The string to intern.</param>
    /// <returns>The canonical instance — equal to <paramref name="value"/> on first call.</returns>
    public string Preseed(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        if(alternateLookup.TryGetValue(utf8, out string? existing))
        {
            return existing;
        }

        entries[utf8] = value;

        return value;
    }
}

/// <summary>
/// Equality comparer for UTF-8 byte arrays that also supports span-keyed
/// lookup. Used by <see cref="CborStringInternPool"/> so the cache-hit
/// path is allocation-free.
/// </summary>
internal sealed class Utf8ByteArrayComparer
    : IEqualityComparer<byte[]>,
      IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
{
    public static Utf8ByteArrayComparer Instance { get; } = new();

    public bool Equals(byte[]? x, byte[]? y)
    {
        if(ReferenceEquals(x, y))
        {
            return true;
        }
        if(x is null || y is null)
        {
            return false;
        }

        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode(byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        HashCode hc = default;
        hc.AddBytes(obj);

        return hc.ToHashCode();
    }

    public bool Equals(ReadOnlySpan<byte> alternate, byte[] other)
    {
        return other is not null && alternate.SequenceEqual(other);
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        HashCode hc = default;
        hc.AddBytes(alternate);

        return hc.ToHashCode();
    }

    public byte[] Create(ReadOnlySpan<byte> alternate)
    {
        return alternate.ToArray();
    }
}
