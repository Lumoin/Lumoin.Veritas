using System;
using System.Buffers;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Verisync.Core;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The durable home of one host's metadata-plane state, kept as files in a host-supplied directory. It holds
/// two artifacts: the consensus host's node state — persisted through <see cref="PersistNode"/>, the
/// <see cref="PersistVersionedNodeDelegate{TValue}"/> the consensus runner drives — and this host's
/// <see cref="ConfirmedMetadataFacts"/> record. Every write stages to a temporary file in the SAME directory,
/// flushes those bytes to stable storage, and makes them live by an atomic same-directory rename, so a write
/// is durable before it returns and a crash leaves the prior state wholly in force or the new state wholly
/// live.
/// </summary>
/// <remarks>
/// <para>
/// THE TWO ARTIFACTS DIFFER IN KIND, and that is why only one takes a codec seam. The node state carries the
/// coordinated record the whole deployment agrees on, so its encoding is the host's choice and arrives as an
/// injected serializer/deserializer pair — the library stays serialization-agnostic. The confirmed-facts
/// record is this host's own memory of what consensus already settled; it never crosses a wire, so it carries
/// its own fixed binary layout and no codec dependency at all (see <see cref="ConfirmedMetadataFacts"/>).
/// </para>
/// <para>
/// WHAT THIS STORE CHECKS, AND WHAT IT DOES NOT. A missing node-state file is a VALUE: it means a fresh host,
/// and <see cref="TryLoadAsync"/> reports it as <see langword="null"/> rather than as a failure. A file that
/// is present but unreadable is an invariant violation and is surfaced loudly: the codec's own refusal
/// propagates, as does a short read or a length no state this codec writes could have. The store adds NO
/// relational checks of its own — whether the restored leader, version, and membership agree with the record
/// they are stored beside is the restoring host's question, answered once by
/// <see cref="QuePaxaVersionedNode{TValue}"/>'s <c>FromState</c>, whose torn-snapshot refusal is the safety
/// net. A second copy of that reasoning here could only drift from it.
/// </para>
/// <para>
/// LOADING IS NOT CONSTRUCTION. A restore reads a file, so it is asynchronous and cannot happen in a
/// constructor: the composition root constructs the store, awaits <see cref="TryLoadAsync"/>, and hands the
/// restored state (or <see langword="null"/>) to <see cref="QuePaxaVersionedNode{TValue}"/>'s
/// <c>FromState</c> or to its fresh-host constructor.
/// </para>
/// <para>
/// THE DURABILITY POINT IS ONE SYNCHRONOUS CALL. The bytes are written through the asynchronous positional
/// write path, and the rename is a metadata operation, but flushing a handle to stable storage has no
/// asynchronous form in the runtime — so the injected <see cref="DurableFlushDelegate"/> is invoked
/// synchronously, once per write, and that call is the point the durability contract of
/// <see cref="PersistVersionedNodeDelegate{TValue}"/> rests on. The rename and the directory barrier that
/// follow are likewise synchronous for the same reason. None of this is sync-over-async: no asynchronous work
/// is being blocked on, these primitives simply have no asynchronous form.
/// </para>
/// <para>
/// ONE WRITER PER ARTIFACT. Each artifact has ONE staged name rather than a per-call unique one, because each
/// has a single writer: the consensus runner's loop is the sole caller of the persist face, and a host's
/// coordination path is the sole writer of its confirmed facts. A staged file left behind by a crash is
/// truncated by the next write of the same artifact and never observed by a reader, which reads only the live
/// name.
/// </para>
/// <para>
/// Host-only: there is no file system to publish into in a browser runtime, matching the atomic-publish
/// primitives this store commits through.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class MetadataNodeStore
{
    /// <summary>The live file name of the consensus host's node state.</summary>
    public const string NodeStateFileName = "metadata-node.state";

    /// <summary>The live file name of this host's confirmed-facts record.</summary>
    public const string ConfirmedFactsFileName = "metadata-facts.bin";

    /// <summary>The live file name of this store's own incarnation, minted once when the store is created.</summary>
    public const string IncarnationFileName = "metadata-store.id";

    /// <summary>The staged file name a node-state write is written and flushed under before the rename makes it live.</summary>
    private const string StagedNodeStateFileName = "metadata-node.state.staged";

    /// <summary>The staged file name a confirmed-facts write is written and flushed under before the rename makes it live.</summary>
    private const string StagedConfirmedFactsFileName = "metadata-facts.bin.staged";

    /// <summary>The staged file name the incarnation is written and flushed under before the rename makes it live.</summary>
    private const string StagedIncarnationFileName = "metadata-store.id.staged";

    /// <summary>The byte length of each slab the node-state serializer writes into; a coordinated control-plane record is small, so a single slab typically carries a whole encoded state and the writer rents a second one only when it does not.</summary>
    private const int SerializeSlabSize = 4096;

    /// <summary>
    /// The persist face the consensus runner is started with — a cached binding of
    /// <see cref="PersistAsync"/>, so handing the same store to the runner twice costs no fresh delegate.
    /// </summary>
    public PersistVersionedNodeDelegate<VeritasMetadataRecord> PersistNode { get; }

    /// <summary>The directory the two artifacts live in; created when this store is constructed.</summary>
    public string DirectoryPath { get; }

    /// <summary>The pool every transient serialize, read, and layout buffer is rented from; the store holds no pooled buffer of its own between calls.</summary>
    internal MemoryPool<byte> Pool { get; }

    /// <summary>The encoder the node state is written with; injected so the library carries no serialization dependency.</summary>
    private SerializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> SerializeState { get; }

    /// <summary>The decoder the node state is read back with; the counterpart of <see cref="SerializeState"/>, and the one place a malformed state is refused.</summary>
    private DeserializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> DeserializeState { get; }

    /// <summary>The file-content durability flush applied to a staged write before it is made live.</summary>
    private DurableFlushDelegate Flush { get; }

    /// <summary>The directory durability barrier applied after the rename that makes a staged write live.</summary>
    private DurabilityBarrierDelegate Barrier { get; }

    /// <summary>
    /// The persist face a planeless or ephemeral host passes instead of a store: it makes nothing durable and
    /// completes at once, reproducing the in-memory behavior of a host that is not expected to survive a
    /// restart. It captures nothing, so it holds no lexical closure.
    /// </summary>
    public static PersistVersionedNodeDelegate<VeritasMetadataRecord> NoDurability { get; } = static (_, _) => ValueTask.CompletedTask;

    /// <summary>Creates a store over a directory, creating the directory if it does not exist.</summary>
    /// <param name="directoryPath">The directory the node state and the confirmed-facts record live in.</param>
    /// <param name="pool">The pool every transient serialize, read, and layout buffer is rented from.</param>
    /// <param name="serializeState">The encoder the node state is written with.</param>
    /// <param name="deserializeState">The decoder the node state is read back with.</param>
    /// <param name="flush">The file-content durability flush applied to a staged write; <see langword="null"/> uses <see cref="AtomicPublish.DefaultFlush"/>, the production per-host flush. A fault-injection harness substitutes a no-op or failing flush to exercise the degraded path without a real crash.</param>
    /// <param name="barrier">The directory durability barrier applied after the rename; <see langword="null"/> uses <see cref="AtomicPublish.DefaultBarrier"/>, the production per-host barrier.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="directoryPath"/> is empty or white space.</exception>
    public MetadataNodeStore(
        string directoryPath,
        MemoryPool<byte> pool,
        SerializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> serializeState,
        DeserializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> deserializeState,
        DurableFlushDelegate? flush = null,
        DurabilityBarrierDelegate? barrier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(serializeState);
        ArgumentNullException.ThrowIfNull(deserializeState);

        DirectoryPath = directoryPath;
        Pool = pool;
        SerializeState = serializeState;
        DeserializeState = deserializeState;
        Flush = flush ?? AtomicPublish.DefaultFlush;
        Barrier = barrier ?? AtomicPublish.DefaultBarrier;
        PersistNode = PersistAsync;

        Directory.CreateDirectory(directoryPath);
    }

    /// <summary>
    /// Persists the consensus host's state durably: it encodes the state through the injected serializer,
    /// stages the bytes in this store's directory, flushes them to stable storage, and makes them live by an
    /// atomic same-directory rename. It does not return until the state is durable, which is what the
    /// consensus host's own contract requires of a persist face — every reply that depends on the state
    /// leaves the process after this completes.
    /// </summary>
    /// <param name="state">The host state to make durable.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes once <paramref name="state"/> is durable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The staged write, the flush, or the rename failed. The consensus runner treats a throwing persist as fail-closed: the call the write belonged to is faulted and the loop ends, rather than a reply going out over state that was never made durable.</exception>
    public async ValueTask PersistAsync(QuePaxaVersionedNodeState<VeritasMetadataRecord> state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        using SlabBufferWriter writer = new(Pool, SerializeSlabSize);
        SerializeState(state, writer);
        int stateLength = writer.BytesWritten;

        //The detach concatenates the written slabs into one exact-length pooled buffer and returns the slabs to
        //the pool, so the writer holds nothing across the write below and its own disposal has nothing left to
        //release.
        using IMemoryOwner<byte> stateOwner = writer.Detach();

        await WriteDurablyAsync(StagedNodeStateFileName, NodeStateFileName, stateOwner.Memory[..stateLength], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the persisted consensus host state, or reports that this host is fresh. A missing file is the
    /// fresh-host answer and is reported as <see langword="null"/>; a file that is present is decoded through
    /// the injected decoder, whose refusal of a malformed payload propagates unchanged.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The restored host state, or <see langword="null"/> when no state has been persisted.</returns>
    /// <exception cref="InvalidDataException">The state file ended early under the read (it was truncated or is being written by a second writer), or it is longer than any state a single buffer can hold — both are corruption of a store whose writes are atomic, so they are surfaced rather than mapped to the fresh-host answer.</exception>
    /// <exception cref="MessageDeserializationException">The stored payload is not a well-formed encoded host state. A torn store is an invariant violation and is loud; the relational question — whether a well-formed state's leader, version, and membership agree with the record they are stored beside — belongs to <see cref="QuePaxaVersionedNode{TValue}"/>'s <c>FromState</c> alone, which the caller hands this result to.</exception>
    public async ValueTask<QuePaxaVersionedNodeState<VeritasMetadataRecord>?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(DirectoryPath, NodeStateFileName);

        //A using declaration over the nullable handle disposes on every path; disposing null is a no-op.
        using SafeFileHandle? handle = TryOpenForRead(path);
        if(handle is null)
        {
            //No state has been persisted: this is a fresh host, not a failure.
            return null;
        }

        long length = RandomAccess.GetLength(handle);
        if(length > Array.MaxLength)
        {
            throw new InvalidDataException("The stored metadata node state is longer than any state this store writes, so the file is not a node state this build produced.");
        }

        if(length == 0)
        {
            //An empty live file cannot come from an atomic publish of an encoded state; handing the empty
            //payload to the decoder makes its own fail-closed refusal the single place a bad payload is
            //named.
            return DeserializeState(ReadOnlySequence<byte>.Empty);
        }

        int stateLength = (int)length;
        using IMemoryOwner<byte> stateOwner = Pool.Rent(stateLength);
        Memory<byte> state = stateOwner.Memory[..stateLength];
        await FillAsync(handle, state, cancellationToken).ConfigureAwait(false);

        return DeserializeState(new ReadOnlySequence<byte>(state));
    }

    /// <summary>
    /// This store's own incarnation: read back when the store already holds one, and minted, written durably
    /// and returned when it does not.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read or the write.</param>
    /// <returns>The incarnation this store answers under for as long as its contents survive.</returns>
    /// <exception cref="InvalidDataException">The incarnation file is present and is not exactly one incarnation wide, which no write of this store produces.</exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE FIRST PHASE OF PROVISIONING. A membership admits a store and not merely a replica, so a
    /// founder list cannot be written before the stores it names exist: an operator creates each store, reads
    /// the incarnation out of it with this, forms the list from the pairs, and only then starts the hosts
    /// under it. <see cref="MetadataFounder"/> is that pair.
    /// </para>
    /// <para>
    /// It is a marker of its own rather than a field of the node state, and the separation is what makes the
    /// host's restore check mean anything. A store that has never been made durable holds no node state at
    /// all, and a host still has to know which store it is before it can answer as one; keeping the marker
    /// apart also leaves the consensus host's restore comparing two artifacts written at different times
    /// rather than comparing a value with itself. A store wiped between runs comes back without the marker,
    /// mints a new one here, and is then refused by a membership that admits the incarnation it lost — which
    /// is the whole of what the binding buys.
    /// </para>
    /// </remarks>
    public async ValueTask<StoreIncarnation> EnsureIncarnationAsync(CancellationToken cancellationToken = default)
    {
        using IMemoryOwner<byte> owner = Pool.Rent(StoreIncarnation.Size);
        Memory<byte> buffer = owner.Memory[..StoreIncarnation.Size];

        if(await TryReadExactAsync(IncarnationFileName, buffer, cancellationToken).ConfigureAwait(false))
        {
            return StoreIncarnation.FromSpan(buffer.Span);
        }

        //Minted here and never derived from the replica identity: an incarnation a caller could recompute from
        //the identity an operator hands out is one a wiped store could present again, and the binding would
        //then be decoration.
        StoreIncarnation minted = StoreIncarnation.Generate();
        minted.CopyTo(buffer.Span);
        await WriteDurablyAsync(StagedIncarnationFileName, IncarnationFileName, buffer, cancellationToken).ConfigureAwait(false);

        return minted;
    }

    /// <summary>Writes this host's confirmed-facts record durably, under the store's own artifact name and atomicity.</summary>
    /// <param name="content">The encoded record; exactly <see cref="ConfirmedMetadataFacts.SerializedLength"/> bytes.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes once the record is durable.</returns>
    internal ValueTask WriteConfirmedFactsAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        return WriteDurablyAsync(StagedConfirmedFactsFileName, ConfirmedFactsFileName, content, cancellationToken);
    }

    /// <summary>Reads this host's confirmed-facts record, or reports that none has been written.</summary>
    /// <param name="destination">The buffer the record is read into; exactly <see cref="ConfirmedMetadataFacts.SerializedLength"/> bytes.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns><see langword="true"/> when the record was read; <see langword="false"/> when no record has been written.</returns>
    internal ValueTask<bool> TryReadExactAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        return TryReadExactAsync(ConfirmedFactsFileName, destination, cancellationToken);
    }

    /// <summary>
    /// Writes one artifact durably: stage, flush, atomic same-directory rename. The staged name is truncated
    /// and rewritten on every call, so a staged file a crash left behind is replaced rather than resumed, and
    /// the live name only ever names a whole, flushed artifact.
    /// </summary>
    /// <param name="stagedFileName">The staged name the bytes are written and flushed under.</param>
    /// <param name="liveFileName">The live name the rename publishes them to.</param>
    /// <param name="content">The bytes to write.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes once the artifact is durable under its live name.</returns>
    /// <exception cref="IOException">The staged write, the flush, or the rename failed.</exception>
    private async ValueTask WriteDurablyAsync(string stagedFileName, string liveFileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        string stagedPath = Path.Combine(DirectoryPath, stagedFileName);
        string livePath = Path.Combine(DirectoryPath, liveFileName);

        //The handle is closed before the rename: a rename over an open source handle is refused on some hosts,
        //so the scope ends here rather than at the end of the method.
        using(SafeFileHandle handle = File.OpenHandle(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous))
        {
            await RandomAccess.WriteAsync(handle, content, 0, cancellationToken).ConfigureAwait(false);

            //The durability point. Flushing a handle's bytes to stable storage has no asynchronous form, so
            //this one call is synchronous by necessity, and it is what lets this method's completion mean the
            //bytes survive a power loss rather than that they reached a cache.
            Flush(handle);
        }

        //The rename is the commit point and the barrier is what makes it durable where a host exposes the
        //call; neither has an asynchronous form either.
        AtomicPublish.Publish(stagedPath, livePath, Barrier);
    }

    /// <summary>Reads an artifact of an exactly known length, or reports that it does not exist.</summary>
    /// <param name="fileName">The live artifact name.</param>
    /// <param name="destination">The buffer the artifact is read into; its length is the length the artifact must have.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns><see langword="true"/> when the artifact was read; <see langword="false"/> when it does not exist.</returns>
    /// <exception cref="InvalidDataException">The artifact's length differs from <paramref name="destination"/>'s, or it ended early under the read. A fixed-layout artifact published atomically has exactly one length, so any other length is a foreign or torn file rather than a shorter record.</exception>
    private async ValueTask<bool> TryReadExactAsync(string fileName, Memory<byte> destination, CancellationToken cancellationToken)
    {
        string path = Path.Combine(DirectoryPath, fileName);

        //A using declaration over the nullable handle disposes on every path; disposing null is a no-op.
        using SafeFileHandle? handle = TryOpenForRead(path);
        if(handle is null)
        {
            return false;
        }

        if(RandomAccess.GetLength(handle) != destination.Length)
        {
            throw new InvalidDataException("A stored metadata-plane artifact has a length its fixed layout cannot produce, so it is refused rather than read as a shorter or longer record.");
        }

        await FillAsync(handle, destination, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>Opens a live artifact for reading, or reports its absence as <see langword="null"/> rather than as a failure.</summary>
    /// <param name="path">The full path of the artifact.</param>
    /// <returns>The open handle, or <see langword="null"/> when the artifact (or the directory) does not exist.</returns>
    private static SafeFileHandle? TryOpenForRead(string path)
    {
        try
        {
            //FileShare.Delete lets a concurrent publish or cleanup proceed against this read handle.
            return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileOptions.Asynchronous);
        }
        catch(FileNotFoundException)
        {
            return null;
        }
        catch(DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes from the start of an artifact, looping over partial positional reads.</summary>
    /// <param name="handle">The open artifact handle.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>A task that completes once the buffer is filled.</returns>
    /// <exception cref="InvalidDataException">The artifact ended before the buffer was filled.</exception>
    private static async ValueTask FillAsync(SafeFileHandle handle, Memory<byte> destination, CancellationToken cancellationToken)
    {
        int filled = 0;
        while(filled < destination.Length)
        {
            int read = await RandomAccess.ReadAsync(handle, destination[filled..], filled, cancellationToken).ConfigureAwait(false);
            if(read == 0)
            {
                throw new InvalidDataException("A stored metadata-plane artifact ended before the length it reported was read, so the file is truncated or is being rewritten by a second writer.");
            }

            filled += read;
        }
    }
}
