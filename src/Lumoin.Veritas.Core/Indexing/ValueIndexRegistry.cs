using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>The error a value-index registration raises when the acceptance ladder rejects it.</summary>
public sealed class ValueIndexRegistrationException: Exception
{
    /// <summary>Constructs the error with the rejection reason.</summary>
    /// <param name="message">The rejection reason.</param>
    public ValueIndexRegistrationException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs the error with no detail.</summary>
    public ValueIndexRegistrationException()
    {
    }

    /// <summary>Constructs the error with the rejection reason and a cause.</summary>
    /// <param name="message">The rejection reason.</param>
    /// <param name="innerException">The causing error.</param>
    public ValueIndexRegistrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The frozen set of accepted value-index registrations an engine composes with.
/// </summary>
/// <remarks>
/// <para>
/// Built once through <see cref="ValueIndexRegistryBuilder"/>, whose acceptance ladder every
/// registration passes at composition time: the duplicate check (one registration per
/// (datatype, axis) pair), the shape sanity check (the mandatory nearest-predecessor primitive;
/// interval overlap declared if and only if the axis is an interval pair), and the differential
/// self-test (the method builds the registrant's sample corpus and must answer every supplied case
/// exactly). A rejected registration throws <see cref="ValueIndexRegistrationException"/> — a
/// composition-time configuration invariant, never a query-time condition.
/// </para>
/// <para>
/// <see cref="Empty"/> is the process-wide no-registration singleton the default engine options
/// carry: composing with it adds zero per-engine allocation and zero query-path work.
/// </para>
/// </remarks>
public sealed class ValueIndexRegistry
{
    /// <summary>Constructs a frozen registry over accepted registrations. Called by the builder.</summary>
    /// <param name="registrations">The accepted registrations.</param>
    private ValueIndexRegistry(IReadOnlyList<ValueIndexRegistration> registrations)
    {
        Registrations = registrations;
    }

    /// <summary>The process-wide empty registry — the default composition, zero overhead.</summary>
    public static ValueIndexRegistry Empty { get; } = new([]);

    /// <summary>The accepted registrations, in registration order.</summary>
    public IReadOnlyList<ValueIndexRegistration> Registrations { get; }

    /// <summary>Whether the registry holds no registrations.</summary>
    public bool IsEmpty => Registrations.Count == 0;

    /// <summary>Finds the registration whose axis involves the given predicate IRI, or <see langword="null"/> when none does.</summary>
    /// <param name="predicateIri">The predicate IRI a probe or maintenance path routes by.</param>
    /// <returns>The registration, or <see langword="null"/>.</returns>
    public ValueIndexRegistration? FindByPredicate(Utf8String predicateIri)
    {
        for(int i = 0; i < Registrations.Count; i++)
        {
            ValueIndexRegistration registration = Registrations[i];
            if(registration.Axis.StartPredicateIri.Equals(predicateIri)
                || (registration.Axis.EndPredicateIri is Utf8String end && end.Equals(predicateIri)))
            {
                return registration;
            }
        }

        return null;
    }

    /// <summary>Builds a frozen registry from accepted registrations. Called by the builder after the acceptance ladder.</summary>
    /// <param name="registrations">The accepted registrations.</param>
    /// <returns>The frozen registry.</returns>
    internal static ValueIndexRegistry Freeze(IReadOnlyList<ValueIndexRegistration> registrations)
    {
        return registrations.Count == 0 ? Empty : new ValueIndexRegistry(registrations);
    }
}
