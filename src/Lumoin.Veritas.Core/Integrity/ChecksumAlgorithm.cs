using System;
using System.IO.Hashing;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A pluggable, explicitly-selected checksum algorithm for the persistence container's integrity
/// layer: a stable on-disk id, a fixed byte width, and the compute function. Built-ins are exposed
/// as named static instances and resolved by <see cref="DefaultResolver"/>; a deployment adds its
/// own with <see cref="Create"/> and an injected <see cref="ResolveChecksumAlgorithmDelegate"/> that
/// maps the id on read. The id is a wire contract — once an image is written with an id, that id
/// must keep meaning the same algorithm.
/// </summary>
/// <remarks>
/// The on-disk id inventory: 0 is reserved for "no checksum" and is never a valid algorithm id; 1 is
/// <see cref="XxHash3"/> (8-byte, the default); 2 is <see cref="Crc32"/> (4-byte); 3
/// (<see cref="KeyedHmacSha256Id"/>) and 4 (<see cref="KeyedBlake2b256Id"/>) are reserved for keyed
/// message-authentication tags a host composes with a key and injects through its own resolver. The
/// built-in <see cref="DefaultResolver"/> resolves only 1 and 2; the keyed ids resolve to
/// <see langword="null"/> under it, so a keyless composition refuses a keyed image rather than
/// downgrading it to an unkeyed check. Refusal-never-downgrade is enforced structurally, not by
/// convention: <see cref="Create"/> refuses the reserved keyed ids (only <see cref="CreateKeyed"/>
/// constructs them, marking <see cref="IsKeyed"/> and binding the tag width), and every reader
/// resolves through <see cref="ResolveForRead"/>, which refuses a resolver whose answer misstates its
/// identity or fails to witness the keyed construction a reserved id demands. The keyed witness is
/// scoped to the reserved ids: a deployment's own keyed id gets id-identity protection but no
/// keyedness assertion — the reader cannot know a custom id is keyed, so custom keyed ids rely on the
/// deployment's own resolver policy.
/// </remarks>
public sealed class ChecksumAlgorithm
{
    /// <summary>The largest checksum width this layer admits, bounding the on-load verify buffer.</summary>
    public const int MaximumByteWidth = 64;

    /// <summary>
    /// The reserved on-disk id of the keyed HMAC-SHA-256 message-authentication tag (a 32-byte tag). This id
    /// permanently binds that construction and that tag width: a different width or a different keyed
    /// construction takes a NEW id, never a renumber of this one. A host builds the algorithm with
    /// <see cref="Create"/>, closing its <see cref="ChecksumComputeDelegate"/> over the key at the composition
    /// root so the key never crosses this type or the on-disk format, and injects a
    /// <see cref="ResolveChecksumAlgorithmDelegate"/> that maps this id to that algorithm ONLY when the key is
    /// present. A resolver must NEVER map this id to a keyless fallback: refusing an image whose key is absent
    /// is the contract (refusal-never-downgrade). Key epoch and key-check values ride the planned sealed-format
    /// headers, not this checksum rung. The built-in <see cref="DefaultResolver"/> does not resolve this id.
    /// </summary>
    public const byte KeyedHmacSha256Id = 3;

    /// <summary>
    /// The reserved on-disk id of the keyed BLAKE2b-256 message-authentication tag (a 32-byte tag). This id
    /// permanently binds that construction and that tag width: a different width or a different keyed
    /// construction takes a NEW id, never a renumber of this one. A host builds the algorithm with
    /// <see cref="Create"/>, closing its <see cref="ChecksumComputeDelegate"/> over the key at the composition
    /// root so the key never crosses this type or the on-disk format, and injects a
    /// <see cref="ResolveChecksumAlgorithmDelegate"/> that maps this id to that algorithm ONLY when the key is
    /// present. A resolver must NEVER map this id to a keyless fallback: refusing an image whose key is absent
    /// is the contract (refusal-never-downgrade). Key epoch and key-check values ride the planned sealed-format
    /// headers, not this checksum rung. The built-in <see cref="DefaultResolver"/> does not resolve this id.
    /// </summary>
    public const byte KeyedBlake2b256Id = 4;

    /// <summary>The tag width in bytes the reserved keyed ids permanently bind; a different width is a new id, never a renumber.</summary>
    public const int ReservedKeyedByteWidth = 32;

    /// <summary>The stable on-disk algorithm id; 0 is reserved for "no checksum" and is not a valid algorithm id.</summary>
    public byte Id { get; }

    /// <summary>A short human-readable name.</summary>
    public string Name { get; }

    /// <summary>The fixed checksum width in bytes, in <c>[1, <see cref="MaximumByteWidth"/>]</c>.</summary>
    public int ByteWidth { get; }

    /// <summary>Computes the checksum of a span into a <see cref="ByteWidth"/>-byte destination.</summary>
    public ChecksumComputeDelegate Compute { get; }

    /// <summary>Opens an incremental session for a windowed verification, or <see langword="null"/> when the algorithm is one-shot only — its OPTIONAL streaming capability. A verification whose artifact exceeds a single span's range and whose algorithm carries no session FAILS CLOSED rather than passing unverified.</summary>
    public CreateChecksumSessionDelegate? CreateSession { get; }

    /// <summary>Whether this algorithm witnesses a keyed message-authentication construction. Only <see cref="CreateKeyed"/> marks it, so a reader asserting the marker on a reserved keyed id refuses a keyless substitute — the declared capability the resolution witness checks, not an inspection of key material, which never crosses this type.</summary>
    public bool IsKeyed { get; }

    private ChecksumAlgorithm(byte id, string name, int byteWidth, ChecksumComputeDelegate compute, CreateChecksumSessionDelegate? createSession, bool isKeyed)
    {
        if(id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Checksum-algorithm id 0 is reserved for no checksum.");
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(byteWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteWidth, MaximumByteWidth);
        ArgumentNullException.ThrowIfNull(compute);

        Id = id;
        Name = name;
        ByteWidth = byteWidth;
        Compute = compute;
        CreateSession = createSession;
        IsKeyed = isKeyed;
    }

    /// <summary>Creates a custom checksum algorithm; the deployment is responsible for a globally-unique id and for resolving it on read. The reserved keyed ids are refused here — only <see cref="CreateKeyed"/> constructs them, so a keyless compute can never carry a reserved keyed id.</summary>
    /// <param name="id">The stable on-disk id (non-zero, not a reserved keyed id).</param>
    /// <param name="name">A short human-readable name.</param>
    /// <param name="byteWidth">The fixed checksum width in bytes, in <c>[1, <see cref="MaximumByteWidth"/>]</c>.</param>
    /// <param name="compute">The compute function.</param>
    /// <param name="createSession">The incremental-session factory, or <see langword="null"/> for a one-shot-only algorithm that cannot verify an artifact past a single span's range.</param>
    /// <returns>The algorithm.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is a reserved keyed id (<see cref="IsReservedKeyedId"/>).</exception>
    public static ChecksumAlgorithm Create(byte id, string name, int byteWidth, ChecksumComputeDelegate compute, CreateChecksumSessionDelegate? createSession = null)
    {
        if(IsReservedKeyedId(id))
        {
            throw new ArgumentException($"Checksum-algorithm id {id} is reserved for a keyed construction; build it with {nameof(CreateKeyed)} so the algorithm witnesses its keyed capability.", nameof(id));
        }

        return new ChecksumAlgorithm(id, name, byteWidth, compute, createSession, isKeyed: false);
    }

    /// <summary>
    /// Creates a keyed message-authentication algorithm — the ONLY construction that marks
    /// <see cref="IsKeyed"/>. The compute delegate closes over the key at the composition root, so the key never
    /// crosses this type or the on-disk format; because that closure is opaque, the marker is a DECLARED
    /// capability: the caller asserts the compute — and any <paramref name="createSession"/> factory, which must
    /// implement the same keyed construction — actually holds the key. A reserved keyed id
    /// (<see cref="IsReservedKeyedId"/>) permanently binds the <see cref="ReservedKeyedByteWidth"/>-byte tag the
    /// id documents, enforced here; a custom keyed id takes any admitted width under the deployment's own policy.
    /// </summary>
    /// <param name="id">The stable on-disk id (non-zero); a reserved keyed id or the deployment's own.</param>
    /// <param name="name">A short human-readable name.</param>
    /// <param name="byteWidth">The fixed tag width in bytes; exactly <see cref="ReservedKeyedByteWidth"/> for a reserved keyed id.</param>
    /// <param name="compute">The keyed compute function, closed over its key.</param>
    /// <param name="createSession">The incremental-session factory implementing the same keyed construction, or <see langword="null"/> for a one-shot-only algorithm.</param>
    /// <returns>The keyed-marked algorithm.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a reserved keyed id is given a width other than <see cref="ReservedKeyedByteWidth"/>.</exception>
    public static ChecksumAlgorithm CreateKeyed(byte id, string name, int byteWidth, ChecksumComputeDelegate compute, CreateChecksumSessionDelegate? createSession = null)
    {
        if(IsReservedKeyedId(id) && byteWidth != ReservedKeyedByteWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(byteWidth), byteWidth, $"Reserved keyed checksum id {id} permanently binds a {ReservedKeyedByteWidth}-byte tag; a different width is a new id, never a renumber.");
        }

        return new ChecksumAlgorithm(id, name, byteWidth, compute, createSession, isKeyed: true);
    }

    /// <summary>Reports whether <paramref name="id"/> is one of the project-reserved keyed ids — the single place the reserved set lives, consulted by both the construction gate and the read-side resolution witness.</summary>
    /// <param name="id">The on-disk id.</param>
    /// <returns>Whether the id is reserved for a keyed construction.</returns>
    public static bool IsReservedKeyedId(byte id)
    {
        return id is KeyedHmacSha256Id or KeyedBlake2b256Id;
    }

    /// <summary>
    /// The witnessed resolution seam every reader resolves through: the resolved algorithm must witness the
    /// identity the image demands (the same id) and, for a reserved keyed id, the keyed capability and tag width
    /// the id permanently binds — or the read refuses loudly. A reader never invokes a raw
    /// <see cref="ResolveChecksumAlgorithmDelegate"/> itself. The witness checks DECLARED capability
    /// (<see cref="IsKeyed"/>), scoped to the reserved ids: a deployment's own keyed id gets id-identity
    /// protection but no keyedness assertion — the reader has no universal way to know a custom id is keyed, so
    /// custom keyed ids rely on the deployment's own resolver policy.
    /// </summary>
    /// <param name="id">The on-disk checksum-algorithm id the image records.</param>
    /// <param name="resolver">The read-side resolver; <see langword="null"/> uses <see cref="DefaultResolver"/>.</param>
    /// <param name="artifactDescription">A short human-readable name of the artifact kind for the diagnostics (e.g. "item segment").</param>
    /// <returns>The witnessed algorithm.</returns>
    /// <exception cref="NotSupportedException">The resolver does not resolve <paramref name="id"/> — an unsupported image, the legitimate version-skew refusal.</exception>
    /// <exception cref="InvalidOperationException">The resolver answered with a different algorithm than the id names, or a reserved keyed id resolved to an algorithm that does not witness the keyed construction — a broken or hostile composition, refused before any byte is verified (refusal-never-downgrade).</exception>
    public static ChecksumAlgorithm ResolveForRead(byte id, ResolveChecksumAlgorithmDelegate? resolver, string artifactDescription)
    {
        ChecksumAlgorithm? resolved = (resolver ?? DefaultResolver)(id);
        if(resolved is null)
        {
            throw new NotSupportedException($"The {artifactDescription} uses checksum algorithm id {id}, which this reader does not resolve.");
        }

        if(resolved.Id != id)
        {
            throw new InvalidOperationException($"The checksum resolver answered id {id} for the {artifactDescription} with algorithm '{resolved.Name}' (id {resolved.Id}) — a broken or hostile composition, refused before any byte is verified.");
        }

        if(IsReservedKeyedId(id) && (!resolved.IsKeyed || resolved.ByteWidth != ReservedKeyedByteWidth))
        {
            throw new InvalidOperationException($"Reserved keyed checksum id {id} on the {artifactDescription} resolved to '{resolved.Name}', which does not witness the keyed {ReservedKeyedByteWidth}-byte construction the id permanently binds — refused rather than downgraded.");
        }

        return resolved;
    }

    /// <summary>xxHash3 (64-bit) — the default: fast, well-distributed, non-cryptographic.</summary>
    public static ChecksumAlgorithm XxHash3 { get; } = new(1, "XxHash3-64", sizeof(ulong), ComputeXxHash3, CreateXxHash3Session, isKeyed: false);

    /// <summary>CRC-32 — a selectable narrower alternative.</summary>
    public static ChecksumAlgorithm Crc32 { get; } = new(2, "CRC-32", sizeof(uint), ComputeCrc32, CreateCrc32Session, isKeyed: false);

    /// <summary>Resolves the built-in algorithms by id; returns <see langword="null"/> for an unknown or the reserved-none id.</summary>
    public static ResolveChecksumAlgorithmDelegate DefaultResolver { get; } = ResolveBuiltIn;

    /// <summary>Maps the built-in ids to their algorithms.</summary>
    /// <param name="id">The on-disk id.</param>
    /// <returns>The built-in algorithm, or <see langword="null"/>.</returns>
    private static ChecksumAlgorithm? ResolveBuiltIn(byte id)
    {
        return id switch
        {
            1 => XxHash3,
            2 => Crc32,
            _ => null,
        };
    }

    /// <summary>Computes xxHash3-64 into the 8-byte destination.</summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="destination">The 8-byte destination.</param>
    private static void ComputeXxHash3(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        System.IO.Hashing.XxHash3.Hash(data, destination);
    }

    /// <summary>Computes CRC-32 into the 4-byte destination.</summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="destination">The 4-byte destination.</param>
    private static void ComputeCrc32(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        System.IO.Hashing.Crc32.Hash(data, destination);
    }

    /// <summary>Opens an incremental xxHash3-64 session; the concrete return converts covariantly to the session-factory delegate.</summary>
    /// <returns>The session.</returns>
    private static IncrementalHashChecksumSession CreateXxHash3Session()
    {
        return new IncrementalHashChecksumSession(new System.IO.Hashing.XxHash3());
    }

    /// <summary>Opens an incremental CRC-32 session; the concrete return converts covariantly to the session-factory delegate.</summary>
    /// <returns>The session.</returns>
    private static IncrementalHashChecksumSession CreateCrc32Session()
    {
        return new IncrementalHashChecksumSession(new System.IO.Hashing.Crc32());
    }

    /// <summary>A checksum session over an incremental non-cryptographic hash: windows append into the running state and the finish writes the accumulated digest.</summary>
    private sealed class IncrementalHashChecksumSession : ChecksumSession
    {
        /// <summary>The running hash state.</summary>
        private NonCryptographicHashAlgorithm Hash { get; }

        /// <summary>Wraps a fresh hash state.</summary>
        /// <param name="hash">The hash state; owned by the session.</param>
        public IncrementalHashChecksumSession(NonCryptographicHashAlgorithm hash)
        {
            Hash = hash;
        }

        /// <inheritdoc/>
        public override void Append(ReadOnlySpan<byte> data)
        {
            Hash.Append(data);
        }

        /// <inheritdoc/>
        public override void Finish(Span<byte> destination)
        {
            Hash.GetCurrentHash(destination);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
        }
    }
}
