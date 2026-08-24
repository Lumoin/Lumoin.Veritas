using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// One geometry literal's diagnosis: its severity and, where the severity names a reason, the typed
/// refusal carrying that reason and its first offending byte. The refusal rides the serialization codec
/// family's own currency, so a consumer switching over
/// <see cref="GeometryCodecRefusalKind"/> stays exhaustive against the one closed roster.
/// </summary>
/// <param name="Status">The severity, which decides whether <paramref name="Refusal"/> carries a reason.</param>
/// <param name="Refusal">
/// The reason and the first offending byte for a warning or an invalid verdict, and
/// <see cref="GeometryCodecRefusal.None"/> for every other status. The byte offset is relative to the
/// WHOLE literal body the diagnosis answered for, and is minus one when no byte of the body is nameable.
/// </param>
public readonly record struct GeoLiteralDiagnosis(
    GeoLiteralDiagnosisStatus Status,
    GeometryCodecRefusal Refusal);
