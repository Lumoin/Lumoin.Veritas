namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Test-only entry point for fluent SHACL validation scenarios that
/// construct a shape graph in code via <see cref="ShapeGraphBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two phases: shape-graph construction
/// (<see cref="TestShaclPipelineShapeState"/>) followed by
/// data-graph construction and execution
/// (<see cref="TestShaclPipelineDataState"/>), with the transition
/// going through the test pipeline's <c>BuildAsync</c>.
/// </para>
/// <para>
/// Production code uses
/// <see cref="Lumoin.Veritas.Shacl.Validation.Pipeline.ShaclPipeline.Begin"/>
/// directly, passing in a pre-built
/// <see cref="Lumoin.Veritas.Shacl.Loading.ShapeRegistry"/> from
/// whatever loader produced it.
/// </para>
/// </remarks>
internal static class TestShaclPipeline
{
    /// <summary>
    /// Begins a test pipeline. Data-stage helpers that take a
    /// predicate+object pair and default the subject are unavailable;
    /// emit triples with explicit subjects instead.
    /// </summary>
    public static TestShaclPipelineShapeState Begin()
        => new(new ShapeGraphBuilder(), OptionalFocusIri: null);

    /// <summary>
    /// Begins a test pipeline that defaults the data-stage subject
    /// to the given focus IRI. The data-stage helpers
    /// <c>WithTripleOnFocus</c> and <c>WithTriplesOnFocus</c> become
    /// available.
    /// </summary>
    public static TestShaclPipelineShapeState BeginWithFocus(string focusIri)
        => new(new ShapeGraphBuilder(), focusIri);
}
