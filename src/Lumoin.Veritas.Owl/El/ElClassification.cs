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
[DebuggerDisplay("ElClassification Classes={SubsumerSets.Count} Coherent={IsCoherent}")]
public sealed class ElClassification
{
    /// <summary>Per-class named subsumer sets, the class itself included.</summary>
    private Dictionary<Utf8String, IReadOnlySet<Utf8String>> SubsumerSets { get; }

    /// <summary>The inverted index: per-class named subsumee sets.</summary>
    private Dictionary<Utf8String, HashSet<Utf8String>> SubsumeeSets { get; }

    /// <summary>The named classes equivalent to <c>owl:Nothing</c>.</summary>
    private HashSet<Utf8String> Unsatisfiable { get; }

    /// <summary>Whether every named class is satisfiable.</summary>
    public bool IsCoherent
    {
        get
        {
            return Unsatisfiable.Count == 0;
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
        System.ArgumentNullException.ThrowIfNull(subsumers);

        SubsumerSets = subsumers;
        Unsatisfiable = unsatisfiable;
        UnsupportedConstructs = unsupportedConstructs;

        //The inverted index: query-time expansion asks "which classes are
        //BELOW C" (an rdf:type C pattern matches instances of any of them),
        //the dual of the subsumer sets.
        SubsumeeSets = [];
        foreach(KeyValuePair<Utf8String, IReadOnlySet<Utf8String>> entry in subsumers)
        {
            foreach(Utf8String subsumer in entry.Value)
            {
                if(!SubsumeeSets.TryGetValue(subsumer, out HashSet<Utf8String>? set))
                {
                    set = [];
                    SubsumeeSets[subsumer] = set;
                }

                set.Add(entry.Key);
            }
        }
    }

    /// <summary>The named classes the classification covers.</summary>
    public IReadOnlyCollection<Utf8String> Classes
    {
        get
        {
            return SubsumerSets.Keys;
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
        return SubsumerSets.TryGetValue(classIri, out IReadOnlySet<Utf8String>? result) ? result : EmptySet;
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
        return Unsatisfiable.Contains(classIri) || SubsumersOf(classIri).Contains(subsumerIri);
    }

    /// <summary>Whether the class can have instances.</summary>
    /// <param name="classIri">The class IRI.</param>
    /// <returns><see langword="false"/> when the class is equivalent to <c>owl:Nothing</c>.</returns>
    public bool IsSatisfiable(Utf8String classIri)
    {
        return !Unsatisfiable.Contains(classIri);
    }

    /// <summary>
    /// The named classes subsumed by <paramref name="classIri"/>, the class
    /// itself included — the expansion set an <c>rdf:type</c> pattern over
    /// the class rewrites to, and the extent union a planner estimates
    /// cardinality from.
    /// </summary>
    /// <param name="classIri">The class IRI.</param>
    /// <returns>The subsumee set; empty for a class the ontology never mentions.</returns>
    public IReadOnlySet<Utf8String> SubsumeesOf(Utf8String classIri)
    {
        return SubsumeeSets.TryGetValue(classIri, out HashSet<Utf8String>? result) ? result : EmptySet;
    }

    private static IReadOnlySet<Utf8String> EmptySet { get; } = new HashSet<Utf8String>();
}
