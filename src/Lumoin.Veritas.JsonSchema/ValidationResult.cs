using System.Collections.Generic;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// The outcome of validating an instance against a schema: whether the instance is valid, and the
/// assertion failures gathered when it is not.
/// </summary>
/// <param name="IsValid">Whether the instance satisfied the schema.</param>
/// <param name="Errors">The assertion failures; empty when <paramref name="IsValid"/> is <see langword="true"/>.</param>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationError> Errors);
