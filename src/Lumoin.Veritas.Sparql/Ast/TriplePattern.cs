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
[DebuggerDisplay("{Subject} {Predicate} {Object}")]
public sealed record TriplePattern(SourceSpan Span, TriplePatternTerm Subject, TriplePatternTerm Predicate, TriplePatternTerm Object);

/// <summary>
/// One position of a <see cref="TriplePattern"/>: a constant term, a variable, a
/// property path (predicate position only), a nested RDF 1.2 triple term, an RDF
/// collection, or a blank-node property list (the last two in subject or object
/// position). Every term carries its source <see cref="ConstantTerm.Span"/> (and
/// equivalents) for tooling.
/// </summary>
public abstract record TriplePatternTerm;

/// <summary>A ground RDF term (IRI, literal, or blank node).</summary>
/// <param name="Span">The source extent of the term.</param>
/// <param name="Term">The constant term.</param>
[DebuggerDisplay("{Term}")]
public sealed record ConstantTerm(SourceSpan Span, RdfTerm Term) : TriplePatternTerm;

/// <summary>A variable in a triple-pattern position.</summary>
/// <param name="Span">The source extent of the variable.</param>
/// <param name="Variable">The variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record VariableTerm(SourceSpan Span, SparqlVariable Variable) : TriplePatternTerm;

/// <summary>A property path in the predicate position.</summary>
/// <param name="Span">The source extent of the path.</param>
/// <param name="Path">The property-path expression.</param>
[DebuggerDisplay("path")]
public sealed record PropertyPathTerm(SourceSpan Span, PropertyPathExpression Path) : TriplePatternTerm;

/// <summary>A nested RDF 1.2 triple term in the subject or object position.</summary>
/// <param name="Span">The source extent of the triple term.</param>
/// <param name="Inner">The nested triple.</param>
[DebuggerDisplay("<<( triple )>>")]
public sealed record TripleTerm(SourceSpan Span, TriplePattern Inner) : TriplePatternTerm;

/// <summary>
/// An RDF 1.2 reified triple <c>&lt;&lt; s p o ~r? &gt;&gt;</c> in the subject or object position.
/// Unlike a <see cref="TripleTerm"/>, a reified triple asserts its inner triple and yields the reifier
/// as its value. Kept un-expanded for tooling fidelity (mirroring the Turtle parser's
/// <c>ReifiedTripleTerm</c>); the early normalization pass lowers it to the asserted triple plus the
/// <c>rdf:reifies</c> reification triple.
/// </summary>
/// <param name="Span">The source extent from <c>&lt;&lt;</c> to <c>&gt;&gt;</c>.</param>
/// <param name="Inner">The asserted inner triple.</param>
/// <param name="Reifier">The explicit reifier identity (an IRI, variable, or blank node), or <see langword="null"/> for a fresh anonymous reifier.</param>
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
[DebuggerDisplay("collection[{Items.Count}]")]
public sealed record CollectionTerm(SourceSpan Span, IReadOnlyList<TriplePatternTerm> Items) : TriplePatternTerm;

/// <summary>
/// A blank-node property list <c>[ p o ; ... ]</c> used as a subject or object
/// term. Kept un-expanded for tooling fidelity; an early normalization pass lowers
/// it to a fresh blank node carrying the listed predicate-object triples.
/// </summary>
/// <param name="Span">The source extent from the opening to the closing bracket.</param>
/// <param name="Properties">The predicate-object entries, in order; always non-empty (the empty <c>[]</c> is a plain anonymous blank node).</param>
[DebuggerDisplay("[ props={Properties.Count} ]")]
public sealed record BlankNodePropertyListTerm(SourceSpan Span, IReadOnlyList<PropertyListPath> Properties) : TriplePatternTerm;

/// <summary>One predicate with its object list inside a blank-node property list.</summary>
/// <param name="Span">The source extent of the predicate and its objects.</param>
/// <param name="Verb">The predicate term (an IRI, variable, or property path).</param>
/// <param name="Objects">The objects attached to the predicate, in order.</param>
[DebuggerDisplay("{Verb} [{Objects.Count}]")]
public sealed record PropertyListPath(SourceSpan Span, TriplePatternTerm Verb, IReadOnlyList<TriplePatternTerm> Objects);
