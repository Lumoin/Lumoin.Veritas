namespace Lumoin.Veritas.Shacl;

/// <summary>
/// The severity level of a validation result.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.6, severity is an IRI. This enum represents the
/// three standard severity levels defined by the specification. Non-standard
/// severities (IRIs outside the SHACL namespace) are not supported; shapes
/// using them are rejected at load time.
/// </para>
/// <para>
/// Default severity when unspecified by a shape is
/// <see cref="Violation"/>, per the specification.
/// </para>
/// </remarks>
public enum Severity
{
    /// <summary><c>sh:Info</c> — informational finding that does not represent a constraint failure.</summary>
    Info = 0,

    /// <summary><c>sh:Warning</c> — a soft failure that does not invalidate the data.</summary>
    Warning = 1,

    /// <summary><c>sh:Violation</c> — a hard constraint failure. The default.</summary>
    Violation = 2
}
