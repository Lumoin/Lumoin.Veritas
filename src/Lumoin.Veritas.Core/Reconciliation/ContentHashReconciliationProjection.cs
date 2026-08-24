using System;
using System.Buffers;
using System.Buffers.Binary;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The cross-fleet CONTENT-HASH reconciliation item domain: it projects a triple to a 128-bit hash of its terms'
/// canonical content, so two replicas produce the same item for the same triple even when their dictionaries
/// assigned different identifiers to the same terms — the cross-node identity the
/// <see cref="StructuralReconciliationProjection"/> cannot give (its packed identifiers are dictionary-epoch-local).
/// It is the boundary/interchange domain: a deployment keeps the compact structural domain inside a shared-epoch
/// cluster and uses this one to reconcile across independently-built dictionaries, the two related through the
/// <see cref="ProjectReconciliationItemDelegate"/> seam and the <see cref="TermDictionary"/> that translates ids to
/// terms.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not invertible.</b> A content hash discards the triple, so unlike the structural domain there is no inverse:
/// a recovered item names "a triple the peer has" without revealing which, and the triple is recovered another way
/// (a per-node hash-to-triple side-map plus a fetch-by-hash step), supplied by the content-hash reconcile path,
/// not by a <see cref="ReconciliationItemInverseDelegate"/>.
/// </para>
/// <para>
/// <b>Canonical, unambiguous encoding.</b> Each term is encoded as a kind tag followed by length-prefixed content
/// fields (an IRI's bytes; a literal's lexical value, datatype IRI, a present/absent language, and base direction),
/// so no two distinct terms share an encoding and the three terms concatenate without a separator that data
/// could forge. The 128-bit key is two domain-separated <see cref="VeritasHash"/> passes over those bytes; a
/// cryptographic deployment swaps the hash (the seam permits a SHA-256 truncation) for adversarial collision
/// resistance.
/// </para>
/// <para>
/// <b>Ground triples today.</b> IRIs and literals have node-independent content, so their triples are cross-node
/// stable. Blank nodes and RDF 1.2 triple terms are NOT projected — they are rejected with a
/// <see cref="NotSupportedException"/> rather than hashed, because a blank node's label is node-local (an identifier,
/// not content) and would otherwise silently produce non-cross-node-stable keys; both are lifted when RDFC-1.0
/// canonical labels feed this projection (a deferred brick), and a shared-epoch cluster reconciles blank-node data
/// through the structural domain instead.
/// </para>
/// </remarks>
public sealed class ContentHashReconciliationProjection
{
    private const byte IriTag = (byte)'I';
    private const byte LiteralTag = (byte)'L';
    private const byte NoDirection = 0;
    private const byte LtrDirection = 1;
    private const byte RtlDirection = 2;
    private const int LengthPrefixWidth = sizeof(uint);

    /// <summary>The dictionary that resolves a triple's identifiers back to the terms whose content is hashed.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>The deterministic hash the key is computed with; all replicas must agree on it.</summary>
    private VeritasHash Hash { get; }

    /// <summary>The pool the per-call canonical-bytes buffer is rented from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a content-hash projection over a dictionary and a deterministic hash.</summary>
    /// <param name="dictionary">The dictionary the projected triples' identifiers resolve against.</param>
    /// <param name="hash">The deterministic hash; every replica that reconciles together must use the same one (for example <see cref="VeritasHashing.Default"/>).</param>
    /// <param name="pool">The pool the per-call canonical-bytes buffer is rented from.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ContentHashReconciliationProjection(TermDictionary dictionary, VeritasHash hash, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(pool);

        Dictionary = dictionary;
        Hash = hash;
        Pool = pool;
        Projection = Project;
    }

    /// <summary>The projection as an injectable delegate — plug it into the reconciliation seam in place of the structural default.</summary>
    public ProjectReconciliationItemDelegate Projection { get; }

    /// <summary>Projects a triple to the 128-bit content hash of its terms' canonical content.</summary>
    /// <param name="triple">The triple to project.</param>
    /// <returns>The content-hash item.</returns>
    /// <exception cref="NotSupportedException">A term is a blank node or an RDF 1.2 triple term, which this domain does not yet project (deferred to the RDFC-1.0 brick) — so an epoch-local key is never produced silently.</exception>
    public ContentKey128 Project(EncodedTriple triple)
    {
        return ProjectTerms(Dictionary.Resolve(triple.Subject.Encoded), Dictionary.Resolve(triple.Predicate.Encoded), Dictionary.Resolve(triple.Object.Encoded));
    }

    /// <summary>Projects a triple given as terms (rather than identifiers) to its content key — the same hash <see cref="Project"/> computes after resolving, but without the dictionary round-trip. Used to verify that a peer-fetched triple hashes to the key it was requested for, before encoding it locally.</summary>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicate">The predicate term.</param>
    /// <param name="object">The object term.</param>
    /// <returns>The content-hash item.</returns>
    /// <exception cref="NotSupportedException">A term is a blank node or an RDF 1.2 triple term, which this domain does not yet project.</exception>
    public ContentKey128 ProjectTerms(RdfTerm subject, RdfTerm predicate, RdfTerm @object)
    {
        int size = 1 + EncodedSize(subject) + EncodedSize(predicate) + EncodedSize(@object);
        using IMemoryOwner<byte> owner = Pool.Rent(size);
        Span<byte> buffer = owner.Memory.Span[..size];

        int position = 1;
        position += WriteTerm(subject, buffer[position..]);
        position += WriteTerm(predicate, buffer[position..]);
        position += WriteTerm(@object, buffer[position..]);

        //The two passes differ only in the leading domain byte, so xxHash64's avalanche gives two well-separated
        //64-bit halves of one 128-bit key from a single canonical encoding.
        buffer[0] = 0;
        ulong low = Hash(buffer);
        buffer[0] = 1;
        ulong high = Hash(buffer);

        return new ContentKey128(low, high);
    }

    /// <summary>The number of bytes <see cref="WriteTerm"/> will write for a term.</summary>
    /// <param name="term">The term.</param>
    /// <returns>The encoded byte count.</returns>
    /// <exception cref="NotSupportedException">The term is a blank node or an RDF 1.2 triple term.</exception>
    private static int EncodedSize(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => 1 + LengthPrefixWidth + named.Iri.Span.Length,
            Literal literal => LiteralEncodedSize(literal),
            _ => throw UnsupportedTerm(term),
        };
    }

    /// <summary>The number of bytes <see cref="WriteLiteral"/> will write for a literal: the tag, the length-prefixed value and datatype IRI, the language presence byte and (when present) its length-prefixed bytes, and the base-direction byte.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The encoded byte count.</returns>
    private static int LiteralEncodedSize(Literal literal)
    {
        int languageSize = literal.Language is { } language ? LengthPrefixWidth + language.Span.Length : 0;

        return 1 + LengthPrefixWidth + literal.Value.Span.Length + LengthPrefixWidth + literal.Datatype.Iri.Span.Length + 1 + languageSize + 1;
    }

    /// <summary>Writes a term's canonical encoding — a kind tag and its length-prefixed content fields.</summary>
    /// <param name="term">The term to encode.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="NotSupportedException">The term is a blank node or an RDF 1.2 triple term.</exception>
    private static int WriteTerm(RdfTerm term, Span<byte> destination)
    {
        return term switch
        {
            NamedNode named => WriteNamed(named, destination),
            Literal literal => WriteLiteral(literal, destination),
            _ => throw UnsupportedTerm(term),
        };
    }

    /// <summary>Writes a named node: the IRI tag and the length-prefixed IRI bytes.</summary>
    /// <param name="named">The named node.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteNamed(NamedNode named, Span<byte> destination)
    {
        destination[0] = IriTag;

        return 1 + WriteField(destination[1..], named.Iri.Span);
    }

    /// <summary>Writes a literal: the literal tag, the length-prefixed lexical value and datatype IRI, a language presence byte and (when present) the length-prefixed language bytes, then the base-direction byte. The presence byte distinguishes an absent language from a present-but-empty one.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteLiteral(Literal literal, Span<byte> destination)
    {
        destination[0] = LiteralTag;
        int position = 1;
        position += WriteField(destination[position..], literal.Value.Span);
        position += WriteField(destination[position..], literal.Datatype.Iri.Span);
        if(literal.Language is { } language)
        {
            destination[position] = 1;
            position += 1;
            position += WriteField(destination[position..], language.Span);
        }
        else
        {
            destination[position] = 0;
            position += 1;
        }

        destination[position] = DirectionByte(literal.BaseDirection);

        return position + 1;
    }

    /// <summary>Writes a length-prefixed field: a little-endian 32-bit length followed by the content bytes.</summary>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <param name="content">The content bytes.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteField(Span<byte> destination, ReadOnlySpan<byte> content)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)content.Length);
        content.CopyTo(destination[LengthPrefixWidth..]);

        return LengthPrefixWidth + content.Length;
    }

    /// <summary>The canonical byte for a literal's optional base direction.</summary>
    /// <param name="direction">The base direction, or <see langword="null"/>.</param>
    /// <returns>0 for none, 1 for left-to-right, 2 for right-to-left.</returns>
    private static byte DirectionByte(TextDirection? direction)
    {
        return direction switch
        {
            null => NoDirection,
            TextDirection.Ltr => LtrDirection,
            _ => RtlDirection,
        };
    }

    /// <summary>Builds the exception for a term kind this domain does not yet project.</summary>
    /// <param name="term">The unsupported term.</param>
    /// <returns>The exception to throw.</returns>
    private static NotSupportedException UnsupportedTerm(RdfTerm term)
    {
        return new NotSupportedException($"The content-hash reconciliation domain does not yet project the term kind '{term.GetType().Name}'; blank nodes and RDF 1.2 triple terms are deferred until RDFC-1.0 canonical labels feed the projection. A blank node's label is node-local, so hashing it would silently produce non-cross-node-stable keys; reconcile blank-node data through the structural domain within a shared dictionary epoch instead.");
    }
}
