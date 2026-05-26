using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The query prologue: the <c>BASE</c> and <c>PREFIX</c> declarations in source
/// order. Consumed by the parser to expand prefixed names and resolve relative
/// IRIs as it builds <see cref="IriRef"/> nodes.
/// </summary>
/// <param name="Bases">The <c>BASE</c> declarations, in source order.</param>
/// <param name="Prefixes">The <c>PREFIX</c> declarations, in source order.</param>
[DebuggerDisplay("Prologue Bases={Bases.Count} Prefixes={Prefixes.Count}")]
public sealed record Prologue(IReadOnlyList<BaseDecl> Bases, IReadOnlyList<PrefixDecl> Prefixes);

/// <summary>A <c>BASE</c> declaration setting the base IRI for subsequent relative IRIs.</summary>
/// <param name="Iri">The base IRI.</param>
[DebuggerDisplay("BASE <{Iri.Value}>")]
public sealed record BaseDecl(IriRef Iri);

/// <summary>A <c>PREFIX</c> declaration binding a namespace prefix to an IRI.</summary>
/// <param name="Prefix">The prefix label, including the trailing colon (for example <c>foaf:</c>).</param>
/// <param name="Namespace">The namespace IRI the prefix expands to.</param>
[DebuggerDisplay("PREFIX {Prefix} <{Namespace.Value}>")]
public sealed record PrefixDecl(Utf8String Prefix, IriRef Namespace);
