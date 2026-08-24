using System;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The one named Geo extension module: composes everything the Geo extension registers into a host's
/// registries — the <see cref="GeoFunctions"/> catalog into an extension-function registry, the
/// geometry-serialization value-datatype definitions into a value-datatype registry, and the
/// <see cref="GeoQueryRewrite"/> entry into a rewrite pipeline. Nothing wires the module anywhere by
/// itself; a composing host calls the register methods on its own builders, so an unregistered engine
/// keeps its dark-by-default posture byte-identically. The rewrite pipeline and the function registry
/// belong together: the rewritten patterns' derived branches call the catalog's predicate functions, so a
/// pipeline composed without the functions degrades to asserted-only matching.
/// </summary>
public static class GeoExtensionModule
{
    /// <summary>Creates the rewrite pipeline holding the Geo query-rewrite entry, for a host's <see cref="SparqlEnginePolicy.Rewrites"/> or a per-call pipeline argument.</summary>
    /// <returns>The pipeline.</returns>
    public static AlgebraRewritePipeline CreateRewritePipeline()
    {
        return AlgebraRewritePipeline.Create(GeoQueryRewrite.TopologicalRelations);
    }

    /// <summary>
    /// Registers every <see cref="GeoFunctions"/> catalog entry and installs the GeoJSON read binding
    /// the catalog's operand seam ingests <c>geo:geoJSONLiteral</c> bodies through; outcomes accumulate
    /// on the builder's audit list. The binding is a required argument because the Geo library holds no
    /// JSON tokenizer of its own — the assembly owning the System.Text.Json dependency implements the
    /// delegate, and requiring it here makes a missing binding a compile-time absence instead of a
    /// runtime hole.
    /// </summary>
    /// <param name="builder">The extension-function registry builder to register into.</param>
    /// <param name="geoJsonReader">The GeoJSON read binding the operand seam ingests through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="geoJsonReader"/> is <see langword="null"/>.</exception>
    public static void RegisterFunctions(SparqlFunctionRegistryBuilder builder, GeoJsonGeometryReadDelegate geoJsonReader)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(geoJsonReader);

        GeoFunctions.GeoJsonReader = geoJsonReader;
        foreach(SparqlFunctionEntry entry in GeoFunctions.All)
        {
            builder.Add(entry);
        }
    }

    /// <summary>Registers the geometry-serialization value-datatype definitions — <c>geo:wktLiteral</c>, <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>, <c>geo:kmlLiteral</c>, <c>geo:dggsLiteral</c>, and the house <c>a5Literal</c> DGGS subclass; the outcomes accumulate on the builder's audit list in that order. The registry models no datatype subsumption, so a check scoped to <c>geo:dggsLiteral</c> never matches an <c>a5Literal</c>-typed literal — indicating the flavour through the subclass is a decided tradeoff against generic-typed reach.</summary>
    /// <param name="builder">The value-datatype registry builder to register into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static void RegisterValueDatatypes(ValueDatatypeRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(WktLiteralValueDatatype.Instance);
        builder.Add(GmlLiteralValueDatatype.Instance);
        builder.Add(GeoJsonLiteralValueDatatype.Instance);
        builder.Add(KmlLiteralValueDatatype.Instance);
        builder.Add(DggsLiteralValueDatatype.Instance);
        builder.Add(A5DggsLiteralValueDatatype.Instance);
    }
}
