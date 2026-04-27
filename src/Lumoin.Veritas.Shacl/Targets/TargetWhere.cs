using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Targets;

/// <summary>
/// <c>sh:targetWhere</c> (SHACL 1.2) — targets defined by a node expression
/// producing focus nodes.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §5.1.6. The target value is a SHACL Node Expression
/// (see the separate <em>SHACL 1.2 Node Expressions</em> working draft).
/// Its evaluation produces the focus-node set.
/// </para>
/// <para>
/// <b>This target is a placeholder in the current implementation.</b> The
/// Node Expressions specification is a separate working draft with its own
/// AST and evaluator; implementing it is a future session. The shape
/// loader accepts and preserves the expression payload; expansion throws
/// <see cref="NotImplementedException"/>.
/// </para>
/// </remarks>
/// <param name="ExpressionRoot">
/// The encoded identifier of the blank or IRI node that is the root of
/// the node-expression graph. The expression structure itself is not
/// materialized in this placeholder record.
/// </param>
public sealed record TargetWhere(TermId ExpressionRoot): Target
{
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always, until SHACL 1.2 Node Expressions are implemented.</exception>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "Deliberate placeholder for SHACL 1.2 Node Expressions. The method is documented to throw until the Node Expressions specification is implemented in a later session.")]
    public override IAsyncEnumerable<TermId> ExpandAsync(
        StorageDelegates.MatchTriplesAsync dataMatch,
        CancellationToken cancellationToken = default)
    {
        _ = dataMatch;
        _ = cancellationToken;
        throw new NotImplementedException(
            "sh:targetWhere requires SHACL 1.2 Node Expressions, which are not yet implemented. " +
            "See https://www.w3.org/TR/shacl12-node-expr/ for the specification.");
    }
}
