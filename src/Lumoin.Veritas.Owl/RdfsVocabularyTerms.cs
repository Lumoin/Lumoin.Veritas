using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// The encoded identifiers of the RDFS vocabulary terms the
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
/// <remarks>
/// A term left as <see cref="TermId.None"/> disables the rules
/// that consume it — a graph whose dictionary never minted
/// <c>rdfs:domain</c> simply derives no domain typings.
/// </remarks>
[DebuggerDisplay("RdfsVocabularyTerms Type={Type.Encoded}")]
public readonly record struct RdfsVocabularyTerms(
    TermId Type,
    TermId SubClassOf,
    TermId SubPropertyOf,
    TermId Domain,
    TermId Range);
