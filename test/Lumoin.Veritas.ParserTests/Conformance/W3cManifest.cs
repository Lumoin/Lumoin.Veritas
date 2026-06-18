using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One W3C test manifest after parsing: an absolute identity
/// plus the ordered tests it declares.
/// </summary>
/// <remarks>
/// <para>
/// The manifest's own IRI comes from the absolute file URL of
/// the manifest source so callers can correlate emitted test
/// cases with the file they were loaded from. Nested
/// <c>mf:include</c> references are flattened during loading
/// so <see cref="Tests"/> covers the full transitive closure;
/// unresolved includes are recorded on
/// <see cref="UnresolvedIncludes"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("W3cManifest {Tests.Length} tests, {UnresolvedIncludes.Length} unresolved includes from {Uri.AbsoluteUri,nq}")]
internal sealed record W3cManifest(
    Uri Uri,
    ImmutableArray<W3cTestCase> Tests,
    ImmutableArray<string> UnresolvedIncludes);
