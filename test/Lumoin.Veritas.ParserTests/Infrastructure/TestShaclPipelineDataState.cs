using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using System.Collections.Generic;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// The second state of the test-side fluent pipeline: shape graph has
/// been finalised and loaded; dictionary exists; data triples and
/// evaluator registrations accumulate; ready to materialise into a
/// library <see cref="Lumoin.Veritas.Shacl.Validation.Pipeline.ShaclPipelineDataState"/>
/// and run.
/// </summary>
/// <remarks>
/// <para>
/// The state holds the loaded library context
/// (<see cref="Shapes"/>, <see cref="Dictionary"/>, evaluator dict
/// inside <see cref="LibraryDataState"/>) plus test-only accumulators:
/// <see cref="DataTriples"/> for in-memory data-graph construction,
/// and <see cref="OptionalFocus"/> as the default subject for
/// convenience helpers.
/// </para>
/// <para>
/// The terminal call wraps <see cref="DataTriples"/> in an
/// <see cref="InMemoryGraphStore"/>, takes its match delegate, and
/// hands the package to the library pipeline's runner.
/// </para>
/// </remarks>
internal sealed record TestShaclPipelineDataState(
    Lumoin.Veritas.Shacl.Loading.ShapeRegistry Shapes,
    TermDictionary Dictionary,
    NamedNode? OptionalFocus,
    List<EncodedTriple> DataTriples,
    Dictionary<Utf8String, Lumoin.Veritas.Shacl.Validation.Evaluators.ConstraintEvaluator> Evaluators);
