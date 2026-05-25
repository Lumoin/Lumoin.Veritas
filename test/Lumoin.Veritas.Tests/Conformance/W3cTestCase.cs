using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// One W3C conformance test case as declared in a manifest. The
/// runner uses <see cref="Type"/> to choose its assertion and the
/// fixture-path fields to locate the on-disk inputs.
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
/// <para>
/// The trailing fields carry SHACL-specific inputs and are unset for the
/// syntax, evaluation, and canonicalization suites:
/// <see cref="ShapesGraphPath"/> is the shapes graph (often the same file as
/// <see cref="InputPath"/>), and <see cref="ExpectedReportTerm"/> is the
/// manifest term key of the inline expected <c>sh:ValidationReport</c>
/// rather than a sibling result file. <see cref="OriginManifest"/> records
/// the declaring manifest so results can be grouped by sub-suite.
/// </para>
/// </remarks>
/// <param name="Uri">The full test IRI.</param>
/// <param name="Type">The classified test type the runner dispatches on.</param>
/// <param name="RawTypeIri">The original <c>rdf:type</c> IRI, preserved for diagnostics.</param>
/// <param name="Name">The test's <c>mf:name</c> or label, for display.</param>
/// <param name="Comment">The test's <c>rdfs:comment</c>, when present.</param>
/// <param name="InputPath">The action input file: the parsed document for syntax and evaluation tests, the data graph for SHACL tests.</param>
/// <param name="ExpectedPath">The expected-result file for evaluation and canonicalization tests; <c>null</c> when the test has no sibling result file.</param>
/// <param name="OriginManifest">The absolute path of the manifest that declared this test, used to group results by sub-suite.</param>
/// <param name="ShapesGraphPath">The SHACL shapes graph file; <c>null</c> for non-SHACL tests.</param>
/// <param name="ExpectedReportTerm">The manifest term key of the inline expected <c>sh:ValidationReport</c> for SHACL tests; <c>null</c> otherwise.</param>
[DebuggerDisplay("W3cTestCase {Type} {Name,nq}")]
internal sealed record W3cTestCase(
    Uri Uri,
    W3cTestType Type,
    string RawTypeIri,
    string Name,
    string Comment,
    string InputPath,
    string? ExpectedPath,
    string OriginManifest = "",
    string? ShapesGraphPath = null,
    string? ExpectedReportTerm = null);
