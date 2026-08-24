using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One binding in a <see cref="SparqlSolution"/>: a query variable mapped to the RDF term it is bound to.
/// </summary>
/// <param name="Variable">The bound query variable.</param>
/// <param name="Value">The RDF term the variable is bound to.</param>
/// <remarks>
/// The SPARQL-level analogue of the hypertrie's <see cref="Core.Hypertrie.Planning.VariableBinding"/> (which is
/// over encoded ids): the executor decodes each encoded binding back to its <see cref="SparqlVariable"/> and
/// <see cref="RdfTerm"/> at the backend boundary. SPARQL 1.2 §18.1 [Solution Mapping].
/// </remarks>
[DebuggerDisplay("?{Variable.Name} = {Value}")]
public readonly record struct SparqlBinding(SparqlVariable Variable, RdfTerm Value);

/// <summary>
/// One result of evaluating a SPARQL query: an immutable solution mapping (a set of <see cref="SparqlBinding"/>s
/// from query variables to RDF terms). A variable absent from <see cref="Bindings"/> is unbound in this solution.
/// </summary>
/// <remarks>
/// <para>
/// The SPARQL-level analogue of the hypertrie's <see cref="Core.Hypertrie.Execution.Solution"/>: the executor
/// decodes each encoded <see cref="Core.Hypertrie.Planning.VariableBinding"/> to a <see cref="SparqlBinding"/>.
/// Variable count per query is small, so linear search through <see cref="Bindings"/> in
/// <see cref="TryGetValue"/> is the right shape — it allocates nothing and beats dictionary construction at
/// these sizes.
/// </para>
/// <para>SPARQL 1.2 §18.1 [Solution Mapping].</para>
/// </remarks>
[DebuggerDisplay("Solution Bindings={Bindings.Count}")]
public sealed class SparqlSolution
{
    /// <summary>The bindings making up this solution.</summary>
    public IReadOnlyList<SparqlBinding> Bindings { get; }

    /// <summary>Constructs a solution over the given bindings, held by reference.</summary>
    /// <param name="bindings">The variable-to-term bindings; callers must not mutate the list after construction.</param>
    public SparqlSolution(IReadOnlyList<SparqlBinding> bindings)
    {
        Bindings = bindings;
    }

    /// <summary>Returns whether <paramref name="variable"/> is bound in this solution, yielding its value when it is.</summary>
    /// <param name="variable">The variable to look up.</param>
    /// <param name="value">Receives the bound term on success; the default term otherwise.</param>
    /// <returns><see langword="true"/> when the variable is bound; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue(SparqlVariable variable, out RdfTerm value)
    {
        for(int i = 0; i < Bindings.Count; i++)
        {
            SparqlBinding binding = Bindings[i];
            if(binding.Variable == variable)
            {
                value = binding.Value;

                return true;
            }
        }

        value = null!;

        return false;
    }
}
