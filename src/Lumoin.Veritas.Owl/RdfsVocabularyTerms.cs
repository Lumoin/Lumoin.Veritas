using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// The encoded identifiers of the RDF and RDFS vocabulary terms the
/// materializer recognises. The caller resolves these through its
/// term dictionary once and passes the bundle in; the reasoner
/// itself never sees IRIs, only <see cref="TermId"/> values, so it
/// works over any encoding the surrounding engine uses.
/// </summary>
/// <param name="Type">The encoded <c>rdf:type</c> term.</param>
/// <param name="SubClassOf">The encoded <c>rdfs:subClassOf</c> term.</param>
/// <param name="SubPropertyOf">The encoded <c>rdfs:subPropertyOf</c> term.</param>
/// <param name="Domain">The encoded <c>rdfs:domain</c> term.</param>
/// <param name="Range">The encoded <c>rdfs:range</c> term.</param>
/// <param name="Property">The encoded <c>rdf:Property</c> class term; enables rdf1 predicate typing and, with it, rdfs6 subproperty reflexivity.</param>
/// <param name="Class">The encoded <c>rdfs:Class</c> class term; enables axiomatic class typing and, with it, rdfs8 and rdfs10.</param>
/// <param name="Resource">The encoded <c>rdfs:Resource</c> class term; enables rdfs8 (every class is a subclass of <c>rdfs:Resource</c>).</param>
/// <param name="ContainerMembershipProperty">The encoded <c>rdfs:ContainerMembershipProperty</c> class term; with <paramref name="Member"/>, enables rdfs12.</param>
/// <param name="Member">The encoded <c>rdfs:member</c> property term, the superproperty rdfs12 concludes.</param>
/// <param name="Datatype">The encoded <c>rdfs:Datatype</c> class term; with <paramref name="Literal"/>, enables rdfs13.</param>
/// <param name="Literal">The encoded <c>rdfs:Literal</c> class term, the superclass rdfs13 concludes.</param>
/// <remarks>
/// <para>
/// A term left as <see cref="TermId.None"/> disables the rules
/// that consume it — a graph whose dictionary never minted
/// <c>rdfs:domain</c> simply derives no domain typings. The
/// trailing terms beyond the schema five default to
/// <see cref="TermId.None"/>, so a bundle carrying only the schema
/// terms runs the schema-driven rules and nothing else.
/// </para>
/// </remarks>
[DebuggerDisplay("RdfsVocabularyTerms Type={Type.Encoded}")]
public readonly record struct RdfsVocabularyTerms(
    TermId Type,
    TermId SubClassOf,
    TermId SubPropertyOf,
    TermId Domain,
    TermId Range,
    TermId Property = default,
    TermId Class = default,
    TermId Resource = default,
    TermId ContainerMembershipProperty = default,
    TermId Member = default,
    TermId Datatype = default,
    TermId Literal = default);
