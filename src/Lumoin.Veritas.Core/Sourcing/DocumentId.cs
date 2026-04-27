using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// A 64-bit identifier for a parsed document or other byte sequence of
/// significance to the Veritas pipeline. Conventionally content-addressed:
/// the value is the application's chosen <c>VeritasHash</c> applied to the
/// document's canonical bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention, not enforcement.</b> The type itself is a transparent
/// 64-bit wrapper. It does not call a hash function and does not check that
/// its <see cref="Hash"/> value was derived from any particular bytes. The
/// content-addressing guarantee comes from the convention at construction
/// sites: parsers, emitters, and persistence reload code apply the
/// application's <c>VeritasHash</c> to canonical bytes and wrap the
/// resulting <see cref="ulong"/> in a <see cref="DocumentId"/>. Departures
/// from this convention break content addressing — two parties seeing the
/// same bytes will no longer agree on the identifier — and the type cannot
/// detect that.
/// </para>
/// <para>
/// <b>Why not a factory.</b> Pinning a hash function in a
/// <c>FromContent</c> factory would contradict the project's hash-function
/// composition: <c>VeritasHash</c> is the variability point that lets the
/// application name the algorithm in exactly one place at the composition
/// root, and have every content-addressing layer agree by construction.
/// A factory that called a hash function directly would either pin the
/// algorithm (wrong: contradicts <c>VeritasHash</c>'s purpose) or take the
/// delegate as a parameter (creates a parallel parameter-threading channel
/// for what is already explicit at parser sites). Construction at parser
/// sites reads <c>new DocumentId(veritasHash(canonicalBytes))</c>, with
/// the application's chosen <c>VeritasHash</c> already in scope as a
/// constructor or method argument — exactly the same pattern the hypertrie
/// uses for its node identifiers.
/// </para>
/// <para>
/// <b>Cross-platform protocol.</b> When the application's chosen
/// <c>VeritasHash</c> is xxHash64 with seed zero (the project default),
/// identifiers are stable across platforms and library builds. A different
/// algorithm at the composition root produces different identifiers but
/// the same content-addressing property: same bytes through the same
/// <c>VeritasHash</c> always produce the same <see cref="DocumentId"/>.
/// </para>
/// <para>
/// <b>Commitment role.</b> When derived from canonical bytes, a
/// <see cref="DocumentId"/> serves directly as a cryptographic commitment
/// input for proof systems that operate over RDF data — sister libraries
/// implementing zero-knowledge proofs, folding schemes, or
/// selective-disclosure credentials use the value as a public commitment
/// without further processing. A non-cryptographic <c>VeritasHash</c>
/// (such as xxHash64) has no security claim against adversarial preimage;
/// deployments needing cryptographic-grade fingerprinting compose a
/// stronger <c>VeritasHash</c> at the composition root, or store a parallel
/// strong-hash identifier alongside the <see cref="DocumentId"/>.
/// </para>
/// <para>
/// <b>Zero is a legitimate value.</b> A hash function may produce zero for
/// some inputs. The zero value carries no special meaning here and is not
/// used as a sentinel; consumers should not test against zero for "no
/// identifier."
/// </para>
/// </remarks>
/// <param name="Hash">
/// The 64-bit identifier value. Conventionally the result of applying the
/// application's <c>VeritasHash</c> to the document's canonical bytes.
/// </param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct DocumentId(ulong Hash)
{
    /// <summary>
    /// Gets the debugger label rendering this identifier in hexadecimal.
    /// Used by the type's <see cref="DebuggerDisplayAttribute"/>.
    /// </summary>
    private string DebuggerLabel => string.Create(CultureInfo.InvariantCulture, $"DocumentId 0x{Hash:X16}");
}
