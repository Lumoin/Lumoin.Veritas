using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A triple statement: <c>subject predicateObjectList .</c>.
/// </summary>
/// <remarks>
/// The <see cref="Subject"/> may be any subject-eligible term
/// — an IRI, prefixed name, blank node, blank-node property list,
/// collection, or reified triple. When the source omits a leading
/// subject and starts with a blank-node property list or reified
/// triple, the parser uses the property list or reifier itself as
/// the subject; the predicate-object list is still preserved as a
/// separate sequence so downstream consumers see the textual shape.
/// </remarks>
[DebuggerDisplay("TripleStatement s={Subject.NodeId} ({Predicates.Length} predicates) #{NodeId}")]
public sealed class TripleStatement: Statement
{
    /// <summary>
    /// Initialises a new <see cref="TripleStatement"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the entire statement up to and including its terminator.</param>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicates">The predicate-object list. May be empty when the subject is a self-asserting form (e.g. a bare blank-node property list).</param>
    public TripleStatement(
        int nodeId,
        SourceSpan span,
        Term subject,
        ImmutableArray<PredicateObject> predicates)
        : base(nodeId, span)
    {
        Subject = subject;
        Predicates = predicates;
    }

    /// <summary>Gets the subject term.</summary>
    public Term Subject { get; }

    /// <summary>Gets the predicate-object list.</summary>
    public ImmutableArray<PredicateObject> Predicates { get; }
}
