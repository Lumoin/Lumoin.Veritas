using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The registering host's value-datatype registry for the Geo conformance arm: the six geometry
/// serialization definitions — <see cref="WktLiteralValueDatatype"/>,
/// <see cref="GmlLiteralValueDatatype"/>, <see cref="GeoJsonLiteralValueDatatype"/>,
/// <see cref="KmlLiteralValueDatatype"/>, <see cref="DggsLiteralValueDatatype"/> and the house
/// <see cref="A5DggsLiteralValueDatatype"/> DGGS subclass — registered, nothing else. The arm's
/// populated-registry rows all consult this one instance, so every behavioural delta the registration
/// introduces is exercised against one composition.
/// </summary>
internal static class GeoArmRegistry
{
    /// <summary>The registry with the geometry serialization datatypes registered — built once; the registry is immutable.</summary>
    public static ValueDatatypeRegistry SerializationRegistered { get; } = Build();

    /// <summary>Builds the registry, asserting every registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static ValueDatatypeRegistry Build()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(WktLiteralValueDatatype.Instance).Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(GmlLiteralValueDatatype.Instance).Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(GeoJsonLiteralValueDatatype.Instance).Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(KmlLiteralValueDatatype.Instance).Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(DggsLiteralValueDatatype.Instance).Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(A5DggsLiteralValueDatatype.Instance).Kind);

        return builder.Build();
    }
}
