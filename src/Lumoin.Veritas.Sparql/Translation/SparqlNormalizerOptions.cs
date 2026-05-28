namespace Lumoin.Veritas.Sparql.Translation;

/// <summary>
/// Options controlling the <see cref="SparqlNormalizer"/> sugar-lowering pass.
/// </summary>
/// <remarks>
/// The defaults produce strictly spec-faithful output, so the normalized AST a conformance run
/// sees matches RDF 1.2 / SPARQL 1.2 exactly. Non-default options are deliberate, documented
/// extensions a consumer opts into.
/// </remarks>
public sealed record SparqlNormalizerOptions
{
    /// <summary>Gets the default options: strictly spec-faithful lowering.</summary>
    public static SparqlNormalizerOptions Default { get; } = new();

    /// <summary>
    /// Gets whether lowering a reified triple (<c>&lt;&lt; s p o ~r? &gt;&gt;</c>, whether standalone or
    /// used as a subject/object term) also asserts its inner base triple <c>s p o</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> — the RDF 1.2 semantics (Turtle §2.11 / §7.3.2): a reified
    /// triple yields only the reifier and the reification triple <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c>;
    /// it does <em>not</em> add <c>s p o</c> to the graph. Only the annotation syntax <c>{| … |}</c> both
    /// reifies and asserts. Setting this to <see langword="true"/> additionally emits the base triple — a
    /// non-standard extension that diverges from the specification, provided for consumers that want a
    /// reified triple to imply its inner assertion.
    /// </remarks>
    public bool AssertReifiedTripleInnerTriple { get; init; }
}
