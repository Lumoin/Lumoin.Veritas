using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An RDF 1.2 <c>@version</c> or <c>VERSION</c> directive announcing
/// the RDF spec version the document targets.
/// </summary>
/// <remarks>
/// Documents with no version directive are treated as RDF 1.2 by
/// convention; the directive is informational rather than
/// version-gating in this implementation. Multiple directives are
/// permitted by the grammar; consumers can inspect the captured
/// values directly.
/// </remarks>
[DebuggerDisplay("@version \"{VersionText,nq}\" #{NodeId}")]
public sealed class VersionDeclaration: Statement
{
    /// <summary>
    /// Initialises a new <see cref="VersionDeclaration"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the directive.</param>
    /// <param name="version">The version string as written in the source.</param>
    public VersionDeclaration(int nodeId, SourceSpan span, Utf8String version)
        : base(nodeId, span)
    {
        Version = version;
    }

    /// <summary>Gets the version string from the directive.</summary>
    public Utf8String Version { get; }

    private string VersionText => Version.ToString();
}
