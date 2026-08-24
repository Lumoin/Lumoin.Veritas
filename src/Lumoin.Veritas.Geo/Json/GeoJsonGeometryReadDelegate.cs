using System;

using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Json;

/// <summary>
/// Reads one complete UTF-8 GeoJSON Geometry document into a <see cref="FlatGeometry"/>, renting the
/// vertex-scale columns through the caller's allocator seam and answering refusal by value with the
/// first offense's byte offset. The Geo library holds no JSON tokenizer of its own: the binding
/// assembly that owns the System.Text.Json dependency implements this contract, and a composing host
/// supplies it when it registers the Geo function catalog.
/// </summary>
/// <param name="utf8Document">The complete UTF-8 document.</param>
/// <param name="allocators">The column allocator seam.</param>
/// <param name="geometry">The parsed geometry, or default on refusal.</param>
/// <param name="refusal">The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on success.</param>
/// <returns><see langword="true"/> when the document was accepted.</returns>
public delegate bool GeoJsonGeometryReadDelegate(ReadOnlySpan<byte> utf8Document, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
