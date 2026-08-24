namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// A quad in canonical N-Quads serialization form.
/// </summary>
/// <remarks>
/// Produced by <see cref="RdfCanonicalizer"/> after blank node identifier
/// assignment. The <see cref="NQuadsLine"/> property is the exact byte sequence
/// that must appear in the canonical N-Quads output, including the line terminator.
/// </remarks>
/// <param name="NQuadsLine">
/// The canonical N-Quads serialization of this quad, encoded as UTF-8.
/// Includes the trailing <c> .\n</c> terminator. Blank node labels have
/// been replaced with their canonical identifiers.
/// </param>
public sealed record CanonicalQuad(string NQuadsLine);
