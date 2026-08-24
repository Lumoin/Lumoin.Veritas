using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>The typed outcome kind of a value-datatype registration attempt.</summary>
public enum ValueDatatypeRegistrationKind
{
    /// <summary>The definition was accepted.</summary>
    Accepted,

    /// <summary>The IRI is reserved — in the XSD or RDF namespace, or modelled by the engine's own value-space classifier; built-in semantics are not overridable.</summary>
    [SuppressMessage("Naming", "CA1700:Do not name enum values 'Reserved'", Justification = "The member names the reservation-gate outcome — an IRI the engine reserves against registration — not a placeholder for future use.")]
    RejectedReservedIri,

    /// <summary>A definition for the same IRI was already registered.</summary>
    RejectedDuplicate,

    /// <summary>The definition declares no facet, so no consult could ever use it.</summary>
    RejectedFacetless,

    /// <summary>The definition declares more probes than <see cref="ValueDatatypeLaws.ProbeBudget"/>.</summary>
    RejectedProbeBudgetExceeded,

    /// <summary>The definition's <see cref="ValueDatatype.SameValue"/> provably violates an equality law over its own probes.</summary>
    RejectedLawViolation,
}

/// <summary>
/// The value-based outcome of a value-datatype registration attempt — never an exception, because a
/// declined registration is an expected operator-configuration condition. A law-driven rejection carries
/// the typed violation that caused it.
/// </summary>
/// <param name="Kind">The outcome kind.</param>
/// <param name="DatatypeIri">The IRI the attempt was for.</param>
/// <param name="Violation">The law violation, when the law check drove the rejection.</param>
public readonly record struct ValueDatatypeRegistration(ValueDatatypeRegistrationKind Kind, Utf8String DatatypeIri, ValueDatatypeLawViolation? Violation)
{
    /// <summary>An accepted outcome.</summary>
    /// <param name="datatypeIri">The registered IRI.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration Accepted(Utf8String datatypeIri)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.Accepted, datatypeIri, null);
    }

    /// <summary>A reserved-IRI rejection.</summary>
    /// <param name="datatypeIri">The reserved IRI.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration RejectedReservedIri(Utf8String datatypeIri)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.RejectedReservedIri, datatypeIri, null);
    }

    /// <summary>A duplicate-IRI rejection.</summary>
    /// <param name="datatypeIri">The IRI already registered.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration RejectedDuplicate(Utf8String datatypeIri)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.RejectedDuplicate, datatypeIri, null);
    }

    /// <summary>A facet-less rejection.</summary>
    /// <param name="datatypeIri">The rejected IRI.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration RejectedFacetless(Utf8String datatypeIri)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.RejectedFacetless, datatypeIri, null);
    }

    /// <summary>An over-budget probe-list rejection.</summary>
    /// <param name="datatypeIri">The rejected IRI.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration RejectedProbeBudgetExceeded(Utf8String datatypeIri)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.RejectedProbeBudgetExceeded, datatypeIri, null);
    }

    /// <summary>A law-violation rejection carrying the typed violation.</summary>
    /// <param name="datatypeIri">The rejected IRI.</param>
    /// <param name="violation">The violation the law check found.</param>
    /// <returns>The outcome.</returns>
    public static ValueDatatypeRegistration RejectedLawViolation(Utf8String datatypeIri, ValueDatatypeLawViolation violation)
    {
        return new ValueDatatypeRegistration(ValueDatatypeRegistrationKind.RejectedLawViolation, datatypeIri, violation);
    }
}

/// <summary>
/// An immutable, frozen set of registered value-layer datatypes keyed by IRI. Constructed only through
/// <see cref="ValueDatatypeRegistryBuilder"/>, which runs the reservation gate, the facet requirement, the
/// probe budget, and the bounded law check before a definition enters the set. <see cref="Empty"/> is the
/// composition default, and <see cref="IsEmpty"/> is a get-only bool set at construction so the no-op
/// posture costs one predicted branch to detect. <see cref="DatatypeIris"/> is the discovery face over the
/// frozen keys, so a host can enumerate what the composition registered without holding the definitions.
/// </summary>
public sealed class ValueDatatypeRegistry
{
    /// <summary>The registered definitions keyed by IRI.</summary>
    private FrozenDictionary<Utf8String, ValueDatatype> Entries { get; }

    /// <summary>Wraps a frozen definition set.</summary>
    /// <param name="entries">The registered definitions.</param>
    private ValueDatatypeRegistry(FrozenDictionary<Utf8String, ValueDatatype> entries)
    {
        Entries = entries;
        IsEmpty = entries.Count == 0;
    }

    /// <summary>Whether no datatype is registered — the fast path a consult tests first to skip the lookup entirely.</summary>
    public bool IsEmpty { get; }

    /// <summary>The registered value-datatype IRIs — the registry's discovery face, which a host enumerates to state what its composition admits. The order is the frozen set's own; a consumer needing a stable order sorts ordinally.</summary>
    public IReadOnlyCollection<Utf8String> DatatypeIris => Entries.Keys;

    /// <summary>The empty registry — the null object a host with no registered value datatypes uses.</summary>
    public static ValueDatatypeRegistry Empty { get; } = new(new Dictionary<Utf8String, ValueDatatype>().ToFrozenDictionary());

    /// <summary>Freezes a set of accepted definitions into a registry.</summary>
    /// <param name="entries">The accepted definitions keyed by IRI.</param>
    /// <returns>The frozen registry.</returns>
    internal static ValueDatatypeRegistry FromEntries(IReadOnlyDictionary<Utf8String, ValueDatatype> entries)
    {
        Dictionary<Utf8String, ValueDatatype> copy = new(entries.Count);
        foreach(KeyValuePair<Utf8String, ValueDatatype> entry in entries)
        {
            copy[entry.Key] = entry.Value;
        }

        return new ValueDatatypeRegistry(copy.ToFrozenDictionary());
    }

    /// <summary>Looks up the registered definition for an IRI.</summary>
    /// <param name="iri">The datatype IRI.</param>
    /// <param name="registered">The registered definition, when present.</param>
    /// <returns><see langword="true"/> when a definition is registered for the IRI.</returns>
    public bool TryGet(Utf8String iri, [MaybeNullWhen(false)] out ValueDatatype registered)
    {
        return Entries.TryGetValue(iri, out registered);
    }
}

/// <summary>
/// Builds a <see cref="ValueDatatypeRegistry"/>. Each <see cref="Add(ValueDatatype)"/> runs the acceptance
/// rule — reject a reserved IRI, reject a duplicate, reject a facet-less declaration, reject an over-budget
/// probe list, run the bounded law check — and returns a value-based
/// <see cref="ValueDatatypeRegistration"/> without ever throwing for those expected conditions. Every
/// outcome, declined ones included, also accumulates on <see cref="Outcomes"/> so a composition site can
/// inspect a bulk registration after the fact — a declined registration is never a silent drop.
/// </summary>
public sealed class ValueDatatypeRegistryBuilder
{
    /// <summary>The accepted definitions so far, keyed by IRI.</summary>
    private Dictionary<Utf8String, ValueDatatype> Accepted { get; } = [];

    /// <summary>The recorded outcome of every registration attempt, in attempt order.</summary>
    private List<ValueDatatypeRegistration> RecordedOutcomes { get; } = [];

    /// <summary>The outcome of every registration attempt so far, accepted and declined alike, in attempt order.</summary>
    public IReadOnlyList<ValueDatatypeRegistration> Outcomes => RecordedOutcomes;

    /// <summary>Runs the acceptance rule for a definition and, on success, admits it to the pending set.</summary>
    /// <param name="datatype">The definition to register.</param>
    /// <returns>The typed registration outcome.</returns>
    public ValueDatatypeRegistration Add(ValueDatatype datatype)
    {
        ArgumentNullException.ThrowIfNull(datatype);

        ValueDatatypeRegistration outcome = Decide(datatype);
        RecordedOutcomes.Add(outcome);
        if(outcome.Kind == ValueDatatypeRegistrationKind.Accepted)
        {
            Accepted[datatype.DatatypeIri] = datatype;
        }

        return outcome;
    }

    /// <summary>Freezes the accepted definitions into an immutable registry.</summary>
    /// <returns>The registry.</returns>
    public ValueDatatypeRegistry Build()
    {
        return ValueDatatypeRegistry.FromEntries(Accepted);
    }

    /// <summary>Runs the acceptance rule for a definition without mutating the pending set.</summary>
    /// <param name="datatype">The definition under the rule.</param>
    /// <returns>The typed registration outcome.</returns>
    private ValueDatatypeRegistration Decide(ValueDatatype datatype)
    {
        Utf8String iri = datatype.DatatypeIri;
        if(ValueDatatypeReservations.IsReserved(iri))
        {
            return ValueDatatypeRegistration.RejectedReservedIri(iri);
        }

        if(Accepted.ContainsKey(iri))
        {
            return ValueDatatypeRegistration.RejectedDuplicate(iri);
        }

        if(datatype.Facets == ValueDatatypeFacets.None)
        {
            return ValueDatatypeRegistration.RejectedFacetless(iri);
        }

        if(datatype.Probes.Count > ValueDatatypeLaws.ProbeBudget)
        {
            return ValueDatatypeRegistration.RejectedProbeBudgetExceeded(iri);
        }

        if(ValueDatatypeLaws.TryFindViolation(datatype, out ValueDatatypeLawViolation violation))
        {
            return ValueDatatypeRegistration.RejectedLawViolation(iri, violation);
        }

        return ValueDatatypeRegistration.Accepted(iri);
    }
}
