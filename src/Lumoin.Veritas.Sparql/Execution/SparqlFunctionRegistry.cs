using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The value an extension function invocation produces: either a bound RDF term or the SPARQL expression
/// error value (§17.2). The default instance is the error, so no state of this struct can smuggle an
/// unbound term into evaluation.
/// </summary>
/// <param name="TermOrNull">The bound term, or <see langword="null"/> for the error value.</param>
public readonly record struct SparqlFunctionResult(RdfTerm? TermOrNull)
{
    /// <summary>The expression error value — what an invocation answers for a wrong arity, an argument outside the function's domain, or any other condition the function declines to map to a term.</summary>
    public static SparqlFunctionResult Error => default;

    /// <summary>Whether the invocation produced the error value.</summary>
    public bool IsError => TermOrNull is null;

    /// <summary>The bound term; valid only when <see cref="IsError"/> is <see langword="false"/>.</summary>
    public RdfTerm Term => TermOrNull!;

    /// <summary>Wraps a bound term as a (non-error) result.</summary>
    /// <param name="term">The bound term.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <see langword="null"/>.</exception>
    public static SparqlFunctionResult Of(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return new SparqlFunctionResult(term);
    }
}

/// <summary>
/// Evaluates one extension-function invocation (SPARQL §17.6) over already-evaluated argument values. The
/// evaluator resolves argument errors before consulting the function — an error argument makes the
/// invocation an error without the function ever running — so <paramref name="arguments"/> holds bound
/// terms only. The function owns its arity and its domain: a wrong argument count or an argument outside
/// the domain answers <see cref="SparqlFunctionResult.Error"/>, never an exception — expression errors are
/// values in SPARQL, and a throwing implementation is an invariant violation that propagates as a fault.
/// </summary>
/// <remarks>
/// <b>Synchronous by design.</b> Invocations run on the per-solution expression hot path (a <c>FILTER</c>
/// over every solution, an <c>ORDER BY</c> comparator), mirroring the randomness/digest/regex seams on
/// <see cref="SparqlExpressionContext"/>. An implementation that genuinely needs async I/O belongs behind a
/// pre-computed binding, not on the value-expression hot path.
/// </remarks>
/// <param name="functionIri">The invoked function's IRI, so one implementation can serve several registered names.</param>
/// <param name="arguments">The evaluated argument values, in call order; none is an error.</param>
/// <param name="context">The evaluation context, carrying the fixed query timestamp, the implicit timezone, and the value-layer datatype registry.</param>
/// <returns>The invocation's result: a bound term or the error value.</returns>
public delegate SparqlFunctionResult SparqlFunctionDelegate(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context);

/// <summary>
/// The evaluated inputs one extension-aggregate invocation folds: the group's argument values, in
/// member order. The engine has already resolved the per-member discipline before the fold runs —
/// members whose argument variables were unbound are dropped, a member whose argument errored over
/// bound data has already failed the whole aggregate, and <c>DISTINCT</c> has already deduplicated by
/// RDF term equality — so <see cref="Values"/> holds bound terms only. An empty group reaches the fold
/// as an empty span: each aggregate owns its empty-group answer. The dedicated carrier type keeps the
/// aggregate delegate nominally distinct from <see cref="SparqlFunctionDelegate"/>, so a scalar method
/// group can never satisfy an aggregate registration by signature accident.
/// </summary>
public readonly ref struct SparqlAggregateGroup
{
    /// <summary>Wraps a group's evaluated argument values.</summary>
    /// <param name="values">The values, in member order.</param>
    public SparqlAggregateGroup(ReadOnlySpan<RdfTerm> values)
    {
        Values = values;
    }

    /// <summary>The group's evaluated argument values, in member order; none is an error.</summary>
    public ReadOnlySpan<RdfTerm> Values { get; }
}

/// <summary>
/// Folds one extension-aggregate invocation over a group's evaluated argument values. The aggregate
/// owns its value domain and its empty-group answer: a value outside the domain, an undefined
/// empty-group fold, or any other condition the aggregate declines to map to a term answers
/// <see cref="SparqlFunctionResult.Error"/>, never an exception — expression errors are values in
/// SPARQL, and a throwing implementation is an invariant violation that propagates as a fault.
/// </summary>
/// <remarks>
/// <b>Synchronous by design</b>, mirroring <see cref="SparqlFunctionDelegate"/>: the fold runs once
/// per group inside aggregation, on the engine's evaluation path.
/// </remarks>
/// <param name="functionIri">The invoked aggregate's IRI, so one implementation can serve several registered names.</param>
/// <param name="group">The group's evaluated argument values.</param>
/// <param name="context">The evaluation context, carrying the fixed query timestamp, the implicit timezone, and the value-layer datatype registry.</param>
/// <returns>The fold's result: a bound term or the error value.</returns>
public delegate SparqlFunctionResult SparqlAggregateDelegate(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context);

/// <summary>The typed outcome kind of an extension-function registration attempt.</summary>
public enum SparqlFunctionRegistrationKind
{
    /// <summary>The function was accepted.</summary>
    Accepted,

    /// <summary>The IRI is reserved — in the XSD namespace, whose constructor-cast semantics (§17.5) are the evaluator's own and not overridable, present casts and future ones alike.</summary>
    [SuppressMessage("Naming", "CA1700:Do not name enum values 'Reserved'", Justification = "The member names the reservation-gate outcome — an IRI the evaluator reserves against registration — not a placeholder for future use.")]
    RejectedReservedIri,

    /// <summary>A function for the same IRI was already registered.</summary>
    RejectedDuplicate,

    /// <summary>The IRI is empty, so no call expression could ever name it.</summary>
    RejectedEmptyIri,
}

/// <summary>
/// The value-based outcome of an extension-function registration attempt — never an exception, because a
/// declined registration is an expected operator-configuration condition.
/// </summary>
/// <param name="Kind">The outcome kind.</param>
/// <param name="FunctionIri">The IRI the attempt was for.</param>
public readonly record struct SparqlFunctionRegistration(SparqlFunctionRegistrationKind Kind, Utf8String FunctionIri)
{
    /// <summary>An accepted outcome.</summary>
    /// <param name="functionIri">The registered IRI.</param>
    /// <returns>The outcome.</returns>
    public static SparqlFunctionRegistration Accepted(Utf8String functionIri)
    {
        return new SparqlFunctionRegistration(SparqlFunctionRegistrationKind.Accepted, functionIri);
    }

    /// <summary>A reserved-IRI rejection.</summary>
    /// <param name="functionIri">The reserved IRI.</param>
    /// <returns>The outcome.</returns>
    public static SparqlFunctionRegistration RejectedReservedIri(Utf8String functionIri)
    {
        return new SparqlFunctionRegistration(SparqlFunctionRegistrationKind.RejectedReservedIri, functionIri);
    }

    /// <summary>A duplicate-IRI rejection.</summary>
    /// <param name="functionIri">The IRI already registered.</param>
    /// <returns>The outcome.</returns>
    public static SparqlFunctionRegistration RejectedDuplicate(Utf8String functionIri)
    {
        return new SparqlFunctionRegistration(SparqlFunctionRegistrationKind.RejectedDuplicate, functionIri);
    }

    /// <summary>An empty-IRI rejection.</summary>
    /// <param name="functionIri">The empty IRI.</param>
    /// <returns>The outcome.</returns>
    public static SparqlFunctionRegistration RejectedEmptyIri(Utf8String functionIri)
    {
        return new SparqlFunctionRegistration(SparqlFunctionRegistrationKind.RejectedEmptyIri, functionIri);
    }
}

/// <summary>
/// An immutable, frozen set of extension functions (SPARQL §17.6) keyed by IRI, each carrying a scalar
/// face, an aggregate face, or both. Constructed only through <see cref="SparqlFunctionRegistryBuilder"/>,
/// which runs the reservation gate before a function enters the set. <see cref="Empty"/> is the
/// composition default, under which every extension-function IRI evaluates to the expression error value;
/// <see cref="IsEmpty"/> is a get-only bool set at construction so the no-op posture costs one predicted
/// branch to detect. <see cref="AggregateIris"/> is the frozen recognition profile the translator lifts
/// IRI aggregate calls against — the registry is frozen at engine composition, so one engine always reads
/// one profile. There is no arity keying — the function owns its arity, so a signature change upstream is
/// a function-body concern, never a registry-shape change.
/// </summary>
public sealed class SparqlFunctionRegistry
{
    /// <summary>The registered scalar implementations keyed by IRI.</summary>
    private FrozenDictionary<Utf8String, SparqlFunctionDelegate> ScalarEntries { get; }

    /// <summary>The registered aggregate implementations keyed by IRI.</summary>
    private FrozenDictionary<Utf8String, SparqlAggregateDelegate> AggregateEntries { get; }

    /// <summary>Wraps the frozen implementation sets.</summary>
    /// <param name="scalarEntries">The registered scalar implementations.</param>
    /// <param name="aggregateEntries">The registered aggregate implementations.</param>
    private SparqlFunctionRegistry(FrozenDictionary<Utf8String, SparqlFunctionDelegate> scalarEntries, FrozenDictionary<Utf8String, SparqlAggregateDelegate> aggregateEntries)
    {
        ScalarEntries = scalarEntries;
        AggregateEntries = aggregateEntries;
        AggregateIris = aggregateEntries.Keys.ToFrozenSet();
        IsEmpty = scalarEntries.Count == 0 && aggregateEntries.Count == 0;
    }

    /// <summary>Whether no function of either face is registered — the fast path the evaluator tests first to skip the lookup entirely.</summary>
    public bool IsEmpty { get; }

    /// <summary>The registered aggregate-function IRIs: the frozen recognition profile under which the translator lifts an IRI function call into aggregation.</summary>
    public IReadOnlySet<Utf8String> AggregateIris { get; }

    /// <summary>The registered scalar extension-function IRIs — the registry's discovery face, which a host's service description enumerates.</summary>
    public IReadOnlyCollection<Utf8String> FunctionIris => ScalarEntries.Keys;

    /// <summary>The empty registry — the null object a host with no registered extension functions uses.</summary>
    public static SparqlFunctionRegistry Empty { get; } = new(new Dictionary<Utf8String, SparqlFunctionDelegate>().ToFrozenDictionary(), new Dictionary<Utf8String, SparqlAggregateDelegate>().ToFrozenDictionary());

    /// <summary>Freezes the accepted implementation sets into a registry.</summary>
    /// <param name="scalarEntries">The accepted scalar implementations keyed by IRI.</param>
    /// <param name="aggregateEntries">The accepted aggregate implementations keyed by IRI.</param>
    /// <returns>The frozen registry.</returns>
    internal static SparqlFunctionRegistry FromEntries(IReadOnlyDictionary<Utf8String, SparqlFunctionDelegate> scalarEntries, IReadOnlyDictionary<Utf8String, SparqlAggregateDelegate> aggregateEntries)
    {
        Dictionary<Utf8String, SparqlFunctionDelegate> scalarCopy = new(scalarEntries.Count);
        foreach(KeyValuePair<Utf8String, SparqlFunctionDelegate> entry in scalarEntries)
        {
            scalarCopy[entry.Key] = entry.Value;
        }

        Dictionary<Utf8String, SparqlAggregateDelegate> aggregateCopy = new(aggregateEntries.Count);
        foreach(KeyValuePair<Utf8String, SparqlAggregateDelegate> entry in aggregateEntries)
        {
            aggregateCopy[entry.Key] = entry.Value;
        }

        return new SparqlFunctionRegistry(scalarCopy.ToFrozenDictionary(), aggregateCopy.ToFrozenDictionary());
    }

    /// <summary>Looks up the registered scalar implementation for an IRI.</summary>
    /// <param name="iri">The function IRI.</param>
    /// <param name="registered">The registered scalar implementation, when present.</param>
    /// <returns><see langword="true"/> when a scalar implementation is registered for the IRI.</returns>
    public bool TryGet(Utf8String iri, [MaybeNullWhen(false)] out SparqlFunctionDelegate registered)
    {
        return ScalarEntries.TryGetValue(iri, out registered);
    }

    /// <summary>Looks up the registered aggregate implementation for an IRI.</summary>
    /// <param name="iri">The function IRI.</param>
    /// <param name="registered">The registered aggregate implementation, when present.</param>
    /// <returns><see langword="true"/> when an aggregate implementation is registered for the IRI.</returns>
    public bool TryGetAggregate(Utf8String iri, [MaybeNullWhen(false)] out SparqlAggregateDelegate registered)
    {
        return AggregateEntries.TryGetValue(iri, out registered);
    }
}

/// <summary>
/// Builds a <see cref="SparqlFunctionRegistry"/>. Each <see cref="Add(Utf8String, SparqlFunctionDelegate)"/>
/// runs the acceptance rule — reject an empty IRI, reject a reserved IRI, reject a duplicate — and returns a
/// value-based <see cref="SparqlFunctionRegistration"/> without ever throwing for those expected conditions.
/// Every outcome, declined ones included, also accumulates on <see cref="Outcomes"/> so a composition site
/// can inspect a bulk registration after the fact — a declined registration is never a silent drop.
/// </summary>
public sealed class SparqlFunctionRegistryBuilder
{
    /// <summary>The XSD namespace prefix bytes (<c>http://www.w3.org/2001/XMLSchema#</c>) — the evaluator's own constructor-cast jurisdiction (§17.5), reserved whole so a later built-in cast can never be shadowed by an earlier registration.</summary>
    private static ReadOnlyMemory<byte> XsdNamespacePrefix { get; } = "http://www.w3.org/2001/XMLSchema#"u8.ToArray();

    /// <summary>The accepted entries so far, keyed by IRI, each carrying its registered faces.</summary>
    private Dictionary<Utf8String, SparqlFunctionEntry> Accepted { get; } = [];

    /// <summary>The recorded outcome of every registration attempt, in attempt order.</summary>
    private List<SparqlFunctionRegistration> RecordedOutcomes { get; } = [];

    /// <summary>The outcome of every registration attempt so far, accepted and declined alike, in attempt order.</summary>
    public IReadOnlyList<SparqlFunctionRegistration> Outcomes => RecordedOutcomes;

    /// <summary>Runs the acceptance rule for a scalar function and, on success, admits it to the pending set.</summary>
    /// <param name="functionIri">The IRI call expressions name the function by.</param>
    /// <param name="implementation">The scalar function implementation.</param>
    /// <returns>The typed registration outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="implementation"/> is <see langword="null"/>.</exception>
    public SparqlFunctionRegistration Add(Utf8String functionIri, SparqlFunctionDelegate implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);

        return Add(new SparqlFunctionEntry(functionIri, implementation));
    }

    /// <summary>Runs the acceptance rule for a catalog entry and, on success, admits it — its faces together — to the pending set.</summary>
    /// <param name="entry">The catalog entry to register.</param>
    /// <returns>The typed registration outcome.</returns>
    /// <exception cref="ArgumentNullException">The entry carries neither a scalar nor an aggregate implementation (a default entry).</exception>
    public SparqlFunctionRegistration Add(SparqlFunctionEntry entry)
    {
        //Faceless entries are a programming error, checked before any per-face admission so the
        //registration can never silently vanish.
        if(entry.Scalar is null && entry.Aggregate is null)
        {
            throw new ArgumentNullException(nameof(entry), "The entry carries neither a scalar nor an aggregate implementation.");
        }

        SparqlFunctionRegistration outcome = Decide(entry.FunctionIri);
        RecordedOutcomes.Add(outcome);
        if(outcome.Kind == SparqlFunctionRegistrationKind.Accepted)
        {
            Accepted[entry.FunctionIri] = entry;
        }

        return outcome;
    }

    /// <summary>Freezes the accepted functions into an immutable registry.</summary>
    /// <returns>The registry.</returns>
    public SparqlFunctionRegistry Build()
    {
        Dictionary<Utf8String, SparqlFunctionDelegate> scalars = [];
        Dictionary<Utf8String, SparqlAggregateDelegate> aggregates = [];
        foreach(KeyValuePair<Utf8String, SparqlFunctionEntry> entry in Accepted)
        {
            if(entry.Value.Scalar is { } scalar)
            {
                scalars[entry.Key] = scalar;
            }

            if(entry.Value.Aggregate is { } aggregate)
            {
                aggregates[entry.Key] = aggregate;
            }
        }

        return SparqlFunctionRegistry.FromEntries(scalars, aggregates);
    }

    /// <summary>Runs the acceptance rule for an IRI without mutating the pending set.</summary>
    /// <param name="functionIri">The IRI under the rule.</param>
    /// <returns>The typed registration outcome.</returns>
    private SparqlFunctionRegistration Decide(Utf8String functionIri)
    {
        if(functionIri.Span.IsEmpty)
        {
            return SparqlFunctionRegistration.RejectedEmptyIri(functionIri);
        }

        if(functionIri.Span.StartsWith(XsdNamespacePrefix.Span))
        {
            return SparqlFunctionRegistration.RejectedReservedIri(functionIri);
        }

        if(Accepted.ContainsKey(functionIri))
        {
            return SparqlFunctionRegistration.RejectedDuplicate(functionIri);
        }

        return SparqlFunctionRegistration.Accepted(functionIri);
    }
}
