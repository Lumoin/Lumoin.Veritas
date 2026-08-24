namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Phase-1 state for the test-side fluent pipeline: the shape graph
/// is being constructed via <see cref="ShapeGraphBuilder"/>; the
/// dictionary doesn't exist yet.
/// </summary>
/// <remarks>
/// Test infrastructure only. The library pipeline
/// (<see cref="Lumoin.Veritas.Shacl.Validation.Pipeline.ShaclPipeline"/>)
/// takes a pre-built <see cref="Lumoin.Veritas.Shacl.Loading.ShapeRegistry"/>
/// and never touches a builder. This state exists for tests that
/// want to express scenarios in code rather than parsing a shape
/// graph from a file.
/// </remarks>
internal sealed record TestShaclPipelineShapeState(
    ShapeGraphBuilder Builder,
    string? OptionalFocusIri);
