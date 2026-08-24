using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>The equality law a registration-time probe check found violated.</summary>
public enum ValueDatatypeLaw
{
    /// <summary>A probe was decided distinct from itself.</summary>
    Reflexivity,

    /// <summary>Two probes were decided same in one operand order and distinct in the other.</summary>
    Symmetry,

    /// <summary>Two same-verdicts compose to a pair the definition decided distinct.</summary>
    Transitivity,
}

/// <summary>
/// The value-based description of one equality-law violation, carried on a
/// <see cref="ValueDatatypeRegistration"/> so a rejected definition is diagnosable. The indexes point into
/// the definition's <see cref="ValueDatatype.Probes"/>; an index beyond the violated law's arity is
/// <c>-1</c>.
/// </summary>
/// <param name="Law">The violated law.</param>
/// <param name="FirstProbeIndex">The first probe of the violating tuple.</param>
/// <param name="SecondProbeIndex">The second probe, for a symmetry or transitivity violation; otherwise <c>-1</c>.</param>
/// <param name="ThirdProbeIndex">The third probe, for a transitivity violation; otherwise <c>-1</c>.</param>
public readonly record struct ValueDatatypeLawViolation(ValueDatatypeLaw Law, int FirstProbeIndex, int SecondProbeIndex, int ThirdProbeIndex)
{
    /// <summary>A reflexivity violation at one probe.</summary>
    /// <param name="probeIndex">The probe decided distinct from itself.</param>
    /// <returns>The violation.</returns>
    public static ValueDatatypeLawViolation Reflexivity(int probeIndex)
    {
        return new ValueDatatypeLawViolation(ValueDatatypeLaw.Reflexivity, probeIndex, -1, -1);
    }

    /// <summary>A symmetry violation between two probes.</summary>
    /// <param name="firstProbeIndex">The first probe of the asymmetric pair.</param>
    /// <param name="secondProbeIndex">The second probe of the asymmetric pair.</param>
    /// <returns>The violation.</returns>
    public static ValueDatatypeLawViolation Symmetry(int firstProbeIndex, int secondProbeIndex)
    {
        return new ValueDatatypeLawViolation(ValueDatatypeLaw.Symmetry, firstProbeIndex, secondProbeIndex, -1);
    }

    /// <summary>A transitivity violation across three probes.</summary>
    /// <param name="firstProbeIndex">The probe same-linked to the second and decided distinct from the third.</param>
    /// <param name="secondProbeIndex">The middle probe same-linked to both ends.</param>
    /// <param name="thirdProbeIndex">The probe the composition was decided distinct from.</param>
    /// <returns>The violation.</returns>
    public static ValueDatatypeLawViolation Transitivity(int firstProbeIndex, int secondProbeIndex, int thirdProbeIndex)
    {
        return new ValueDatatypeLawViolation(ValueDatatypeLaw.Transitivity, firstProbeIndex, secondProbeIndex, thirdProbeIndex);
    }
}

/// <summary>
/// The bounded registration-time law check: over a definition's declared probes,
/// <see cref="ValueDatatype.SameValue"/> must behave as an equality — never provably non-reflexive,
/// non-symmetric, or non-transitive. Only decisive verdicts can violate; abstention is always sound. The
/// sweep is nested loops over at most <see cref="ProbeBudget"/> probes with every pairwise verdict
/// consulted once, so the whole check is hard-capped with no recursion.
/// </summary>
public static class ValueDatatypeLaws
{
    /// <summary>The hard cap on declared probes, bounding the law sweep's pairwise consultations and its transitivity triple loop.</summary>
    public const int ProbeBudget = 16;

    /// <summary>
    /// Finds the first provable equality-law violation over the definition's declared probes. The caller
    /// keeps the probe list within <see cref="ProbeBudget"/> — <see cref="ValueDatatypeRegistryBuilder"/>
    /// rejects an over-budget definition with a value-based outcome before consulting the laws.
    /// </summary>
    /// <param name="datatype">The definition under check.</param>
    /// <param name="violation">The first violation found, when one exists.</param>
    /// <returns><see langword="true"/> when a violation was found.</returns>
    /// <exception cref="ArgumentException">The definition declares more probes than <see cref="ProbeBudget"/>.</exception>
    public static bool TryFindViolation(ValueDatatype datatype, out ValueDatatypeLawViolation violation)
    {
        ArgumentNullException.ThrowIfNull(datatype);

        IReadOnlyList<Utf8String> probes = datatype.Probes;
        int count = probes.Count;
        if(count > ProbeBudget)
        {
            throw new ArgumentException($"The definition declares {count} probes; the law-check budget is {ProbeBudget}.", nameof(datatype));
        }

        //Every pairwise verdict is consulted exactly once into a flat matrix, so the triple loop below
        //reads memory instead of re-asking the definition.
        ValueIdentity[] verdicts = new ValueIdentity[count * count];
        for(int i = 0; i < count; i++)
        {
            for(int j = 0; j < count; j++)
            {
                verdicts[(i * count) + j] = datatype.SameValue(probes[i], probes[j]);
            }
        }

        for(int i = 0; i < count; i++)
        {
            if(verdicts[(i * count) + i] == ValueIdentity.Distinct)
            {
                violation = ValueDatatypeLawViolation.Reflexivity(i);

                return true;
            }
        }

        for(int i = 0; i < count; i++)
        {
            for(int j = i + 1; j < count; j++)
            {
                ValueIdentity forward = verdicts[(i * count) + j];
                ValueIdentity backward = verdicts[(j * count) + i];
                if((forward == ValueIdentity.Same && backward == ValueIdentity.Distinct)
                    || (forward == ValueIdentity.Distinct && backward == ValueIdentity.Same))
                {
                    violation = ValueDatatypeLawViolation.Symmetry(i, j);

                    return true;
                }
            }
        }

        for(int i = 0; i < count; i++)
        {
            for(int j = 0; j < count; j++)
            {
                if(verdicts[(i * count) + j] != ValueIdentity.Same)
                {
                    continue;
                }

                for(int k = 0; k < count; k++)
                {
                    if(verdicts[(j * count) + k] == ValueIdentity.Same && verdicts[(i * count) + k] == ValueIdentity.Distinct)
                    {
                        violation = ValueDatatypeLawViolation.Transitivity(i, j, k);

                        return true;
                    }
                }
            }
        }

        violation = default;

        return false;
    }
}
