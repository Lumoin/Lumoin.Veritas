using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The named cryptographic digest algorithms SPARQL's hash functions (§17.4) expose.
/// </summary>
public enum SparqlHashAlgorithm
{
    /// <summary>MD5 (<c>MD5</c>).</summary>
    Md5,

    /// <summary>SHA-1 (<c>SHA1</c>).</summary>
    Sha1,

    /// <summary>SHA-256 (<c>SHA256</c>).</summary>
    Sha256,

    /// <summary>SHA-384 (<c>SHA384</c>).</summary>
    Sha384,

    /// <summary>SHA-512 (<c>SHA512</c>).</summary>
    Sha512
}

/// <summary>
/// Computes a cryptographic digest of <paramref name="data"/> under <paramref name="algorithm"/>. The seam
/// through which SPARQL's hash functions obtain digests, so the crypto provider is swappable and inventoried for
/// the CBOM: the default is in-process, but a deployment may route to a TPM / HSM / remote provider.
/// </summary>
/// <param name="algorithm">The digest algorithm.</param>
/// <param name="data">The bytes to digest.</param>
/// <returns>The raw digest bytes.</returns>
public delegate byte[] SparqlHashDelegate(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data);

/// <summary>
/// The injected, swappable seams the expression evaluator's non-pure functions consume: randomness
/// (<c>RAND</c>, <c>UUID</c>, <c>STRUUID</c>), cryptographic digests (<c>MD5</c>/<c>SHA*</c>), and the
/// query-execution timestamp (<c>NOW</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Synchronous by design.</b> These run on the per-solution expression hot path (a <c>FILTER</c> over every
/// solution, an <c>ORDER BY</c> comparator, an aggregate over every group member), so the seams are synchronous
/// delegates — swappable at ~zero cost, without pushing <see langword="async"/> through the comparator-driven
/// evaluation path. A provider that genuinely needs async I/O (a remote/hardware crypto service) belongs at the
/// signing/provenance boundary, not on the value-expression hot path.
/// </para>
/// <para>
/// <b>NOW is captured once.</b> <see cref="Now"/> is the single query-execution instant SPARQL <c>NOW()</c>
/// returns; it is fixed for the lifetime of this context so every <c>NOW()</c> in a query agrees.
/// </para>
/// </remarks>
public sealed class SparqlExpressionContext
{
    /// <summary>Constructs a context over explicit seams.</summary>
    /// <param name="randomness">The randomness seam for <c>RAND</c>/<c>UUID</c>/<c>STRUUID</c>.</param>
    /// <param name="hash">The cryptographic-digest seam for <c>MD5</c>/<c>SHA*</c>.</param>
    /// <param name="now">The query-execution instant <c>NOW()</c> returns.</param>
    /// <exception cref="ArgumentNullException">A required seam is <see langword="null"/>.</exception>
    public SparqlExpressionContext(RandomnessDelegate randomness, SparqlHashDelegate hash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(randomness);
        ArgumentNullException.ThrowIfNull(hash);

        Randomness = randomness;
        Hash = hash;
        Now = now;
    }

    /// <summary>The randomness seam backing <c>RAND</c> (uniform double) and <c>UUID</c>/<c>STRUUID</c> (a fresh UUID).</summary>
    public RandomnessDelegate Randomness { get; }

    /// <summary>The cryptographic-digest seam backing <c>MD5</c>/<c>SHA1</c>/<c>SHA256</c>/<c>SHA384</c>/<c>SHA512</c>.</summary>
    public SparqlHashDelegate Hash { get; }

    /// <summary>The query-execution instant <c>NOW()</c> returns, fixed for this context.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>
    /// Creates a context over the default seams: <see cref="VeritasRandomness.System"/>, the in-process digest
    /// implementation, and the current instant from <paramref name="timeProvider"/> (or
    /// <see cref="TimeProvider.System"/>).
    /// </summary>
    /// <param name="timeProvider">The clock <c>NOW()</c> is captured from; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <returns>The default context.</returns>
    public static SparqlExpressionContext CreateDefault(TimeProvider? timeProvider = null)
    {
        return new SparqlExpressionContext(VeritasRandomness.System, DefaultHash, (timeProvider ?? TimeProvider.System).GetUtcNow());
    }

    /// <summary>The default in-process digest implementation, dispatching to <see cref="System.Security.Cryptography"/>.</summary>
    /// <param name="algorithm">The digest algorithm.</param>
    /// <param name="data">The bytes to digest.</param>
    /// <returns>The raw digest bytes.</returns>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is a SPARQL 1.2 built-in (§17.4) — a content digest function exposed to queries, not used to protect anything; the algorithm is dictated by the query, not chosen for security.")]
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA-1 is a SPARQL 1.2 built-in (§17.4) — a content digest function exposed to queries, not used to protect anything; the algorithm is dictated by the query, not chosen for security.")]
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "The library does not target the browser platform; the digest primitives are available on every targeted runtime.")]
    private static byte[] DefaultHash(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data)
    {
        return algorithm switch
        {
            SparqlHashAlgorithm.Md5 => MD5.HashData(data),
            SparqlHashAlgorithm.Sha1 => SHA1.HashData(data),
            SparqlHashAlgorithm.Sha256 => SHA256.HashData(data),
            SparqlHashAlgorithm.Sha384 => SHA384.HashData(data),
            SparqlHashAlgorithm.Sha512 => SHA512.HashData(data),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown SPARQL hash algorithm.")
        };
    }
}
