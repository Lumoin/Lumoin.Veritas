using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A property-path expression at the AST level, before resolution to the
/// term-dictionary-encoded <see cref="Lumoin.Veritas.Rdf.PropertyPath"/>. Carries
/// <see cref="IriRef"/> predicates (resolved against the prologue) rather than
/// encoded ids; the translator lowers it to <see cref="Lumoin.Veritas.Rdf.PropertyPath"/>.
/// </summary>
public abstract record PropertyPathExpression;

/// <summary>A single predicate IRI.</summary>
/// <param name="Predicate">The predicate IRI.</param>
[DebuggerDisplay("<{Predicate.Value}>")]
public sealed record PathPredicate(IriRef Predicate) : PropertyPathExpression;

/// <summary>The inverse of a path: <c>^path</c>.</summary>
/// <param name="Inner">The path being inverted.</param>
[DebuggerDisplay("^(path)")]
public sealed record PathInverse(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>A sequence of paths: <c>p1 / p2 / ...</c>.</summary>
/// <param name="Steps">The path steps, in order.</param>
[DebuggerDisplay("seq[{Steps.Count}]")]
public sealed record PathSequence(IReadOnlyList<PropertyPathExpression> Steps) : PropertyPathExpression;

/// <summary>An alternative of paths: <c>p1 | p2 | ...</c>.</summary>
/// <param name="Alternatives">The path alternatives.</param>
[DebuggerDisplay("alt[{Alternatives.Count}]")]
public sealed record PathAlternative(IReadOnlyList<PropertyPathExpression> Alternatives) : PropertyPathExpression;

/// <summary>Zero or more repetitions: <c>path*</c>.</summary>
/// <param name="Inner">The repeated path.</param>
[DebuggerDisplay("(path)*")]
public sealed record PathZeroOrMore(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>One or more repetitions: <c>path+</c>.</summary>
/// <param name="Inner">The repeated path.</param>
[DebuggerDisplay("(path)+")]
public sealed record PathOneOrMore(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>Zero or one occurrence: <c>path?</c>.</summary>
/// <param name="Inner">The optional path.</param>
[DebuggerDisplay("(path)?")]
public sealed record PathZeroOrOne(PropertyPathExpression Inner) : PropertyPathExpression;

/// <summary>A negated property set: <c>!(p1 | ^p2 | ...)</c>.</summary>
/// <param name="Elements">The forward and inverse predicates excluded by the set.</param>
[DebuggerDisplay("!set[{Elements.Count}]")]
public sealed record PathNegatedSet(IReadOnlyList<PathNegatedElement> Elements) : PropertyPathExpression;

/// <summary>One element of a <see cref="PathNegatedSet"/>: a forward or inverse predicate.</summary>
public abstract record PathNegatedElement;

/// <summary>A forward predicate excluded by a negated property set.</summary>
/// <param name="Predicate">The excluded predicate IRI.</param>
[DebuggerDisplay("<{Predicate.Value}>")]
public sealed record PathNegatedForward(IriRef Predicate) : PathNegatedElement;

/// <summary>An inverse predicate excluded by a negated property set.</summary>
/// <param name="Predicate">The excluded inverse predicate IRI.</param>
[DebuggerDisplay("^<{Predicate.Value}>")]
public sealed record PathNegatedInverse(IriRef Predicate) : PathNegatedElement;
