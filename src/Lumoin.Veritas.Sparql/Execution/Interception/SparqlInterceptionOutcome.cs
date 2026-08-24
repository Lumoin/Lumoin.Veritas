using System;
using Lumoin.Veritas.Sparql.Algebra;

namespace Lumoin.Veritas.Sparql.Execution.Interception;

/// <summary>How an evaluation interception responded at one expand-phase operator position.</summary>
internal enum SparqlInterceptionApplication
{
    /// <summary>The entry's pattern or guards did not hold; the driver consults the next entry, then expands normally.</summary>
    Declined = 0,

    /// <summary>The entry answered the node's whole subtree with a table; the driver records it and skips the subtree.</summary>
    Answered = 1,

    /// <summary>The entry recorded a leaf annotation (a row cap); the node itself still expands and evaluates normally.</summary>
    Annotated = 2
}

/// <summary>
/// One interception entry's outcome at one operator position: the verdict plus its per-verdict payload — the
/// answering table, or the leaf-cap annotation the driver records before the leaf evaluates. Constructed
/// only through the static factories, so an answered outcome always carries its table and an annotated one
/// its target.
/// </summary>
internal readonly record struct SparqlInterceptionOutcome
{
    /// <summary>Constructs an outcome; reachable only through the static factories.</summary>
    /// <param name="application">The entry's verdict.</param>
    /// <param name="table">The answering table, populated for <see cref="SparqlInterceptionApplication.Answered"/>.</param>
    /// <param name="annotationTarget">The leaf the annotation applies to, populated for <see cref="SparqlInterceptionApplication.Annotated"/>.</param>
    /// <param name="annotationCap">The leaf row cap, populated for <see cref="SparqlInterceptionApplication.Annotated"/>.</param>
    private SparqlInterceptionOutcome(SparqlInterceptionApplication application, SolutionTable? table, AlgebraOperator? annotationTarget, int annotationCap)
    {
        Application = application;
        Table = table;
        AnnotationTarget = annotationTarget;
        AnnotationCap = annotationCap;
    }

    /// <summary>The entry's verdict at this position.</summary>
    public SparqlInterceptionApplication Application { get; }

    /// <summary>The answering table when <see cref="Application"/> is <see cref="SparqlInterceptionApplication.Answered"/>; <see langword="null"/> otherwise.</summary>
    public SolutionTable? Table { get; }

    /// <summary>The leaf the annotation applies to when <see cref="Application"/> is <see cref="SparqlInterceptionApplication.Annotated"/>; <see langword="null"/> otherwise.</summary>
    public AlgebraOperator? AnnotationTarget { get; }

    /// <summary>The leaf row cap when <see cref="Application"/> is <see cref="SparqlInterceptionApplication.Annotated"/>; zero otherwise.</summary>
    public int AnnotationCap { get; }

    /// <summary>The shared declined outcome.</summary>
    public static SparqlInterceptionOutcome Declined { get; } = new(SparqlInterceptionApplication.Declined, table: null, annotationTarget: null, annotationCap: 0);

    /// <summary>The entry answered the node's subtree with <paramref name="table"/>.</summary>
    /// <param name="table">The answering table.</param>
    /// <returns>The answered outcome.</returns>
    public static SparqlInterceptionOutcome Answered(SolutionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return new SparqlInterceptionOutcome(SparqlInterceptionApplication.Answered, table, annotationTarget: null, annotationCap: 0);
    }

    /// <summary>The entry recorded a row cap on a leaf the driver will visit later in this evaluation.</summary>
    /// <param name="target">The leaf the cap applies to.</param>
    /// <param name="cap">The maximum rows the leaf needs to drain.</param>
    /// <returns>The annotated outcome.</returns>
    public static SparqlInterceptionOutcome LeafCap(AlgebraOperator target, int cap)
    {
        ArgumentNullException.ThrowIfNull(target);

        return new SparqlInterceptionOutcome(SparqlInterceptionApplication.Annotated, table: null, target, cap);
    }
}
