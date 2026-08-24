using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf.Values;
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
    /// <summary>The execution-strategy policy the cached engine is built under.</summary>
    private SparqlEnginePolicy EnginePolicy { get; }

    /// <summary>The value-layer datatype registry the built engine's expression context carries, so a constraint query's <c>=</c>/<c>!=</c> comparisons answer the same as the embedding host's queries.</summary>
    private ValueDatatypeRegistry ValueDatatypes { get; }

    /// <summary>The extension-function registry the built engine's expression context carries, so a constraint query can invoke the same IRI-named functions as the embedding host's queries.</summary>
    private SparqlFunctionRegistry ExtensionFunctions { get; }

    private SparqlQueryEngine? engine;

    /// <summary>Constructs the cache; the engine it builds carries <paramref name="enginePolicy"/> and the two expression-context registries.</summary>
    /// <param name="enginePolicy">The execution-strategy policy the cached engine is built under; standalone SHACL callers pass <see cref="SparqlEnginePolicy.Default"/>, a host embedding the validator passes its engine-wide policy.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry the engine's expression context consults; standalone callers pass <see cref="ValueDatatypeRegistry.Empty"/>, a host passes its engine-wide registry.</param>
    /// <param name="extensionFunctions">The extension-function registry the engine's expression context consults; standalone callers pass <see cref="SparqlFunctionRegistry.Empty"/>, a host passes its engine-wide registry.</param>
    /// <exception cref="ArgumentNullException">A registry is <see langword="null"/>.</exception>
    public SparqlEngineCache(SparqlEnginePolicy enginePolicy, ValueDatatypeRegistry valueDatatypes, SparqlFunctionRegistry extensionFunctions)
    {
        ArgumentNullException.ThrowIfNull(valueDatatypes);
        ArgumentNullException.ThrowIfNull(extensionFunctions);

        EnginePolicy = enginePolicy;
        ValueDatatypes = valueDatatypes;
        ExtensionFunctions = extensionFunctions;
    }

    /// <summary>
    /// Returns the engine for the validation dataset, building it on first request by materializing every
    /// data-graph triple as the default graph and — when a shapes graph is supplied — every shapes-graph triple
    /// as a named graph under <paramref name="shapesGraphIri"/> (so a SPARQL constraint's <c>GRAPH $shapesGraph</c>
    /// can query it). Subsequent calls return the cached engine.
    /// </summary>
    /// <param name="dataMatchOps">Match-op bundle over the data graph being validated.</param>
    /// <param name="shapesGraphMatchOps">Match-op bundle over the shapes graph, or <see langword="null"/> to omit it from the dataset.</param>
    /// <param name="shapesGraphIri">The IRI naming the shapes named graph (pre-bound to <c>$shapesGraph</c>), or <see langword="null"/> when no shapes graph is supplied.</param>
    /// <param name="dictionary">The SHACL run's term dictionary, used to decode the encoded triples.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The query engine over the dataset.</returns>
    public async ValueTask<SparqlQueryEngine> GetOrBuildAsync(
        GraphMatchOps dataMatchOps,
        GraphMatchOps? shapesGraphMatchOps,
        RdfTerm? shapesGraphIri,
        TermDictionary dictionary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        if(engine is not null)
        {
            return engine;
        }

        List<DataTriple> defaultGraph = await MaterializeAsync(dataMatchOps, dictionary, cancellationToken).ConfigureAwait(false);

        //The engine's expression context carries the host's registries so a constraint query's value-layer
        //comparisons and extension-function invocations answer exactly as they would in the host's own queries.
        SparqlExpressionContext expressionContext = SparqlExpressionContext.CreateDefault(valueDatatypes: ValueDatatypes, extensionFunctions: ExtensionFunctions);

        if(shapesGraphMatchOps is GraphMatchOps shapesMatchOps && shapesGraphIri is RdfTerm shapesIri)
        {
            List<DataTriple> shapesGraph = await MaterializeAsync(shapesMatchOps, dictionary, cancellationToken).ConfigureAwait(false);
            engine = await SparqlQueryEngine.BuildDatasetAsync(
                defaultGraph,
                [(shapesIri, shapesGraph)],
                expressionContext: expressionContext,
                enginePolicy: EnginePolicy,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return engine;
        }

        engine = await SparqlQueryEngine.BuildAsync(defaultGraph, expressionContext: expressionContext, enginePolicy: EnginePolicy, cancellationToken: cancellationToken).ConfigureAwait(false);

        return engine;
    }

    /// <summary>Materializes a graph's encoded triples into decoded <see cref="DataTriple"/>s for the engine build.</summary>
    /// <param name="matchOps">Match-op bundle over the graph.</param>
    /// <param name="dictionary">The SHACL run's term dictionary, used to decode the encoded triples.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The decoded triples.</returns>
    private static async ValueTask<List<DataTriple>> MaterializeAsync(GraphMatchOps matchOps, TermDictionary dictionary, CancellationToken cancellationToken)
    {
        List<DataTriple> triples = [];
        await foreach(EncodedTriple triple in matchOps.MatchTriples(TermId.None, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            triples.Add(new DataTriple(
                dictionary.Resolve(triple.Subject),
                dictionary.Resolve(triple.Predicate),
                dictionary.Resolve(triple.Object)));
        }

        return triples;
    }
}
