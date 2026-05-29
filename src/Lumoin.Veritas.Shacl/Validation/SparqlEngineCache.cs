using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Per-validation-run cache for the <see cref="SparqlQueryEngine"/> backing <c>sh:sparql</c> constraint
/// evaluation. The data graph is materialized and indexed into a hypertrie-backed engine at most once per
/// <see cref="ShaclValidator.ValidateAsync"/> call, then re-used across every focus node and every SPARQL
/// constraint, so the O(data-graph) build cost is paid once rather than per evaluation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Independent dictionary.</b> The engine is built from the data graph's decoded <see cref="RdfTerm"/>s into
/// its own term dictionary (via <see cref="SparqlQueryEngine.BuildAsync"/>), distinct from the SHACL run's
/// dictionary. The two are bridged by <see cref="RdfTerm"/> value identity at the pre-binding and
/// result-mapping boundaries — a focus node decodes to an <see cref="RdfTerm"/> for pre-binding, and each
/// result term re-encodes into the SHACL dictionary for the produced <see cref="ValidationResult"/>.
/// </para>
/// <para>
/// <b>Single-threaded.</b> Mirrors <see cref="ClassMembershipCache"/>: the validation fold awaits each
/// constraint sequentially, so the lazy build needs no lock. The engine is held by reference once built.
/// </para>
/// </remarks>
public sealed class SparqlEngineCache
{
    private SparqlQueryEngine? engine;

    /// <summary>
    /// Returns the engine for the data graph, building it on first request by materializing every data-graph
    /// triple and indexing it. Subsequent calls return the cached engine.
    /// </summary>
    /// <param name="dataMatchOps">Match-op bundle over the data graph being validated.</param>
    /// <param name="dictionary">The SHACL run's term dictionary, used to decode the encoded data triples.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The query engine over the data graph.</returns>
    public async ValueTask<SparqlQueryEngine> GetOrBuildAsync(
        GraphMatchOps dataMatchOps,
        TermDictionary dictionary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        if(engine is not null)
        {
            return engine;
        }

        List<DataTriple> triples = [];
        await foreach(EncodedTriple triple in dataMatchOps.MatchTriples(TermId.None, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            triples.Add(new DataTriple(
                dictionary.Resolve(triple.Subject),
                dictionary.Resolve(triple.Predicate),
                dictionary.Resolve(triple.Object)));
        }

        engine = await SparqlQueryEngine.BuildAsync(triples, cancellationToken: cancellationToken).ConfigureAwait(false);

        return engine;
    }
}
