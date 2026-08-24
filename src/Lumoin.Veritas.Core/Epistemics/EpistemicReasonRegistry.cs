using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// The frozen set of accepted epistemic-reason registrations an engine composes with — the single
/// identity authority for reason codes on the epistemic surface.
/// </summary>
/// <remarks>
/// <para>
/// Built once through <see cref="EpistemicReasonRegistryBuilder"/>, whose acceptance ladder every
/// registration passes at composition time. A rejected registration throws
/// <see cref="EpistemicRegistrationException"/> at composition, never at query time.
/// </para>
/// <para>
/// Lookup is cold-path and Rust-translatable: a sorted <see cref="int"/> code array with a
/// parallel registration array, probed by <see cref="Array.BinarySearch{T}(T[], T)"/>. No
/// dictionary and no hashing are involved. <see cref="Registrations"/> preserves registration
/// order; the sorted arrays serve only the lookups.
/// </para>
/// <para>
/// <see cref="Empty"/> is the process-wide no-registration singleton the default engine options
/// carry: composing with it adds zero per-engine allocation and zero query-path work.
/// </para>
/// </remarks>
public sealed class EpistemicReasonRegistry
{
    /// <summary>The registration codes in ascending order; parallel to <see cref="byCode"/>.</summary>
    private readonly int[] sortedCodes;

    /// <summary>The registrations ordered by ascending code; parallel to <see cref="sortedCodes"/>.</summary>
    private readonly EpistemicReasonRegistration[] byCode;

    /// <summary>Constructs a frozen registry over accepted registrations. Called by the builder.</summary>
    /// <param name="registrations">The accepted registrations, in registration order.</param>
    private EpistemicReasonRegistry(IReadOnlyList<EpistemicReasonRegistration> registrations)
    {
        Registrations = registrations;

        int count = registrations.Count;
        int[] codes = new int[count];
        EpistemicReasonRegistration[] ordered = new EpistemicReasonRegistration[count];
        for(int i = 0; i < count; i++)
        {
            EpistemicReasonRegistration registration = registrations[i];
            codes[i] = registration.Code.Code;
            ordered[i] = registration;
        }

        Array.Sort(codes, ordered);
        sortedCodes = codes;
        byCode = ordered;
    }

    /// <summary>The process-wide empty registry — the default composition, zero overhead.</summary>
    public static EpistemicReasonRegistry Empty { get; } = new([]);

    /// <summary>The accepted registrations, in registration order.</summary>
    public IReadOnlyList<EpistemicReasonRegistration> Registrations { get; }

    /// <summary>Whether the registry holds no registrations.</summary>
    public bool IsEmpty => Registrations.Count == 0;

    /// <summary>Builds a frozen registry from accepted registrations. Called by the builder after the acceptance ladder.</summary>
    /// <param name="registrations">The accepted registrations, in registration order.</param>
    /// <returns>The frozen registry; <see cref="Empty"/> when nothing was accepted.</returns>
    internal static EpistemicReasonRegistry Freeze(IReadOnlyList<EpistemicReasonRegistration> registrations)
    {
        return registrations.Count == 0 ? Empty : new EpistemicReasonRegistry(registrations);
    }

    /// <summary>Finds the registration for a code.</summary>
    /// <param name="code">The code to resolve.</param>
    /// <param name="registration">The registration when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a registration for <paramref name="code"/> exists.</returns>
    public bool TryFind(EpistemicReasonCode code, [MaybeNullWhen(false)] out EpistemicReasonRegistration registration)
    {
        int index = Array.BinarySearch(sortedCodes, code.Code);
        if(index >= 0)
        {
            registration = byCode[index];

            return true;
        }

        registration = null;

        return false;
    }

    /// <summary>Resolves a code to its cold WHY explanation.</summary>
    /// <param name="code">The code to resolve.</param>
    /// <param name="explanation">The explanation bytes when found; otherwise the default.</param>
    /// <returns><see langword="true"/> when a registration for <paramref name="code"/> exists.</returns>
    public bool TryGetExplanation(EpistemicReasonCode code, out ReadOnlyMemory<byte> explanation)
    {
        int index = Array.BinarySearch(sortedCodes, code.Code);
        if(index >= 0)
        {
            explanation = byCode[index].Explanation;

            return true;
        }

        explanation = default;

        return false;
    }
}
