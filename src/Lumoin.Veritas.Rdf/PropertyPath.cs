using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The abstract syntax tree for SPARQL 1.1 / SHACL Core property paths, over
/// encoded predicate identifiers.
/// </summary>
/// <remarks>
/// <para>
/// Property paths are used by SHACL <c>sh:path</c> and by SPARQL graph patterns.
/// They describe a reachability relation between two nodes in terms of the
/// predicates that connect them. The constructors are defined in
/// <see href="https://www.w3.org/TR/sparql11-query/#propertypaths">SPARQL 1.1 §9</see>
/// and mirrored for SHACL in
/// <see href="https://www.w3.org/TR/shacl12-core/#property-paths">SHACL 1.2 Core §2.3</see>.
/// </para>
/// <para>
/// Paths are immutable value-equal records. The <see cref="PropertyPathEvaluator"/>
/// consumes this AST together with a <see cref="Core.StorageDelegates.MatchTriplesAsync"/>
/// delegate to produce the set of nodes reachable from a given start node via the path.
/// </para>
/// <para>
/// Use pattern matching to dispatch:
/// </para>
/// <code>
/// string description = path switch
/// {
///     PredicatePath p => $"P({p.Predicate})",
///     SequencePath s => "Seq",
///     AlternativePath a => "Alt",
///     InversePath i => $"Inv({i.Inner})",
///     ZeroOrMorePath z => "*",
///     OneOrMorePath o => "+",
///     ZeroOrOnePath q => "?",
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </remarks>
public abstract record PropertyPath;

/// <summary>
/// A single-predicate path. Matches triples <c>(?s, Predicate, ?o)</c>.
/// </summary>
/// <remarks>
/// The predicate is typed as <see cref="IriId"/> rather than a generic
/// <see cref="TermId"/> because RDF predicates are IRIs by definition
/// (<see href="https://www.w3.org/TR/rdf12-concepts/#section-triples">RDF 1.2 Concepts §3.6</see>).
/// </remarks>
/// <param name="Predicate">The encoded predicate identifier.</param>
[DebuggerDisplay("P({Predicate})")]
public sealed record PredicatePath(IriId Predicate): PropertyPath;

/// <summary>
/// Sequence path: <c>A / B / C</c>. Evaluated left-to-right, feeding the result
/// of each step as the start set of the next step.
/// </summary>
/// <param name="Steps">The ordered sequence of sub-paths. Must contain at least two elements.</param>
[DebuggerDisplay("Seq[{Steps.Count}]")]
public sealed record SequencePath(ImmutableArray<PropertyPath> Steps): PropertyPath;

/// <summary>
/// Alternative path: <c>A | B | C</c>. The result is the union of evaluating
/// each alternative independently from the same start set.
/// </summary>
/// <param name="Alternatives">The set of alternative sub-paths. Must contain at least two elements.</param>
[DebuggerDisplay("Alt[{Alternatives.Count}]")]
public sealed record AlternativePath(ImmutableArray<PropertyPath> Alternatives): PropertyPath;

/// <summary>
/// Inverse path: <c>^A</c>. Evaluates the inner path with triples traversed in
/// the opposite direction (from object to subject).
/// </summary>
/// <param name="Inner">The sub-path to invert.</param>
[DebuggerDisplay("Inv({Inner})")]
public sealed record InversePath(PropertyPath Inner): PropertyPath;

/// <summary>
/// Zero-or-more path: <c>A*</c>. The set of nodes reachable via zero or more
/// applications of the inner path. Includes the start node.
/// </summary>
/// <param name="Inner">The sub-path to iterate.</param>
[DebuggerDisplay("{Inner}*")]
public sealed record ZeroOrMorePath(PropertyPath Inner): PropertyPath;

/// <summary>
/// One-or-more path: <c>A+</c>. The set of nodes reachable via one or more
/// applications of the inner path. Excludes the start node unless reachable by a cycle.
/// </summary>
/// <param name="Inner">The sub-path to iterate.</param>
[DebuggerDisplay("{Inner}+")]
public sealed record OneOrMorePath(PropertyPath Inner): PropertyPath;

/// <summary>
/// Zero-or-one path: <c>A?</c>. The set containing the start node and any node
/// reachable via exactly one application of the inner path.
/// </summary>
/// <param name="Inner">The optional sub-path.</param>
[DebuggerDisplay("{Inner}?")]
public sealed record ZeroOrOnePath(PropertyPath Inner): PropertyPath;

/// <summary>
/// Negated property set: <c>!(p1 | ... | ^q1 | ...)</c>. Matches a single step over
/// <b>any</b> predicate that is not one of the excluded ones — <see cref="Forward"/>
/// excludes forward edges <c>(start, p, o)</c> and <see cref="Inverse"/> excludes
/// inverse edges <c>(o, q, start)</c>. The result is the union of the surviving forward
/// objects and inverse subjects.
/// </summary>
/// <remarks>
/// A negated set carries no predicate to descend on, so — unlike the other leaf
/// constructors — it cannot use the bound-predicate batch primitives; it scans each
/// start's incident triples with a wildcard predicate and filters out the excluded set.
/// See <see href="https://www.w3.org/TR/sparql12-query/#eval_negatedPropertySet">SPARQL 1.2 §18.4 negated property set evaluation</see>.
/// </remarks>
/// <param name="Forward">The forward predicates excluded from <c>(start, p, o)</c> matches; may be empty.</param>
/// <param name="Inverse">The inverse predicates excluded from <c>(o, q, start)</c> matches; may be empty.</param>
[DebuggerDisplay("!set(fwd={Forward.Length},inv={Inverse.Length})")]
public sealed record NegatedPropertySet(ImmutableArray<IriId> Forward, ImmutableArray<IriId> Inverse): PropertyPath;
