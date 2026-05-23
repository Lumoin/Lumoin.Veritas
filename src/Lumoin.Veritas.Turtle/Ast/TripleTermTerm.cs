using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An RDF 1.2 triple term <c>&lt;&lt;( s p o )&gt;&gt;</c> — a
/// term representing a triple, suitable for use as the object of
/// another triple (typically with the predicate <c>rdf:reifies</c>).
/// </summary>
/// <remarks>
/// Triple terms are not asserted as triples themselves; the parser
/// produces a single <see cref="Core.TripleTerm"/> in the emitter
/// rather than three independent triples.
/// </remarks>
[DebuggerDisplay("<<( s={Subject.NodeId} p={Predicate.NodeId} o={Object.NodeId} )>> #{NodeId}")]
public sealed class TripleTermTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="TripleTermTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the triple term including its delimiters.</param>
    /// <param name="subject">The subject of the denoted triple.</param>
    /// <param name="predicate">The predicate of the denoted triple.</param>
    /// <param name="objectTerm">The object of the denoted triple.</param>
    public TripleTermTerm(int nodeId, SourceSpan span, Term subject, Term predicate, Term objectTerm)
        : base(nodeId, span)
    {
        Subject = subject;
        Predicate = predicate;
        Object = objectTerm;
    }

    /// <summary>Gets the subject of the denoted triple.</summary>
    public Term Subject { get; }

    /// <summary>Gets the predicate of the denoted triple.</summary>
    public Term Predicate { get; }

    /// <summary>Gets the object of the denoted triple.</summary>
    public Term Object { get; }
}
