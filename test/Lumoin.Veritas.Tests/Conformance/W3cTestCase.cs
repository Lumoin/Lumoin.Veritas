using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// One W3C conformance test case as declared in a manifest. The
/// runner uses <see cref="Type"/> to choose its assertion and
/// <see cref="InputPath"/>/<see cref="ExpectedPath"/> to locate
/// the on-disk fixtures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Uri"/> is the full test IRI (typically a hash
/// fragment under the manifest's namespace), useful as a stable
/// identifier for reporting and for jumping back to the upstream
/// description.
/// </para>
/// <para>
/// <see cref="RawTypeIri"/> preserves the original
/// <c>rdf:type</c> IRI even when the harness classifies it as
/// <see cref="W3cTestType.Unknown"/>; failure reports cite the
/// raw IRI so the reader can decide whether to extend the
/// dispatcher or treat the test as out of scope.
/// </para>
/// </remarks>
[DebuggerDisplay("W3cTestCase {Type} {Name,nq}")]
internal sealed record W3cTestCase(
    Uri Uri,
    W3cTestType Type,
    string RawTypeIri,
    string Name,
    string Comment,
    string InputPath,
    string? ExpectedPath);
