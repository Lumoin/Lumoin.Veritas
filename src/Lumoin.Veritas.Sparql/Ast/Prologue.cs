using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The query prologue: the <c>BASE</c>, <c>PREFIX</c>, and <c>VERSION</c> declarations in source
/// order. Consumed by the parser to expand prefixed names and resolve relative
/// IRIs as it builds <see cref="IriRef"/> nodes.
/// </summary>
/// <param name="Span">The source extent of the prologue.</param>
/// <param name="Bases">The <c>BASE</c> declarations, in source order.</param>
/// <param name="Prefixes">The <c>PREFIX</c> declarations, in source order.</param>
/// <param name="Versions">The <c>VERSION</c> declarations, in source order.</param>
/// <remarks>SPARQL <c>Prologue</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPrologue">SPARQL 1.2 §19.8 [Prologue]</see>.</remarks>
[DebuggerDisplay("Prologue Bases={Bases.Count} Prefixes={Prefixes.Count} Versions={Versions.Count}")]
public sealed record Prologue(SourceSpan Span, IReadOnlyList<BaseDecl> Bases, IReadOnlyList<PrefixDecl> Prefixes, IReadOnlyList<VersionDecl> Versions);

/// <summary>A <c>BASE</c> declaration setting the base IRI for subsequent relative IRIs.</summary>
/// <param name="Span">The source extent of the declaration.</param>
/// <param name="Iri">The base IRI.</param>
/// <remarks>SPARQL <c>BaseDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBaseDecl">SPARQL 1.2 §19.8 [BaseDecl]</see>.</remarks>
[DebuggerDisplay("BASE <{Iri.Value}>")]
public sealed record BaseDecl(SourceSpan Span, IriRef Iri);

/// <summary>A <c>PREFIX</c> declaration binding a namespace prefix to an IRI.</summary>
/// <param name="Span">The source extent of the declaration.</param>
/// <param name="Prefix">The prefix label, including the trailing colon (for example <c>foaf:</c>).</param>
/// <param name="Namespace">The namespace IRI the prefix expands to.</param>
/// <remarks>SPARQL <c>PrefixDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPrefixDecl">SPARQL 1.2 §19.8 [PrefixDecl]</see>.</remarks>
[DebuggerDisplay("PREFIX {Prefix} <{Namespace.Value}>")]
public sealed record PrefixDecl(SourceSpan Span, Utf8String Prefix, IriRef Namespace);

/// <summary>
/// A <c>VERSION</c> declaration (RDF 1.2 / SPARQL 1.2) stating the SPARQL version the query targets.
/// Informational at parse time; carried for tooling and round-tripping.
/// </summary>
/// <param name="Span">The source extent of the declaration.</param>
/// <param name="Version">The version specifier — a short-quoted string label (for example <c>1.2</c>), without quotes.</param>
/// <remarks>SPARQL <c>VersionDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVersionDecl">SPARQL 1.2 §19.8 [VersionDecl]</see>.</remarks>
[DebuggerDisplay("VERSION {Version}")]
public sealed record VersionDecl(SourceSpan Span, Utf8String Version);
