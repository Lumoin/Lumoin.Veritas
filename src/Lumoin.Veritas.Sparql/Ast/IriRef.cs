using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// An IRI reference in the AST, resolved to its absolute form.
/// </summary>
/// <remarks>
/// Prefixed names and relative IRIs are resolved at parse time against the
/// current <see cref="Prologue"/> (its prefix map and base); the parser throws
/// <see cref="SparqlParseException"/> when a prefix is unbound or a relative IRI
/// has no base. <see cref="Span"/> records where the reference appeared in the
/// source, or <see cref="SourceSpan.None"/> for programmatically built nodes.
/// </remarks>
/// <param name="Value">The absolute IRI.</param>
/// <param name="Span">The source span of the reference.</param>
[DebuggerDisplay("<{Value}>")]
public readonly record struct IriRef(Utf8String Value, SourceSpan Span);
