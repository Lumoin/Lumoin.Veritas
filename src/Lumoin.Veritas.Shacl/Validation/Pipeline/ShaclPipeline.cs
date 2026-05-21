using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Loading;
using System;

namespace Lumoin.Veritas.Shacl.Validation.Pipeline;

/// <summary>
/// Entry point for fluent execution of SHACL validation runs over a
/// pre-built shape registry and data graph.
/// </summary>
/// <remarks>
/// <para>
/// The library pipeline assumes shape construction happened
/// elsewhere — typically through a parser
/// (<see cref="Lumoin.Veritas.Rdf.NQuads"/>,
/// <see cref="Lumoin.Veritas.Shacl.Loading.ShapeLoader.LoadAsync"/>
/// against a JSON-LD or Turtle-parsed shape graph) or through a
/// programmatic builder living outside the library. From there, the
/// pipeline registers evaluators and runs validation.
/// </para>
/// <para>
/// All operational verbs are extension methods on
/// <see cref="ShaclPipelineDataState"/> so consumers can declare
/// scenario-specific helpers alongside their own code without
/// modifying this library. Test fixtures provide additional
/// extensions that wrap an in-memory shape-graph builder for the
/// fluent-construction case.
/// </para>
/// </remarks>
public static class ShaclPipeline
{
    /// <summary>
    /// Begins a pipeline against a pre-built
    /// <see cref="ShapeRegistry"/> and a <see cref="GraphMatchOps"/>
    /// bundle over the data graph. The returned state has an empty
    /// evaluator registration and is ready for <c>WithEvaluator</c>
    /// calls and a terminal <c>RunAsync</c>.
    /// </summary>
    /// <param name="shapes">The loaded shape registry.</param>
    /// <param name="dictionary">
    /// The term dictionary shared by both the shape graph and the
    /// data graph.
    /// </param>
    /// <param name="dataMatchOps">The data-graph match-op bundle.</param>
    public static ShaclPipelineDataState Begin(
        ShapeRegistry shapes,
        TermDictionary dictionary,
        GraphMatchOps dataMatchOps)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclPipelineDataState(shapes, dictionary, dataMatchOps, Evaluators: []);
    }
}
