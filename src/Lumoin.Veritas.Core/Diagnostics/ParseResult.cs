using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// The outcome of a parse: the produced <typeparamref name="TTree"/> (always non-null, possibly
/// carrying error nodes), the <see cref="Diagnostics"/> gathered while parsing, and whether any of them
/// has error severity.
/// </summary>
/// <remarks>
/// <para>
/// Even a catastrophically broken input yields a tree — error nodes stand in at the nearest abstract
/// base — so an editor surface always has something to render. Production callers check
/// <see cref="HasErrors"/> and refuse to proceed; editor callers read <see cref="Diagnostics"/> and
/// keep the tree. This is the single generic result shape shared by every parser; it depends only on
/// <see cref="Diagnostic"/>, so it lives here rather than being duplicated per parser namespace.
/// </para>
/// </remarks>
/// <typeparam name="TTree">The parsed tree type (for example a document or request root).</typeparam>
/// <param name="Tree">The produced tree; never <see langword="null"/>.</param>
/// <param name="Diagnostics">The diagnostics gathered while parsing, in source order.</param>
/// <param name="HasErrors">Whether any entry in <see cref="Diagnostics"/> has <see cref="DiagnosticSeverity.Error"/> severity.</param>
[DebuggerDisplay("HasErrors={HasErrors} Diagnostics={Diagnostics.Count}")]
public sealed record ParseResult<TTree>(
    TTree Tree,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool HasErrors);
