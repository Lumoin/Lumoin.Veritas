using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>The typed outcome kind of a registration attempt.</summary>
public enum RegistrationOutcomeKind
{
    /// <summary>The definition was accepted.</summary>
    Accepted,

    /// <summary>The definition failed admissibility or the registration self-test.</summary>
    RejectedNotAdmissible,

    /// <summary>A definition for the same IRI was already registered.</summary>
    RejectedDuplicate,

    /// <summary>The IRI is a built-in datatype the family classifier already decides; built-ins are not overridable.</summary>
    RejectedBuiltInIri,
}

/// <summary>The typed budget breach that drove an admissibility rejection.</summary>
/// <param name="Budget">The breached budget axis.</param>
/// <param name="Limit">The configured ceiling for that axis.</param>
/// <param name="Actual">The smallest state count known to exceed the ceiling; the module surfaces only the fact of breach, so this is the ceiling plus one.</param>
public readonly record struct AutomatonBudgetBreach(AutomatonBudgetKind Budget, int Limit, int Actual);

/// <summary>
/// The value-based outcome of a registration attempt — never an exception, because a rejected
/// registration is an expected operator-configuration condition. A budget-driven admissibility rejection
/// carries the typed breach that caused it.
/// </summary>
/// <param name="Kind">The outcome kind.</param>
/// <param name="DatatypeIri">The IRI the attempt was for.</param>
/// <param name="Breach">The budget breach, when a budget drove an admissibility rejection.</param>
public readonly record struct RegistrationOutcome(RegistrationOutcomeKind Kind, Utf8String DatatypeIri, AutomatonBudgetBreach? Breach)
{
    /// <summary>An accepted outcome.</summary>
    /// <param name="datatypeIri">The registered IRI.</param>
    /// <returns>The outcome.</returns>
    public static RegistrationOutcome Accepted(Utf8String datatypeIri)
    {
        return new RegistrationOutcome(RegistrationOutcomeKind.Accepted, datatypeIri, null);
    }

    /// <summary>A duplicate-IRI rejection.</summary>
    /// <param name="datatypeIri">The IRI already registered.</param>
    /// <returns>The outcome.</returns>
    public static RegistrationOutcome RejectedDuplicate(Utf8String datatypeIri)
    {
        return new RegistrationOutcome(RegistrationOutcomeKind.RejectedDuplicate, datatypeIri, null);
    }

    /// <summary>A built-in-IRI rejection.</summary>
    /// <param name="datatypeIri">The built-in IRI.</param>
    /// <returns>The outcome.</returns>
    public static RegistrationOutcome RejectedBuiltInIri(Utf8String datatypeIri)
    {
        return new RegistrationOutcome(RegistrationOutcomeKind.RejectedBuiltInIri, datatypeIri, null);
    }

    /// <summary>An admissibility rejection, optionally carrying the budget breach that drove it.</summary>
    /// <param name="datatypeIri">The rejected IRI.</param>
    /// <param name="breach">The budget breach, or <see langword="null"/> for a structural rejection.</param>
    /// <returns>The outcome.</returns>
    public static RegistrationOutcome RejectedNotAdmissible(Utf8String datatypeIri, AutomatonBudgetBreach? breach)
    {
        return new RegistrationOutcome(RegistrationOutcomeKind.RejectedNotAdmissible, datatypeIri, breach);
    }
}

/// <summary>
/// An immutable, frozen set of registered datatypes keyed by IRI, consulted by the checker where the
/// family classifier abstains. Constructed only through <see cref="DatatypeRegistryBuilder"/>, which
/// runs admissibility and the registration self-test before a definition enters the set.
/// </summary>
public sealed class DatatypeRegistry
{
    /// <summary>The registered definitions keyed by IRI.</summary>
    private FrozenDictionary<Utf8String, RegisteredDatatype> Entries { get; }

    /// <summary>Wraps a frozen definition set.</summary>
    /// <param name="entries">The registered definitions.</param>
    private DatatypeRegistry(FrozenDictionary<Utf8String, RegisteredDatatype> entries)
    {
        Entries = entries;
        IsEmpty = entries.Count == 0;
    }

    /// <summary>Whether no datatype is registered — the fast path a consult site checks first to skip the registry walk and its allocations entirely.</summary>
    public bool IsEmpty { get; }

    /// <summary>The empty registry — the null object a host with no registered datatypes uses.</summary>
    public static DatatypeRegistry Empty { get; } = new(new Dictionary<Utf8String, RegisteredDatatype>().ToFrozenDictionary());

    /// <summary>Freezes a set of accepted definitions into a registry.</summary>
    /// <param name="entries">The accepted definitions keyed by IRI.</param>
    /// <returns>The frozen registry.</returns>
    internal static DatatypeRegistry FromEntries(IReadOnlyDictionary<Utf8String, RegisteredDatatype> entries)
    {
        Dictionary<Utf8String, RegisteredDatatype> copy = new(entries.Count);
        foreach(KeyValuePair<Utf8String, RegisteredDatatype> entry in entries)
        {
            copy[entry.Key] = entry.Value;
        }

        return new DatatypeRegistry(copy.ToFrozenDictionary());
    }

    /// <summary>Looks up the registered definition for an IRI.</summary>
    /// <param name="iri">The datatype IRI.</param>
    /// <param name="registered">The registered definition, when present.</param>
    /// <returns><see langword="true"/> when a definition is registered for the IRI.</returns>
    public bool TryGet(Utf8String iri, [MaybeNullWhen(false)] out RegisteredDatatype registered)
    {
        return Entries.TryGetValue(iri, out registered);
    }
}

/// <summary>
/// Builds a <see cref="DatatypeRegistry"/>. Each <see cref="Add(RegisteredDatatype)"/> runs the born-typed
/// acceptance rule — reject a built-in IRI, reject a duplicate, run admissibility, run the registration
/// self-test — and returns a value-based <see cref="RegistrationOutcome"/> without ever throwing for those
/// expected conditions. The automaton state ceilings the admissibility check runs under are injectable, so
/// a caller can prove a definition's determinization stays within a tighter budget.
/// </summary>
public sealed class DatatypeRegistryBuilder
{
    /// <summary>The automaton state ceilings admissibility runs under.</summary>
    private AutomatonBudgets Budgets { get; }

    /// <summary>The accepted definitions so far, keyed by IRI.</summary>
    private Dictionary<Utf8String, RegisteredDatatype> Accepted { get; } = [];

    /// <summary>Creates a builder over the given automaton budgets, defaulting to the shared defaults.</summary>
    /// <param name="budgets">The automaton state ceilings, or <see langword="null"/> for <see cref="AutomatonBudgets.Default"/>.</param>
    public DatatypeRegistryBuilder(AutomatonBudgets? budgets = null)
    {
        Budgets = budgets ?? AutomatonBudgets.Default;
    }

    /// <summary>
    /// Runs the acceptance rule for a definition and, on success, admits it to the pending set.
    /// </summary>
    /// <param name="datatype">The definition to register.</param>
    /// <returns>The typed registration outcome.</returns>
    public RegistrationOutcome Add(RegisteredDatatype datatype)
    {
        ArgumentNullException.ThrowIfNull(datatype);

        Utf8String iri = datatype.DatatypeIri;
        if(OwlDatatypeFamilies.Classify(iri) != OwlDatatypeFamily.Unknown)
        {
            return RegistrationOutcome.RejectedBuiltInIri(iri);
        }

        if(Accepted.ContainsKey(iri))
        {
            return RegistrationOutcome.RejectedDuplicate(iri);
        }

        AdmissibilityResult admissibility = datatype.CheckAdmissibility(Budgets);
        if(!admissibility.Admissible)
        {
            return RegistrationOutcome.RejectedNotAdmissible(iri, admissibility.Breach);
        }

        if(!datatype.RunSelfTest(Budgets))
        {
            return RegistrationOutcome.RejectedNotAdmissible(iri, null);
        }

        Accepted[iri] = datatype;

        return RegistrationOutcome.Accepted(iri);
    }

    /// <summary>Freezes the accepted definitions into an immutable registry.</summary>
    /// <returns>The registry.</returns>
    public DatatypeRegistry Build()
    {
        return DatatypeRegistry.FromEntries(Accepted);
    }
}
