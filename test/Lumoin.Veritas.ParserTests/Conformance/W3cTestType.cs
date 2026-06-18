namespace Lumoin.Veritas.ParserTests.Conformance;

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
    /// A SHACL validation test: the data graph is validated against the
    /// shapes graph, and the produced <c>sh:ValidationReport</c> is compared
    /// to the manifest's inline expected report under blank-node isomorphism,
    /// ignoring <c>sh:resultMessage</c>.
    /// </summary>
    ShaclValidate,

    /// <summary>
    /// A SPARQL query-evaluation test (<c>mf:QueryEvaluationTest</c>): the
    /// query (<c>qt:query</c>) is executed against the data graph
    /// (<c>qt:data</c>) and its result compared to the expected
    /// <c>mf:result</c> fixture — a SPARQL Query Results serialization for
    /// <c>SELECT</c>/<c>ASK</c>, or an RDF graph for <c>CONSTRUCT</c>/
    /// <c>DESCRIBE</c>.
    /// </summary>
    SparqlQueryEvaluation,

    /// <summary>
    /// A SPARQL Update positive-syntax test (<c>mf:PositiveUpdateSyntaxTest</c>
    /// / <c>...Test11</c>): the update request (<c>mf:action</c>, a <c>.ru</c>
    /// file) must parse without error.
    /// </summary>
    PositiveUpdateSyntax,

    /// <summary>
    /// A SPARQL Update negative-syntax test (<c>mf:NegativeUpdateSyntaxTest</c>
    /// / <c>...Test11</c>): the update request must be rejected (the parser
    /// reports at least one error).
    /// </summary>
    NegativeUpdateSyntax,

    /// <summary>
    /// A SPARQL Update evaluation test (<c>mf:UpdateEvaluationTest</c>): the
    /// request (<c>ut:request</c>) is applied to the input data
    /// (<c>ut:data</c> / <c>ut:graphData</c>) and the resulting dataset
    /// compared to the expected (<c>mf:result</c>'s <c>ut:data</c> /
    /// <c>ut:graphData</c>).
    /// </summary>
    SparqlUpdateEvaluation,

    /// <summary>
    /// The manifest used a test type the harness does not know
    /// how to dispatch. The runner reports
    /// <see cref="W3cOutcomeStatus.Skipped"/> with the original
    /// IRI in the message.
    /// </summary>
    Unknown
}
