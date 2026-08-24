using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A triple pattern at the AST level: three <see cref="TriplePatternTerm"/> values
/// for subject, predicate, and object. Distinct from
/// <see cref="Core.Hypertrie.Query.TriplePattern"/>, which is at the encoded-term
/// level; the hypertrie backend translates this AST form to that BGP form when it
/// assembles a basic graph pattern.
/// </summary>
/// <param name="Span">The source extent of the triple pattern.</param>
/// <param name="Subject">The subject term.</param>
/// <param name="Predicate">The predicate term (an IRI, variable, or property path).</param>
/// <param name="Object">The object term.</param>
/// <remarks>SPARQL <c>TriplesSameSubjectPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTriplesSameSubjectPath">SPARQL 1.2 §19.8 [TriplesSameSubjectPath]</see>.</remarks>
[DebuggerDisplay("{Subject} {Predicate} {Object}")]
public sealed record TriplePattern(SourceSpan Span, TriplePatternTerm Subject, TriplePatternTerm Predicate, TriplePatternTerm Object);

/// <summary>
/// One position of a <see cref="TriplePattern"/>: a constant term, a variable, a
/// property path (predicate position only), a nested RDF 1.2 triple term, an RDF
/// collection, or a blank-node property list (the last two in subject or object
/// position). Every term carries its source <see cref="ConstantTerm.Span"/> (and
/// equivalents) for tooling.
/// </summary>
/// <remarks>SPARQL <c>VarOrTerm</c> / <c>GraphNodePath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrTerm">SPARQL 1.2 §19.8 [VarOrTerm]</see>.</remarks>
public abstract record TriplePatternTerm;

/// <summary>A ground RDF term (IRI, literal, or blank node).</summary>
/// <param name="Span">The source extent of the term.</param>
/// <param name="Term">The constant term.</param>
/// <remarks>SPARQL <c>GraphTerm</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphTerm">SPARQL 1.2 §19.8 [GraphTerm]</see>.</remarks>
[DebuggerDisplay("{Term}")]
public sealed record ConstantTerm(SourceSpan Span, RdfTerm Term) : TriplePatternTerm;

/// <summary>A variable in a triple-pattern position.</summary>
/// <param name="Span">The source extent of the variable.</param>
/// <param name="Variable">The variable.</param>
/// <remarks>SPARQL <c>Var</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVar">SPARQL 1.2 §19.8 [Var]</see>.</remarks>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record VariableTerm(SourceSpan Span, SparqlVariable Variable) : TriplePatternTerm;

/// <summary>A property path in the predicate position.</summary>
/// <param name="Span">The source extent of the path.</param>
/// <param name="Path">The property-path expression.</param>
/// <remarks>SPARQL <c>Path</c> in <c>VerbPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPath">SPARQL 1.2 §19.8 [Path]</see>.</remarks>
[DebuggerDisplay("path")]
public sealed record PropertyPathTerm(SourceSpan Span, PropertyPathExpression Path) : TriplePatternTerm;

/// <summary>A nested RDF 1.2 triple term in the subject or object position.</summary>
/// <param name="Span">The source extent of the triple term.</param>
/// <param name="Inner">The nested triple.</param>
/// <remarks>SPARQL <c>TripleTerm</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
[DebuggerDisplay("<<( triple )>>")]
public sealed record TripleTerm(SourceSpan Span, TriplePattern Inner) : TriplePatternTerm;

/// <summary>
/// An RDF 1.2 reified triple <c>&lt;&lt; s p o ~r? &gt;&gt;</c> in the subject or object position. Its
/// value is the reifier. Per RDF 1.2 (Turtle §2.11 / §7.3.2) a reified triple does NOT assert its inner
/// triple — it produces only the reification triple <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c>
/// (only the annotation form <c>{| … |}</c> both reifies and asserts). Kept un-expanded for tooling
/// fidelity (mirroring the Turtle parser's <c>ReifiedTripleTerm</c>); the early normalization pass lowers
/// it to that reification triple — and, only under the opt-in
/// <c>SparqlNormalizerOptions.AssertReifiedTripleInnerTriple</c> flag, the base triple as well.
/// </summary>
/// <param name="Span">The source extent from <c>&lt;&lt;</c> to <c>&gt;&gt;</c>.</param>
/// <param name="Inner">The inner triple — reified, and not asserted unless the normalizer's opt-in assert flag is set.</param>
/// <param name="Reifier">The explicit reifier identity (an IRI, variable, or blank node), or <see langword="null"/> for a fresh anonymous reifier.</param>
/// <remarks>SPARQL <c>ReifiedTriple</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifiedTriple">SPARQL 1.2 §19.8 [ReifiedTriple]</see>.</remarks>
[DebuggerDisplay("<< triple >>")]
public sealed record ReifiedTriple(SourceSpan Span, TriplePattern Inner, TriplePatternTerm? Reifier) : TriplePatternTerm;

/// <summary>
/// An RDF collection <c>( ... )</c> used as a subject or object term. Kept
/// un-expanded for tooling fidelity; an early normalization pass lowers it to the
/// <c>rdf:first</c> / <c>rdf:rest</c> / <c>rdf:nil</c> triples (an empty collection
/// to <c>rdf:nil</c>) so the evaluation path sees plain triples.
/// </summary>
/// <param name="Span">The source extent from the opening to the closing parenthesis.</param>
/// <param name="Items">The collection items, in order; empty for <c>()</c>.</param>
/// <remarks>SPARQL <c>Collection</c> / <c>CollectionPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rCollectionPath">SPARQL 1.2 §19.8 [CollectionPath]</see>.</remarks>
[DebuggerDisplay("collection[{Items.Count}]")]
public sealed record CollectionTerm(SourceSpan Span, IReadOnlyList<TriplePatternTerm> Items) : TriplePatternTerm;

/// <summary>
/// A blank-node property list <c>[ p o ; ... ]</c> used as a subject or object
/// term. Kept un-expanded for tooling fidelity; an early normalization pass lowers
/// it to a fresh blank node carrying the listed predicate-object triples.
/// </summary>
/// <param name="Span">The source extent from the opening to the closing bracket.</param>
/// <param name="Properties">The predicate-object entries, in order; always non-empty (the empty <c>[]</c> is a plain anonymous blank node).</param>
/// <remarks>SPARQL <c>BlankNodePropertyListPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBlankNodePropertyListPath">SPARQL 1.2 §19.8 [BlankNodePropertyListPath]</see>.</remarks>
[DebuggerDisplay("[ props={Properties.Count} ]")]
public sealed record BlankNodePropertyListTerm(SourceSpan Span, IReadOnlyList<PropertyListPath> Properties) : TriplePatternTerm;

/// <summary>
/// An object together with the RDF 1.2 annotations attached to it in source — reifiers (<c>~ id?</c>)
/// and/or annotation blocks (<c>{| … |}</c>), in source order. Kept faithful (mirroring the Turtle
/// parser's <c>AnnotatedObject</c>); the early normalization pass lowers the annotations to the
/// <c>rdf:reifies</c> triples about the annotated triple's reifier.
/// </summary>
/// <param name="Span">The source extent from the object through its last annotation.</param>
/// <param name="Object">The annotated object term.</param>
/// <param name="Annotations">The reifiers and annotation blocks attached to the object, in source order; always non-empty.</param>
/// <remarks>SPARQL <c>AnnotationPath</c> on an object. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotationPath">SPARQL 1.2 §19.8 [AnnotationPath]</see>.</remarks>
[DebuggerDisplay("annotated {Object}")]
public sealed record AnnotatedObject(SourceSpan Span, TriplePatternTerm Object, IReadOnlyList<Annotation> Annotations) : TriplePatternTerm;

/// <summary>One annotation attached to a triple's object: a reifier or an annotation block.</summary>
/// <param name="Span">The source extent of the annotation.</param>
/// <remarks>SPARQL <c>Annotation</c> / <c>AnnotationPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotationPath">SPARQL 1.2 §19.8 [AnnotationPath]</see>.</remarks>
public abstract record Annotation(SourceSpan Span);

/// <summary>
/// A reifier annotation <c>~ id?</c> on a stated triple. <see cref="Reifier"/> is the explicit
/// identity (an IRI, variable, or blank node) or <see langword="null"/> for a fresh anonymous reifier.
/// </summary>
/// <param name="Span">The source extent of the reifier.</param>
/// <param name="Reifier">The reifier identity, or <see langword="null"/> for a fresh anonymous reifier.</param>
/// <remarks>SPARQL <c>Reifier</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifier">SPARQL 1.2 §19.8 [Reifier]</see>.</remarks>
public sealed record ReifierAnnotation(SourceSpan Span, TriplePatternTerm? Reifier) : Annotation(Span);

/// <summary>An annotation block <c>{| predicate-object list |}</c> asserting properties about the stated triple's reifier.</summary>
/// <param name="Span">The source extent from <c>{|</c> to <c>|}</c>.</param>
/// <param name="Properties">The predicate-object entries inside the block, in order; always non-empty.</param>
/// <remarks>SPARQL <c>AnnotationBlockPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotationBlockPath">SPARQL 1.2 §19.8 [AnnotationBlockPath]</see>.</remarks>
public sealed record AnnotationBlock(SourceSpan Span, IReadOnlyList<PropertyListPath> Properties) : Annotation(Span);

/// <summary>One predicate with its object list inside a blank-node property list.</summary>
/// <param name="Span">The source extent of the predicate and its objects.</param>
/// <param name="Verb">The predicate term (an IRI, variable, or property path).</param>
/// <param name="Objects">The objects attached to the predicate, in order.</param>
/// <remarks>SPARQL <c>PropertyListPathNotEmpty</c> entry. See <see href="https://www.w3.org/TR/sparql12-query/#rPropertyListPathNotEmpty">SPARQL 1.2 §19.8 [PropertyListPathNotEmpty]</see>.</remarks>
[DebuggerDisplay("{Verb} [{Objects.Count}]")]
public sealed record PropertyListPath(SourceSpan Span, TriplePatternTerm Verb, IReadOnlyList<TriplePatternTerm> Objects);
