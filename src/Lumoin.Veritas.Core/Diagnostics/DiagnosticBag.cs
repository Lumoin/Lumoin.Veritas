using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// A mutable, append-only collection of <see cref="Diagnostic"/> values accumulated during lexing and
/// parsing, exposed read-only afterward via <see cref="Diagnostics"/>.
/// </summary>
/// <remarks>
/// The bag does not deduplicate — emitting the same code at the same span twice produces two entries.
/// Cascade suppression (not double-emitting after one bad token) is the parser's responsibility: it
/// advances past the bad token, resyncing, before producing another diagnostic. Keeping the bag a
/// plain container leaves an editor surface free to display every diagnostic it receives.
/// </remarks>
[DebuggerDisplay("Count={Count} HasErrors={HasErrors}")]
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> diagnostics = [];

    /// <summary>Appends a diagnostic to the bag.</summary>
    /// <param name="diagnostic">The diagnostic to append.</param>
    public void Add(Diagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);

        if(diagnostic.Severity == DiagnosticSeverity.Error)
        {
            HasErrors = true;
        }
    }

    /// <summary>Gets whether any appended diagnostic has <see cref="DiagnosticSeverity.Error"/> severity.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>Gets the number of diagnostics in the bag.</summary>
    public int Count => diagnostics.Count;

    /// <summary>Gets the accumulated diagnostics, in emission order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;
}
