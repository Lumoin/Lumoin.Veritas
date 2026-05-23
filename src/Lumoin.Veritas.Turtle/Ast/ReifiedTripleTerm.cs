using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An RDF 1.2 reified-triple expression <c>&lt;&lt; s p o ~reifier? &gt;&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="TripleTermTerm"/> (<c>&lt;&lt;( ... )&gt;&gt;</c>):
/// a reified triple both asserts the inner triple <c>s p o</c> AND
/// asserts <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c>. The
/// reifier is either the explicit identifier from the source or a
/// fresh blank node when <see cref="Reifier"/> is <c>null</c>. The
/// expression yields the reifier as its surface value, suitable for
/// use as a subject in the enclosing predicate-object list.
/// </para>
/// </remarks>
[DebuggerDisplay("<< s={Subject.NodeId} p={Predicate.NodeId} o={Object.NodeId} reifier={Reifier} >> #{NodeId}")]
public sealed class ReifiedTripleTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="ReifiedTripleTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the reified triple including its delimiters.</param>
    /// <param name="subject">The subject of the asserted triple.</param>
    /// <param name="predicate">The predicate of the asserted triple.</param>
    /// <param name="objectTerm">The object of the asserted triple.</param>
    /// <param name="reifier">The explicit reifier identifier, or <c>null</c> for a fresh blank-node reifier.</param>
    public ReifiedTripleTerm(
        int nodeId,
        SourceSpan span,
        Term subject,
        Term predicate,
        Term objectTerm,
        Term? reifier)
        : base(nodeId, span)
    {
        Subject = subject;
        Predicate = predicate;
        Object = objectTerm;
        Reifier = reifier;
    }

    /// <summary>Gets the subject of the asserted triple.</summary>
    public Term Subject { get; }

    /// <summary>Gets the predicate of the asserted triple.</summary>
    public Term Predicate { get; }

    /// <summary>Gets the object of the asserted triple.</summary>
    public Term Object { get; }

    /// <summary>Gets the explicit reifier identifier, or <c>null</c> for a fresh blank-node reifier.</summary>
    public Term? Reifier { get; }
}
