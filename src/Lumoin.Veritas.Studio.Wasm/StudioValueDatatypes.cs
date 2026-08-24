using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Studio.Wasm;

/// <summary>
/// The value-datatype registry the editor's completion corpus enumerates its full-IRI candidates from. It
/// serves that corpus alone: the in-browser engine opens over its own options, which register no value
/// datatypes, so the query surface offers no geometry datatypes and this registry never reaches an
/// evaluation. Built once and reused, like the engine options beside it.
/// </summary>
internal static class StudioValueDatatypes
{
    /// <summary>The geometry-serialization value datatypes the corpus enumerates; the engine's options never see them.</summary>
    public static ValueDatatypeRegistry Registry { get; } = BuildRegistry();

    /// <summary>Registers the geometry-serialization value-datatype definitions into a fresh builder and freezes it.</summary>
    /// <returns>The frozen registry.</returns>
    private static ValueDatatypeRegistry BuildRegistry()
    {
        ValueDatatypeRegistryBuilder builder = new();
        GeoExtensionModule.RegisterValueDatatypes(builder);

        return builder.Build();
    }
}
