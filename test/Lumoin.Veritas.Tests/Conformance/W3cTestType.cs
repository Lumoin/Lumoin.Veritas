namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// The kind of a W3C conformance test, classified by how
/// <see cref="W3cTestRunner"/> dispatches the assertion.
/// </summary>
/// <remarks>
/// <para>
/// W3C manifests use IRI-shaped type markers like
/// <c>rdft:TestTurtlePositiveSyntax</c> and
/// <c>rdft:TestNTriplesNegativeSyntax</c>. The harness collapses
/// every per-syntax marker that means "parse and expect success"
/// to <see cref="PositiveSyntax"/>, every marker that means
/// "parse and expect failure" to <see cref="NegativeSyntax"/>,
/// and so on for the evaluation kinds. The original IRI is
/// preserved on <see cref="W3cTestCase.RawTypeIri"/> for
/// diagnostics.
/// </para>
/// </remarks>
internal enum W3cTestType
{
    /// <summary>
    /// The reader is invoked on the input file; success is
    /// "no exception, stream drained to completion."
    /// </summary>
    PositiveSyntax,

    /// <summary>
    /// The reader is invoked on the input file; success is
    /// "a parse-shaped exception is thrown."
    /// </summary>
    NegativeSyntax,

    /// <summary>
    /// The reader is invoked on input and on expected files;
    /// success is "the two quad sets are equal under blank-node
    /// isomorphism."
    /// </summary>
    Evaluation,

    /// <summary>
    /// As <see cref="Evaluation"/> but success is "the two sets
    /// are NOT equal."
    /// </summary>
    NegativeEvaluation,

    /// <summary>
    /// The reader parses the input; the canonicaliser produces
    /// canonical N-Triples / N-Quads bytes; success is "those
    /// bytes equal the expected fixture's bytes."
    /// </summary>
    PositiveC14N,

    /// <summary>
    /// The manifest used a test type the harness does not know
    /// how to dispatch. The runner reports
    /// <see cref="W3cOutcomeStatus.Skipped"/> with the original
    /// IRI in the message.
    /// </summary>
    Unknown
}
