using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The one recognition rule for IRI-named extension-aggregate calls: a
/// <see cref="FunctionCallExpression"/> whose IRI is in the declared aggregate-function set is an
/// aggregate call, whatever its arity — the arity rule is enforced at evaluation, never by
/// recognition. The translator's promotion pass and the scope analyzer's aggregate-boundary walk both
/// consult this rule, so the two surfaces can never drift on what counts as an aggregate.
/// </summary>
internal static class SparqlAggregateRecognition
{
    /// <summary>Determines whether a node is a recognized extension-aggregate call under a declared-IRI set.</summary>
    /// <param name="node">The expression node.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    /// <param name="call">The recognized call, when the node is one.</param>
    /// <returns><see langword="true"/> when the node is a function call whose IRI is declared.</returns>
    public static bool IsRecognizedAggregateCall(ExpressionNode node, IReadOnlySet<Utf8String> aggregateFunctionIris, [NotNullWhen(true)] out FunctionCallExpression? call)
    {
        if(node is FunctionCallExpression candidate && aggregateFunctionIris.Count > 0 && aggregateFunctionIris.Contains(candidate.Function.Value))
        {
            call = candidate;

            return true;
        }

        call = null;

        return false;
    }
}
