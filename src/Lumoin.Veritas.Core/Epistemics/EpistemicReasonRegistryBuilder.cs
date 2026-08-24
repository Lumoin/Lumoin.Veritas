using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// Accumulates epistemic-reason registrations and resolvable projection names, then freezes them
/// into an <see cref="EpistemicReasonRegistry"/> through the three-rung acceptance ladder.
/// </summary>
/// <remarks>
/// The ladder runs at <see cref="Build"/>. Rung 1 (duplicate/collision) rejects two registrations
/// sharing a code or a canonical name, and two different family names reserving one band index.
/// Rung 2 (shape sanity) requires a band index of at least 1, the code inside its own family band,
/// a non-empty family name, a non-empty canonical name, a non-empty explanation, and a
/// projection-coverage declaration that is present (deferred or declared, never the undeclared
/// default). Rung 3
/// (self-test) tentatively freezes the registry, confirms every registration resolves to itself
/// with a non-empty explanation, and confirms every declared projection name was added through
/// <see cref="AddProjection"/>. Any rung's failure throws <see cref="EpistemicRegistrationException"/>
/// naming the registration and the rung.
/// </remarks>
public sealed class EpistemicReasonRegistryBuilder
{
    /// <summary>The registrations accumulated so far, in registration order.</summary>
    private List<EpistemicReasonRegistration> Pending { get; } = [];

    /// <summary>The resolvable projection names added so far.</summary>
    private List<ReadOnlyMemory<byte>> Projections { get; } = [];

    /// <summary>Adds a registration to be accepted at <see cref="Build"/>.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">The registration is <see langword="null"/>.</exception>
    public EpistemicReasonRegistryBuilder Add(EpistemicReasonRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Pending.Add(registration);

        return this;
    }

    /// <summary>Registers a resolvable projection name a declared coverage may reference.</summary>
    /// <param name="projectionName">The projection name, as <c>u8</c> bytes.</param>
    /// <returns>This builder, for chaining.</returns>
    public EpistemicReasonRegistryBuilder AddProjection(ReadOnlyMemory<byte> projectionName)
    {
        Projections.Add(projectionName);

        return this;
    }

    /// <summary>Runs the acceptance ladder over every pending registration and freezes the registry.</summary>
    /// <returns>The frozen registry; <see cref="EpistemicReasonRegistry.Empty"/> when nothing was added.</returns>
    /// <exception cref="EpistemicRegistrationException">A registration failed a ladder rung.</exception>
    public EpistemicReasonRegistry Build()
    {
        for(int i = 0; i < Pending.Count; i++)
        {
            EpistemicReasonRegistration registration = Pending[i];
            CheckCollisions(registration, i);
            CheckShapeSanity(registration);
        }

        EpistemicReasonRegistry frozen = EpistemicReasonRegistry.Freeze([.. Pending]);
        RunSelfTest(frozen);

        return frozen;
    }

    /// <summary>Rung 1: no shared code, no shared canonical name, and no band reserved by two different family names.</summary>
    /// <param name="registration">The registration under acceptance.</param>
    /// <param name="index">Its position; only earlier registrations are compared, so each conflict reports once.</param>
    /// <exception cref="EpistemicRegistrationException">A collision exists with an earlier registration.</exception>
    private void CheckCollisions(EpistemicReasonRegistration registration, int index)
    {
        for(int i = 0; i < index; i++)
        {
            EpistemicReasonRegistration earlier = Pending[i];
            if(earlier.Code.Code == registration.Code.Code)
            {
                throw new EpistemicRegistrationException($"Rung 1 (duplicate/collision) rejected registration {Describe(registration)}: its code {registration.Code.Code} is already registered.");
            }

            if(earlier.CanonicalName.Span.SequenceEqual(registration.CanonicalName.Span))
            {
                throw new EpistemicRegistrationException($"Rung 1 (duplicate/collision) rejected registration {Describe(registration)}: its canonical name is already registered.");
            }

            if(earlier.Family.BandIndex == registration.Family.BandIndex
                && !earlier.Family.Name.Span.SequenceEqual(registration.Family.Name.Span))
            {
                throw new EpistemicRegistrationException($"Rung 1 (duplicate/collision) rejected registration {Describe(registration)}: band index {registration.Family.BandIndex} is already reserved by a different family name.");
            }
        }
    }

    /// <summary>Rung 2: a band index of at least 1, the code inside its own family band, a non-empty family name, a non-empty canonical name, a non-empty explanation, and a present coverage declaration.</summary>
    /// <param name="registration">The registration under acceptance.</param>
    /// <exception cref="EpistemicRegistrationException">A shape invariant is violated.</exception>
    private static void CheckShapeSanity(EpistemicReasonRegistration registration)
    {
        if(registration.Family.BandIndex < 1)
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration {Describe(registration)}: band index {registration.Family.BandIndex} is reserved-invalid (a valid band index is at least 1).");
        }

        if(!registration.Family.Contains(registration.Code))
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration {Describe(registration)}: its code {registration.Code.Code} falls outside its family band [{registration.Family.BlockStart}, {registration.Family.BlockInclusiveEnd}].");
        }

        if(registration.Family.Name.Length == 0)
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration {Describe(registration)}: its family name is empty (a band reservation names no family).");
        }

        if(registration.CanonicalName.Length == 0)
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration with code {registration.Code.Code}: its canonical name is empty.");
        }

        if(registration.Explanation.Length == 0)
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration {Describe(registration)}: its explanation is empty (a code without an explanation is invalid).");
        }

        if(registration.Coverage.IsUndeclared)
        {
            throw new EpistemicRegistrationException($"Rung 2 (shape sanity) rejected registration {Describe(registration)}: its projection coverage is undeclared (declare it deferred or declared, never the undeclared default).");
        }
    }

    /// <summary>Rung 3: every registration resolves to a non-empty explanation through the frozen registry, and every declared projection name was added.</summary>
    /// <param name="frozen">The tentatively frozen registry.</param>
    /// <exception cref="EpistemicRegistrationException">A registration does not resolve, or a declared projection name is unresolvable.</exception>
    private void RunSelfTest(EpistemicReasonRegistry frozen)
    {
        for(int i = 0; i < Pending.Count; i++)
        {
            EpistemicReasonRegistration registration = Pending[i];
            if(!frozen.TryFind(registration.Code, out EpistemicReasonRegistration? resolved) || !ReferenceEquals(resolved, registration))
            {
                throw new EpistemicRegistrationException($"Rung 3 (self-test) rejected registration {Describe(registration)}: its code {registration.Code.Code} did not resolve to it in the frozen registry.");
            }

            if(!frozen.TryGetExplanation(registration.Code, out ReadOnlyMemory<byte> explanation) || explanation.Length == 0)
            {
                throw new EpistemicRegistrationException($"Rung 3 (self-test) rejected registration {Describe(registration)}: its code {registration.Code.Code} did not resolve to a non-empty explanation.");
            }

            if(registration.Coverage.IsDeclared)
            {
                CheckProjectionsResolvable(registration);
            }
        }
    }

    /// <summary>Confirms every declared projection name of a registration matches a name added through <see cref="AddProjection"/>.</summary>
    /// <param name="registration">The registration whose declared projections are checked.</param>
    /// <exception cref="EpistemicRegistrationException">A declared projection name was not added.</exception>
    private void CheckProjectionsResolvable(EpistemicReasonRegistration registration)
    {
        IReadOnlyList<ReadOnlyMemory<byte>> declaredNames = registration.Coverage.ProjectionNames;
        for(int i = 0; i < declaredNames.Count; i++)
        {
            ReadOnlyMemory<byte> declaredName = declaredNames[i];
            bool resolved = false;
            for(int j = 0; j < Projections.Count; j++)
            {
                if(Projections[j].Span.SequenceEqual(declaredName.Span))
                {
                    resolved = true;

                    break;
                }
            }

            if(!resolved)
            {
                throw new EpistemicRegistrationException($"Rung 3 (self-test) rejected registration {Describe(registration)}: a declared projection name does not match any projection added through AddProjection.");
            }
        }
    }

    /// <summary>Renders a registration for a diagnostic message by canonical name and code.</summary>
    /// <param name="registration">The registration to name.</param>
    /// <returns>A human-readable identifier naming the canonical name (when present) and the code.</returns>
    private static string Describe(EpistemicReasonRegistration registration)
    {
        if(registration.CanonicalName.Length == 0)
        {
            return $"(code {registration.Code.Code})";
        }

        return $"'{System.Text.Encoding.UTF8.GetString(registration.CanonicalName.Span)}' (code {registration.Code.Code})";
    }
}
