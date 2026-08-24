using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// A pair of (test-pipeline-shape-state, in-progress-shape-context)
/// used while declaring constraints on a shape inside a fluent test
/// pipeline chain.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the role
/// <see cref="Lumoin.Veritas.Shacl.Validation.Pipeline.ShapeContextHandle"/>
/// would play if shape-graph construction were a library concern,
/// but lives here in test infrastructure since
/// <see cref="ShapeGraphBuilder"/> is test-only.
/// </para>
/// </remarks>
internal sealed record TestShapeContextHandle(
    TestShaclPipelineShapeState State,
    ShapeGraphBuilder.ShapeContext Context)
{
    /// <summary>
    /// Attaches a <c>(predicate, object)</c> pair to the in-progress
    /// shape's constraint declarations.
    /// </summary>
    public TestShapeContextHandle With(string predicateIri, RdfTerm @object)
    {
        ShapeGraphBuilder.ShapeContext next = Context.With(predicateIri, @object);

        return this with { Context = next };
    }

    /// <summary>
    /// Exits the shape-context chain and returns to the surrounding
    /// pipeline chain.
    /// </summary>
    public TestShaclPipelineShapeState Done() => State;
}
