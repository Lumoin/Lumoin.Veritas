using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A property-path expression at the AST level, before resolution to the
/// term-dictionary-encoded <see cref="Lumoin.Veritas.Rdf.PropertyPath"/>. Carries
/// <see cref="IriRef"/> predicates (resolved against the prologue) rather than
/// encoded ids; the translator lowers it to <see cref="Lumoin.Veritas.Rdf.PropertyPath"/>.
/// </summary>
/// <remarks>SPARQL <c>Path</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPath">SPARQL 1.2 §19.8 [Path]</see>.</remarks>
public abstract record PropertyPathExpression;

/// <summary>A single predicate IRI.</summary>
/// <param name="Predicate">The predicate IRI.</param>
/// <remarks>SPARQL <c>PathPrimary</c> (an <c>iri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rPathPrimary">SPARQL 1.2 §19.8 [PathPrimary]</see>.</remarks>
[DebuggerDisplay("<{Predicate.Value}>")]
public sealed record PathPredicate(IriRef Predicate) : PropertyPathExpression;

/// <summary>The inverse of a path: <c>^path</c>.</summary>
/// <param name="Inner">The path being inverted.</param>
/// <remarks>SPARQL <c>PathEltOrInverse</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathEltOrInverse">SPARQL 1.2 §19.8 [PathEltOrInverse]</see>.</remarks>
[DebuggerDisplay("^(path)")]
public sealed record PathInverse(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>A sequence of paths: <c>p1 / p2 / ...</c>.</summary>
/// <param name="Steps">The path steps, in order.</param>
/// <remarks>SPARQL <c>PathSequence</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathSequence">SPARQL 1.2 §19.8 [PathSequence]</see>.</remarks>
[DebuggerDisplay("seq[{Steps.Count}]")]
public sealed record PathSequence(IReadOnlyList<PropertyPathExpression> Steps) : PropertyPathExpression;

/// <summary>An alternative of paths: <c>p1 | p2 | ...</c>.</summary>
/// <param name="Alternatives">The path alternatives.</param>
/// <remarks>SPARQL <c>PathAlternative</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathAlternative">SPARQL 1.2 §19.8 [PathAlternative]</see>.</remarks>
[DebuggerDisplay("alt[{Alternatives.Count}]")]
public sealed record PathAlternative(IReadOnlyList<PropertyPathExpression> Alternatives) : PropertyPathExpression;

/// <summary>Zero or more repetitions: <c>path*</c>.</summary>
/// <param name="Inner">The repeated path.</param>
/// <remarks>SPARQL <c>PathMod</c> (<c>*</c>) on <c>PathElt</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathMod">SPARQL 1.2 §19.8 [PathMod]</see>.</remarks>
[DebuggerDisplay("(path)*")]
public sealed record PathZeroOrMore(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>One or more repetitions: <c>path+</c>.</summary>
/// <param name="Inner">The repeated path.</param>
/// <remarks>SPARQL <c>PathMod</c> (<c>+</c>) on <c>PathElt</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathMod">SPARQL 1.2 §19.8 [PathMod]</see>.</remarks>
[DebuggerDisplay("(path)+")]
public sealed record PathOneOrMore(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>Zero or one occurrence: <c>path?</c>.</summary>
/// <param name="Inner">The optional path.</param>
/// <remarks>SPARQL <c>PathMod</c> (<c>?</c>) on <c>PathElt</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathMod">SPARQL 1.2 §19.8 [PathMod]</see>.</remarks>
[DebuggerDisplay("(path)?")]
public sealed record PathZeroOrOne(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>A negated property set: <c>!(p1 | ^p2 | ...)</c>.</summary>
/// <param name="Elements">The forward and inverse predicates excluded by the set.</param>
/// <remarks>SPARQL <c>PathNegatedPropertySet</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathNegatedPropertySet">SPARQL 1.2 §19.8 [PathNegatedPropertySet]</see>.</remarks>
[DebuggerDisplay("!set[{Elements.Count}]")]
public sealed record PathNegatedSet(IReadOnlyList<PathNegatedElement> Elements) : PropertyPathExpression;

/// <summary>One element of a <see cref="PathNegatedSet"/>: a forward or inverse predicate.</summary>
/// <param name="Predicate">The excluded predicate IRI (forward or inverse per the concrete element).</param>
/// <remarks>SPARQL <c>PathOneInPropertySet</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathOneInPropertySet">SPARQL 1.2 §19.8 [PathOneInPropertySet]</see>.</remarks>
public abstract record PathNegatedElement(IriRef Predicate);

/// <summary>A forward predicate excluded by a negated property set.</summary>
/// <param name="Predicate">The excluded predicate IRI.</param>
/// <remarks>SPARQL <c>PathOneInPropertySet</c> (forward <c>iri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rPathOneInPropertySet">SPARQL 1.2 §19.8 [PathOneInPropertySet]</see>.</remarks>
[DebuggerDisplay("<{Predicate.Value}>")]
public sealed record PathNegatedForward(IriRef Predicate) : PathNegatedElement(Predicate);

/// <summary>An inverse predicate excluded by a negated property set.</summary>
/// <param name="Predicate">The excluded inverse predicate IRI.</param>
/// <remarks>SPARQL <c>PathOneInPropertySet</c> (inverse <c>^ iri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rPathOneInPropertySet">SPARQL 1.2 §19.8 [PathOneInPropertySet]</see>.</remarks>
[DebuggerDisplay("^<{Predicate.Value}>")]
public sealed record PathNegatedInverse(IriRef Predicate) : PathNegatedElement(Predicate);
