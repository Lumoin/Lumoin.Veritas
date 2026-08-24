using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Shacl.Validation.Evaluators;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// A registry mapping SHACL constraint-component IRIs to the
/// <see cref="ConstraintEvaluator"/> that evaluates them.
/// </summary>
/// <remarks>
/// <para>
/// The registry is immutable after construction. Entries are keyed by
/// the constraint-component IRI (e.g.,
/// <c>sh:MinCountConstraintComponent</c>) as exposed through
/// <see cref="Constraints.ConstraintComponent.ConstraintComponentIri"/>.
/// </para>
/// <para>
/// <see cref="GetOrDefault"/> returns
/// <see cref="NotImplementedEvaluator.EvaluateAsync"/> for components
/// without a registered evaluator, so the orchestrator never has to
/// special-case missing entries.
/// </para>
/// <para>
/// <b>Composition.</b> <see cref="With"/> and <see cref="WithMany"/>
/// produce a new registry with added or overridden bindings, leaving
/// the original unchanged. This is the idiomatic way to extend
/// <see cref="ShaclBuiltInEvaluators.All"/> with application-specific
/// constraint components:
/// </para>
/// <code>
/// ConstraintEvaluatorRegistry custom = ShaclBuiltInEvaluators.All
///     .With(MyCompanyComponentIri, MyCompanyEvaluator.EvaluateAsync);
/// </code>
/// <para>
/// Parallel to <see cref="Shacl.Components.ShaclBuiltInComponents"/> —
/// that class holds the <em>structural</em> metadata about each
/// component (parameters, kinds, factories for
/// <see cref="Shacl.Loading.ShapeLoader"/>); this class holds the
/// <em>behavioural</em> metadata (how to actually evaluate a loaded
/// constraint). They are deliberately separate because loading and
/// validation are distinct phases that ship independently.
/// </para>
/// </remarks>
public sealed class ConstraintEvaluatorRegistry
{
    private readonly ImmutableDictionary<Utf8String, ConstraintEvaluator> evaluators;

    /// <summary>
    /// Creates a registry populated from the given key-value pairs.
    /// </summary>
    /// <param name="evaluators">
    /// The evaluator bindings — each pair associates a component IRI
    /// with the evaluator that evaluates it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="evaluators"/> is <c>null</c>.
    /// </exception>
    public ConstraintEvaluatorRegistry(IEnumerable<KeyValuePair<Utf8String, ConstraintEvaluator>> evaluators)
    {
        ArgumentNullException.ThrowIfNull(evaluators);
        this.evaluators = evaluators.ToImmutableDictionary();
    }

    //Private constructor used by With / WithMany to avoid rebuilding
    //the immutable dictionary from scratch when we can simply forward
    //the SetItem / SetItems result.
    private ConstraintEvaluatorRegistry(ImmutableDictionary<Utf8String, ConstraintEvaluator> evaluators)
    {
        this.evaluators = evaluators;
    }

    /// <summary>
    /// Returns the evaluator registered for
    /// <paramref name="componentIri"/>, or
    /// <see cref="NotImplementedEvaluator.EvaluateAsync"/> when none is
    /// registered.
    /// </summary>
    public ConstraintEvaluator GetOrDefault(Utf8String componentIri)
        => evaluators.TryGetValue(componentIri, out ConstraintEvaluator? evaluator)
            ? evaluator
            : NotImplementedEvaluator.EvaluateAsync;

    /// <summary>
    /// Returns <c>true</c> when an evaluator is registered for
    /// <paramref name="componentIri"/>.
    /// </summary>
    public bool IsRegistered(Utf8String componentIri) => evaluators.ContainsKey(componentIri);

    /// <summary>
    /// The set of component IRIs for which an evaluator is registered.
    /// </summary>
    public IEnumerable<Utf8String> RegisteredComponentIris => evaluators.Keys;

    /// <summary>
    /// Returns a new registry that binds <paramref name="evaluator"/>
    /// to <paramref name="componentIri"/>, replacing any existing
    /// binding for that IRI. The original registry is unchanged.
    /// </summary>
    /// <param name="componentIri">The constraint-component IRI.</param>
    /// <param name="evaluator">The evaluator to associate with it.</param>
    /// <returns>A new registry with the added or replaced binding.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="evaluator"/> is <c>null</c>.
    /// </exception>
    public ConstraintEvaluatorRegistry With(Utf8String componentIri, ConstraintEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        return new ConstraintEvaluatorRegistry(evaluators.SetItem(componentIri, evaluator));
    }

    /// <summary>
    /// Returns a new registry that binds every pair in
    /// <paramref name="additions"/>, replacing any existing bindings
    /// for matching IRIs. The original registry is unchanged.
    /// </summary>
    /// <param name="additions">The evaluator bindings to add or override.</param>
    /// <returns>A new registry with the added or replaced bindings.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="additions"/> is <c>null</c>.
    /// </exception>
    public ConstraintEvaluatorRegistry WithMany(IEnumerable<KeyValuePair<Utf8String, ConstraintEvaluator>> additions)
    {
        ArgumentNullException.ThrowIfNull(additions);
        return new ConstraintEvaluatorRegistry(evaluators.SetItems(additions));
    }

    /// <summary>
    /// An empty registry — every constraint falls through to
    /// <see cref="NotImplementedEvaluator.EvaluateAsync"/>. Useful for
    /// testing validator infrastructure without any real evaluator
    /// semantics and as a starting baseline for callers that add their
    /// own evaluators selectively.
    /// </summary>
    public static ConstraintEvaluatorRegistry Empty { get; } = new([]);
}
