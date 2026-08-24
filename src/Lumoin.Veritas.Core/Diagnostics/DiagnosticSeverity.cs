namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// The severity of a <see cref="Diagnostic"/>. Two levels for now; <c>Info</c> and
/// <c>Hidden</c> can be added when a consumer needs them.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Advisory; the request still executes.</summary>
    Warning = 0,

    /// <summary>The request is refused — it will not execute.</summary>
    Error = 1
}
