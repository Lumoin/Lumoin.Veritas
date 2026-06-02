namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// The JSON Schema 2020-12 validation output structures.
/// </summary>
/// <remarks>
/// <see cref="Detailed"/> and <see cref="Verbose"/> (the hierarchical structures) are not produced yet;
/// <see cref="JsonSchemaValidator.Evaluate"/> supports <see cref="Flag"/> and <see cref="Basic"/>.
/// </remarks>
public enum OutputFormat
{
    /// <summary>Just the boolean validity, with no error or annotation detail.</summary>
    Flag,

    /// <summary>The boolean validity plus a flat list of error units (on failure) or annotation units (on success).</summary>
    Basic
}
