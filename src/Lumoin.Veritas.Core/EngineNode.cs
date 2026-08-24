using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// An engine-minted node: a term a reasoning or mapping engine introduces for structure it derives — an
/// existential witness, a synthesized list cell, a scaffold copy — never a term any parser or converter can
/// produce. Identity is the content key alone: the mint <see cref="Family"/> plus four key components whose
/// meaning the family defines. Content-keyed identity makes re-derivation idempotent — the same semantic
/// occasion always mints the equal node — while the distinct term kind keeps every engine mint disjoint from
/// every parsed term by type, so no input document can pre-load facts onto a node the engine will mint.
/// </summary>
/// <remarks>
/// The constructor is internal: input surfaces build <see cref="NamedNode"/>, <see cref="BlankNode"/>,
/// <see cref="Literal"/>, and <see cref="TripleTerm"/> instances only, and a converter must never reconstruct an
/// engine node from serialized bytes — a text boundary renders the node through <see cref="SkolemIri"/> or
/// refuses loudly, and only the term-record persistence layer inside this assembly rehydrates the identity.
/// </remarks>
[DebuggerDisplay("{ToString()}")]
public sealed record EngineNode: RdfTerm
{
    /// <summary>The mint family that produced this node and defines its key components' meaning.</summary>
    public EngineNodeFamily Family { get; }

    /// <summary>The first key component; its meaning is the <see cref="Family"/>'s to define.</summary>
    public uint Key0 { get; }

    /// <summary>The second key component; zero when the family keys fewer components.</summary>
    public uint Key1 { get; }

    /// <summary>The third key component; zero when the family keys fewer components.</summary>
    public uint Key2 { get; }

    /// <summary>The fourth key component; zero when the family keys fewer components.</summary>
    public uint Key3 { get; }

    /// <summary>Creates an engine-minted node from its content key.</summary>
    /// <param name="family">The mint family.</param>
    /// <param name="key0">The first key component.</param>
    /// <param name="key1">The second key component; omit when the family keys fewer components.</param>
    /// <param name="key2">The third key component; omit when the family keys fewer components.</param>
    /// <param name="key3">The fourth key component; omit when the family keys fewer components.</param>
    internal EngineNode(EngineNodeFamily family, uint key0, uint key1 = 0, uint key2 = 0, uint key3 = 0)
    {
        Family = family;
        Key0 = key0;
        Key1 = key1;
        Key2 = key2;
        Key3 = key3;
    }

    /// <summary>
    /// The node's deterministic Skolem IRI — the one canonical rendering a text boundary uses when it serializes
    /// a graph containing engine mints. The rendering is one-way by construction: it re-parses as an ordinary
    /// <see cref="NamedNode"/>, never back into an engine node, so a round-trip through text can never restore a
    /// term equal to an engine mint.
    /// </summary>
    /// <returns>The Skolem IRI bytes.</returns>
    public Utf8String SkolemIri()
    {
        return Utf8Strings.From($"urn:veritas:genid:{Family.Code}:{Key0}:{Key1}:{Key2}:{Key3}");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"engine:{Family.Code}:{Key0}-{Key1}-{Key2}-{Key3}";
    }
}
