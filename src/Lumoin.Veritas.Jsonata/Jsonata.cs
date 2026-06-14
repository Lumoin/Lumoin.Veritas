using System;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// The entry point to the JSONata query and transformation engine.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "The engine entry point is intentionally named for the language it implements, matching the package and namespace.")]
public static class Jsonata
{
    /// <summary>
    /// Evaluates a JSONata expression against a JSON input document and serializes the result to UTF-8 JSON.
    /// </summary>
    /// <param name="expression">The JSONata expression source, as UTF-8 bytes.</param>
    /// <param name="input">The input JSON document the expression is evaluated against, as UTF-8 bytes.</param>
    /// <returns>The evaluation result serialized as UTF-8 JSON.</returns>
    public static ReadOnlyMemory<byte> Evaluate(ReadOnlyMemory<byte> expression, ReadOnlyMemory<byte> input) =>
        throw new JsonataUnsupportedFeatureException("The Lumoin.Veritas.Jsonata engine is not yet implemented.");
}
