using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A triple pattern at the AST level: three <see cref="TriplePatternTerm"/> values
/// for subject, predicate, and object. Distinct from
/// <see cref="Core.Hypertrie.Query.TriplePattern"/>, which is at the encoded-term
/// level; the hypertrie backend translates this AST form to that BGP form when it
/// assembles a basic graph pattern.
/// </summary>
/// <param name="Subject">The subject term.</param>
/// <param name="Predicate">The predicate term (an IRI, variable, or property path).</param>
/// <param name="Object">The object term.</param>
[DebuggerDisplay("{Subject} {Predicate} {Object}")]
public sealed record TriplePattern(TriplePatternTerm Subject, TriplePatternTerm Predicate, TriplePatternTerm Object);

/// <summary>
/// One position of a <see cref="TriplePattern"/>: a constant term, a variable, a
/// property path (predicate position only), or a nested RDF 1.2 triple term
/// (subject or object position).
/// </summary>
public abstract record TriplePatternTerm;

/// <summary>A ground RDF term (IRI, literal, or blank node).</summary>
/// <param name="Term">The constant term.</param>
[DebuggerDisplay("{Term}")]
public sealed record ConstantTerm(RdfTerm Term) : TriplePatternTerm;

/// <summary>A variable in a triple-pattern position.</summary>
/// <param name="Variable">The variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record VariableTerm(SparqlVariable Variable) : TriplePatternTerm;

/// <summary>A property path in the predicate position.</summary>
/// <param name="Path">The property-path expression.</param>
[DebuggerDisplay("path")]
public sealed record PropertyPathTerm(PropertyPathExpression Path) : TriplePatternTerm;

/// <summary>A nested RDF 1.2 triple term in the subject or object position.</summary>
/// <param name="Inner">The nested triple.</param>
[DebuggerDisplay("<<( triple )>>")]
public sealed record TripleTerm(TriplePattern Inner) : TriplePatternTerm;
