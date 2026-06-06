using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// The result of an EL classification: the subclass closure over the
/// ontology's named classes, per-class satisfiability, and the constructs
/// the classifier did not interpret.
/// </summary>
/// <remarks>
/// <para>
/// The closure is consumed as TBox knowledge, not as materialized triples:
/// the planner reads it as a-priori cardinality structure (an instance of a
/// subclass is an instance of every subsumer), and query-time expansion
/// reads it as the rewrite index for <c>rdf:type</c> patterns. An
/// unsatisfiable class is subsumed by every class; rather than flooding its
/// subsumer set, <see cref="IsSatisfiable"/> reports it.
/// </para>
/// </remarks>
[DebuggerDisplay("ElClassification Classes={subsumers.Count} Coherent={IsCoherent}")]
public sealed class ElClassification
{
    private readonly Dictionary<Utf8String, IReadOnlySet<Utf8String>> subsumers;

    private readonly HashSet<Utf8String> unsatisfiable;

    /// <summary>Whether every named class is satisfiable.</summary>
    public bool IsCoherent
    {
        get
        {
            return unsatisfiable.Count == 0;
        }
    }

    /// <summary>The constructs the classifier did not interpret, one note per occurrence — the honest coverage report.</summary>
    public IReadOnlyList<string> UnsupportedConstructs { get; }

    /// <summary>
    /// Initialises the classification from the classifier's outputs.
    /// </summary>
    /// <param name="subsumers">Per-class named subsumer sets (the class itself included).</param>
    /// <param name="unsatisfiable">The named classes equivalent to <c>owl:Nothing</c>.</param>
    /// <param name="unsupportedConstructs">The uninterpreted-construct notes.</param>
    public ElClassification(
        Dictionary<Utf8String, IReadOnlySet<Utf8String>> subsumers,
        HashSet<Utf8String> unsatisfiable,
        IReadOnlyList<string> unsupportedConstructs)
    {
        this.subsumers = subsumers;
        this.unsatisfiable = unsatisfiable;
        UnsupportedConstructs = unsupportedConstructs;
    }

    /// <summary>The named classes the classification covers.</summary>
    public IReadOnlyCollection<Utf8String> Classes
    {
        get
        {
            return subsumers.Keys;
        }
    }

    /// <summary>
    /// The named classes subsuming <paramref name="classIri"/>, the class
    /// itself included; empty for a class the ontology never mentions.
    /// </summary>
    /// <param name="classIri">The class IRI.</param>
    /// <returns>The subsumer set.</returns>
    public IReadOnlySet<Utf8String> SubsumersOf(Utf8String classIri)
    {
        return subsumers.TryGetValue(classIri, out IReadOnlySet<Utf8String>? result) ? result : EmptySet;
    }

    /// <summary>
    /// Whether <paramref name="classIri"/> is subsumed by
    /// <paramref name="subsumerIri"/> — an unsatisfiable class is subsumed
    /// by everything.
    /// </summary>
    /// <param name="classIri">The candidate subclass IRI.</param>
    /// <param name="subsumerIri">The candidate superclass IRI.</param>
    /// <returns><see langword="true"/> when the subsumption holds.</returns>
    public bool IsSubsumedBy(Utf8String classIri, Utf8String subsumerIri)
    {
        return unsatisfiable.Contains(classIri) || SubsumersOf(classIri).Contains(subsumerIri);
    }

    /// <summary>Whether the class can have instances.</summary>
    /// <param name="classIri">The class IRI.</param>
    /// <returns><see langword="false"/> when the class is equivalent to <c>owl:Nothing</c>.</returns>
    public bool IsSatisfiable(Utf8String classIri)
    {
        return !unsatisfiable.Contains(classIri);
    }

    private static IReadOnlySet<Utf8String> EmptySet { get; } = new HashSet<Utf8String>();
}
