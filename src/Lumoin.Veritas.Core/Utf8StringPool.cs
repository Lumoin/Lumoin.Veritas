using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core;

/// <summary>
/// An arena allocator that interns UTF-8 byte sequences and returns <see cref="Utf8String"/>
/// views over the pooled memory.
/// </summary>
/// <remarks>
/// <para>
/// All term memory for a graph should be allocated from a single pool. When the pool is disposed,
/// all <see cref="Utf8String"/> instances created from it become invalid. This enables bulk
/// disposal of graph memory without per-term GC pressure.
/// </para>
/// <para>
/// Duplicate byte sequences are interned: the pool returns the same <see cref="Utf8String"/>
/// for identical inputs. This is critical for RDF processing where terms like
/// <c>http://www.w3.org/1999/02/22-rdf-syntax-ns#type</c> appear in nearly every triple.
/// </para>
/// <para>
/// Arena buffers are rented from <see cref="VeritasMemoryPool{T}"/> for exact-size
/// allocation and proper lifecycle management via <see cref="IMemoryOwner{T}"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("Utf8StringPool: Count={Count}, TotalBytes={TotalBytesInterned}")]
public sealed class Utf8StringPool: IDisposable
{
    /// <summary>
    /// Default size for arena buffers rented from the memory pool.
    /// </summary>
    private const int DefaultBufferSize = 64 * 1024;

    /// <summary>
    /// The memory pool from which arena buffers are rented.
    /// </summary>
    private VeritasMemoryPool<byte> MemoryPool { get; }

    /// <summary>
    /// Whether this pool instance owns the <see cref="MemoryPool"/> and should
    /// dispose it when this pool is disposed.
    /// </summary>
    private bool OwnsPool { get; }

    /// <summary>
    /// All arena buffers rented from the memory pool during the lifetime of this pool.
    /// Disposed in bulk when this pool is disposed.
    /// </summary>
    private List<IMemoryOwner<byte>> RentedBuffers { get; } = [];

    /// <summary>
    /// Hash-based lookup table mapping precomputed hash codes to lists of interned strings
    /// that share the same hash. Collisions are resolved by byte-sequence comparison.
    /// </summary>
    private Dictionary<int, List<Utf8String>> InternTable { get; } = [];

    /// <summary>
    /// The currently active arena buffer owner.
    /// </summary>
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "CurrentOwner is tracked in RentedBuffers and disposed in bulk during Dispose.")]
    private IMemoryOwner<byte> CurrentOwner { get; set; }

    /// <summary>
    /// The memory region of the currently active arena buffer.
    /// </summary>
    private Memory<byte> CurrentBuffer { get; set; }

    /// <summary>
    /// The write position within the current arena buffer.
    /// </summary>
    private int Position { get; set; }

    /// <summary>
    /// Indicates whether this pool has been disposed.
    /// </summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Counter tracking total intern operations (hits + misses).
    /// Null when no meter is provided.
    /// </summary>
    private Counter<long>? InternOperationsCounter { get; }

    /// <summary>
    /// Counter tracking intern cache hits (existing string returned without allocation).
    /// Null when no meter is provided.
    /// </summary>
    private Counter<long>? InternHitsCounter { get; }

    /// <summary>
    /// Total bytes interned in this pool.
    /// </summary>
    private long TotalBytesInterned { get; set; }

    /// <summary>
    /// Initializes a new <see cref="Utf8StringPool"/> with the specified initial buffer size,
    /// using a default <see cref="VeritasMemoryPool{T}"/>.
    /// </summary>
    /// <param name="initialBufferSize">The initial buffer size in bytes. Defaults to 64 KB.</param>
    public Utf8StringPool(int initialBufferSize = DefaultBufferSize)
        : this(new VeritasMemoryPool<byte>(), ownsPool: true, initialBufferSize, meter: null)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="Utf8StringPool"/> with the specified memory pool and meter.
    /// </summary>
    /// <param name="memoryPool">The memory pool to rent arena buffers from.</param>
    /// <param name="initialBufferSize">The initial buffer size in bytes. Defaults to 64 KB.</param>
    /// <param name="meter">
    /// Optional meter for OTel metrics. When <see langword="null"/>, no metrics are recorded.
    /// </param>
    public Utf8StringPool(
        VeritasMemoryPool<byte> memoryPool,
        int initialBufferSize = DefaultBufferSize,
        Meter? meter = null)
        : this(memoryPool, ownsPool: false, initialBufferSize, meter)
    {
    }

    private Utf8StringPool(
        VeritasMemoryPool<byte> memoryPool,
        bool ownsPool,
        int initialBufferSize,
        Meter? meter)
    {
        ArgumentNullException.ThrowIfNull(memoryPool);

        MemoryPool = memoryPool;
        OwnsPool = ownsPool;

        CurrentOwner = memoryPool.Rent(initialBufferSize);
        CurrentBuffer = CurrentOwner.Memory;
        RentedBuffers.Add(CurrentOwner);

        if(meter is not null)
        {
            InternOperationsCounter = meter.CreateCounter<long>(
                VeritasMetrics.StringPoolInternOperationsTotal,
                "operations",
                "Total intern operations (hits + misses).");

            InternHitsCounter = meter.CreateCounter<long>(
                VeritasMetrics.StringPoolInternHitsTotal,
                "operations",
                "Intern cache hit count (existing string returned).");

            meter.CreateObservableUpDownCounter(
                VeritasMetrics.StringPoolUniqueCount,
                ObserveUniqueCount,
                "strings",
                "Number of unique strings interned in the pool.");

            meter.CreateObservableUpDownCounter(
                VeritasMetrics.StringPoolTotalBytesInterned,
                ObserveTotalBytesInterned,
                "bytes",
                "Total bytes interned in the pool.");
        }
    }

    /// <summary>Observes the pool's current count of unique interned strings.</summary>
    /// <returns>The number of unique strings interned.</returns>
    private int ObserveUniqueCount()
    {
        return Count;
    }

    /// <summary>Observes the total bytes interned in the pool.</summary>
    /// <returns>The total bytes interned.</returns>
    private long ObserveTotalBytesInterned()
    {
        return TotalBytesInterned;
    }

    /// <summary>
    /// Gets the total number of unique strings interned in this pool.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Interns a UTF-8 byte sequence, returning a <see cref="Utf8String"/> backed by pool memory.
    /// </summary>
    /// <remarks>
    /// If the same byte sequence has been interned before, the previously created
    /// <see cref="Utf8String"/> is returned without allocating new memory.
    /// </remarks>
    /// <param name="utf8Bytes">The UTF-8 bytes to intern.</param>
    /// <returns>An interned <see cref="Utf8String"/> over pool-managed memory.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public Utf8String Intern(ReadOnlySpan<byte> utf8Bytes)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        InternOperationsCounter?.Add(1);

        //Compute the hash to look up existing interned values.
        HashCode hash = new();
        hash.AddBytes(utf8Bytes);
        int hashCode = hash.ToHashCode();

        if(InternTable.TryGetValue(hashCode, out List<Utf8String>? candidates))
        {
            foreach(Utf8String candidate in candidates)
            {
                if(candidate.Span.SequenceEqual(utf8Bytes))
                {
                    InternHitsCounter?.Add(1);
                    return candidate;
                }
            }
        }

        //Not found. Allocate from the arena and intern.
        Utf8String interned = Allocate(utf8Bytes);

        if(candidates is null)
        {
            candidates = [];
            InternTable[hashCode] = candidates;
        }

        candidates.Add(interned);
        Count++;
        TotalBytesInterned += utf8Bytes.Length;

        return interned;
    }

    /// <summary>
    /// Interns a UTF-8 byte sequence that may span multiple segments, returning a
    /// <see cref="Utf8String"/> backed by pool memory.
    /// </summary>
    /// <remarks>
    /// A single-segment sequence is interned in place. A multi-segment sequence — a token that
    /// straddles pipe-buffer boundaries — is gathered into a pooled scratch buffer first, because
    /// interning hashes and compares over a contiguous span.
    /// </remarks>
    /// <param name="utf8Bytes">The UTF-8 bytes to intern.</param>
    /// <returns>An interned <see cref="Utf8String"/> over pool-managed memory.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public Utf8String Intern(ReadOnlySequence<byte> utf8Bytes)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        if(utf8Bytes.IsSingleSegment)
        {
            return Intern(utf8Bytes.FirstSpan);
        }

        //Gather the spanning bytes into a transient exact-size buffer from this pool's own
        //VeritasMemoryPool — never ArrayPool<byte>.Shared, whose over-allocation and process-wide
        //retention are exactly what the project pool exists to avoid in the interning path.
        int length = (int)utf8Bytes.Length;
        using IMemoryOwner<byte> scratch = MemoryPool.Rent(length);
        Span<byte> span = scratch.Memory.Span[..length];
        utf8Bytes.CopyTo(span);

        return Intern(span);
    }

    /// <summary>
    /// Interns a .NET string by encoding it as UTF-8.
    /// </summary>
    /// <param name="value">The string to intern.</param>
    /// <returns>An interned <see cref="Utf8String"/> over pool-managed memory.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public Utf8String Intern(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(Disposed, this);

        int maxByteCount = System.Text.Encoding.UTF8.GetMaxByteCount(value.Length);
        Span<byte> tempBuffer = maxByteCount <= 256
            ? stackalloc byte[maxByteCount]
            : new byte[maxByteCount];

        int written = System.Text.Encoding.UTF8.GetBytes(value, tempBuffer);

        return Intern(tempBuffer[..written]);
    }

    /// <summary>
    /// Rents a transient, exact-size scratch buffer from this pool's underlying
    /// <see cref="VeritasMemoryPool{T}"/>.
    /// </summary>
    /// <remarks>
    /// Lexers and serializers decode escape sequences and gather spanning tokens into a
    /// short-lived buffer before interning. Renting that buffer here keeps all of a document's
    /// byte memory in the one pool rather than reaching for a process-wide
    /// <see cref="ArrayPool{T}.Shared"/>, whose over-allocation and retention the project pool
    /// exists to avoid. The caller owns the returned buffer and must dispose it to return the
    /// memory to the pool.
    /// </remarks>
    /// <param name="length">The exact buffer length in bytes; must be greater than zero.</param>
    /// <returns>An <see cref="IMemoryOwner{T}"/> over a buffer of exactly <paramref name="length"/> bytes.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public IMemoryOwner<byte> RentScratch(int length)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        return MemoryPool.Rent(length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        Disposed = true;

        foreach(IMemoryOwner<byte> owner in RentedBuffers)
        {
            owner.Dispose();
        }

        RentedBuffers.Clear();
        InternTable.Clear();

        if(OwnsPool)
        {
            MemoryPool.Dispose();
        }
    }

    private Utf8String Allocate(ReadOnlySpan<byte> utf8Bytes)
    {
        EnsureCapacity(utf8Bytes.Length);

        utf8Bytes.CopyTo(CurrentBuffer.Span[Position..]);
        Memory<byte> slice = CurrentBuffer.Slice(Position, utf8Bytes.Length);
        Position += utf8Bytes.Length;

        return new Utf8String(slice);
    }

    private void EnsureCapacity(int requiredBytes)
    {
        if(Position + requiredBytes <= CurrentBuffer.Length)
        {
            return;
        }

        //Allocate a new buffer at least as large as the default or the required size.
        int newSize = Math.Max(DefaultBufferSize, requiredBytes);
        CurrentOwner = MemoryPool.Rent(newSize);
        CurrentBuffer = CurrentOwner.Memory;
        RentedBuffers.Add(CurrentOwner);
        Position = 0;
    }
}
