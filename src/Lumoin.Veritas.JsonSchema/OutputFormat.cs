namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// The JSON Schema 2020-12 validation output structures, in increasing detail. <see cref="Flag"/> and
/// <see cref="Basic"/> are flat; <see cref="Detailed"/> and <see cref="Verbose"/> nest output units along
/// the evaluation tree (Detailed drops valid, information-free nodes; Verbose keeps the full structure).
/// </summary>
public enum OutputFormat
{
    /// <summary>Just the boolean validity, with no error or annotation detail.</summary>
    Flag,

    /// <summary>The boolean validity plus a flat list of error units (on failure) or annotation units (on success).</summary>
    Basic,

    /// <summary>The hierarchical structure with valid, information-free nodes pruned away.</summary>
    Detailed,

    /// <summary>The full hierarchical structure mirroring the schema's evaluation tree.</summary>
    Verbose
}
