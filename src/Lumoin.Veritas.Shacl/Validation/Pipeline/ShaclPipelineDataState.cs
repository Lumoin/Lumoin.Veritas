using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation.Evaluators;

namespace Lumoin.Veritas.Shacl.Validation.Pipeline;

/// <summary>
/// State for a SHACL validation pipeline: a loaded shape registry,
/// a term dictionary, a data-graph access delegate, and the
/// accumulating constraint-evaluator dictionary.
/// </summary>
/// <remarks>
/// <para>
/// The library pipeline assumes the consumer has already produced a
/// <see cref="ShapeRegistry"/> by whatever means is appropriate to
/// their application — parsing N-Quads or Turtle, decoding
/// JSON-LD, or constructing the graph in code via a builder living
/// outside this library. The pipeline takes these pre-built inputs
/// and provides a fluent surface for the remaining concerns:
/// registering evaluators and running the validator.
/// </para>
/// <para>
/// <b>Mutable evaluator dictionary.</b> The
/// <see cref="Evaluators"/> dictionary grows as fluent
/// <c>WithEvaluator</c> calls register components. The terminal
/// <see cref="ShaclPipelineExtensions.RunAsync"/> wraps it in a
/// <see cref="ConstraintEvaluatorRegistry"/> for the validator.
/// </para>
/// <para>
/// <b>Data-graph access.</b>
/// <see cref="DataMatchOps"/> is the read-only data-graph access
/// bundle used by the validator for target expansion, value-node
/// computation, and constraint-specific traversal. The library does
/// not append to the data graph through this state; consumers that
/// want to build a data graph in code do so through their own
/// builder (test infrastructure provides one for the in-memory case)
/// and pass the resulting <see cref="GraphMatchOps"/> to
/// <see cref="ShaclPipeline.Begin"/>.
/// </para>
/// </remarks>
public sealed record ShaclPipelineDataState(
    ShapeRegistry Shapes,
    TermDictionary Dictionary,
    GraphMatchOps DataMatchOps,
    Dictionary<Utf8String, ConstraintEvaluator> Evaluators): IShaclPipelineState;
