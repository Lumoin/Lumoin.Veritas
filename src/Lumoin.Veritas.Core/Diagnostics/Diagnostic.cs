using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// One diagnostic emitted by the lexer or a parser: a stable <see cref="Code"/>, a
/// <see cref="Severity"/>, the source <see cref="Span"/> it covers, a human-readable
/// <see cref="Message"/>, and an optional editor <see cref="Hint"/>.
/// </summary>
/// <remarks>
/// The <see cref="Code"/> is the machine-stable identifier (for example <c>SP0001</c>), drawn from
/// <see cref="WellKnownDiagnostics"/>; <see cref="Message"/> and <see cref="Hint"/> are displayable
/// text not intended for machine parsing. Localisation, if added later, would carry a separate
/// message-key field — this batch ships English-only and <see cref="Message"/> is the displayable form.
/// </remarks>
/// <param name="Code">The stable diagnostic code (a <see cref="WellKnownDiagnostics"/> constant).</param>
/// <param name="Severity">The severity.</param>
/// <param name="Span">The source extent the diagnostic covers.</param>
/// <param name="Message">A human-readable explanation, suitable for display.</param>
/// <param name="Hint">An optional secondary hint for editor surfaces, or <see langword="null"/>.</param>
[DebuggerDisplay("{Code,nq} {Severity} at {Span}")]
public readonly record struct Diagnostic(
    Utf8String Code,
    DiagnosticSeverity Severity,
    SourceSpan Span,
    Utf8String Message,
    Utf8String? Hint = null);
