using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// Accumulates value-index registrations and freezes them into a <see cref="ValueIndexRegistry"/>
/// through the acceptance ladder.
/// </summary>
/// <remarks>
/// The ladder runs per registration at <see cref="Build"/>, in order: the duplicate check (one
/// registration per (datatype, axis) pair and no predicate shared between axes), the shape sanity
/// check (<see cref="ValueIndexShapes.NearestPredecessor"/> is mandatory;
/// <see cref="ValueIndexShapes.IntervalOverlap"/> is declared if and only if the axis is an interval
/// pair), and the differential self-test — the method builds the registrant's sample corpus and every
/// supplied case's probe must yield exactly the expected hit set. Any rung's failure throws
/// <see cref="ValueIndexRegistrationException"/> naming the registration and the rung.
/// </remarks>
public sealed class ValueIndexRegistryBuilder
{
    /// <summary>The registrations accumulated so far, in registration order.</summary>
    private List<ValueIndexRegistration> Pending { get; } = [];

    /// <summary>Adds a registration to be accepted at <see cref="Build"/>.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">The registration is <see langword="null"/>.</exception>
    public ValueIndexRegistryBuilder Add(ValueIndexRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Pending.Add(registration);

        return this;
    }

    /// <summary>Runs the acceptance ladder over every pending registration and freezes the registry.</summary>
    /// <returns>The frozen registry; <see cref="ValueIndexRegistry.Empty"/> when nothing was added.</returns>
    /// <exception cref="ValueIndexRegistrationException">A registration failed a ladder rung.</exception>
    public ValueIndexRegistry Build()
    {
        for(int i = 0; i < Pending.Count; i++)
        {
            ValueIndexRegistration registration = Pending[i];
            CheckDuplicates(registration, i);
            CheckShapeSanity(registration);
            RunSelfTest(registration);
        }

        return ValueIndexRegistry.Freeze([.. Pending]);
    }

    /// <summary>The duplicate rung: no two registrations may share a (datatype, axis) pair or a declared predicate.</summary>
    /// <param name="registration">The registration under acceptance.</param>
    /// <param name="index">Its position; only earlier registrations are compared, so each conflict reports once.</param>
    /// <exception cref="ValueIndexRegistrationException">A conflict exists.</exception>
    private void CheckDuplicates(ValueIndexRegistration registration, int index)
    {
        for(int i = 0; i < index; i++)
        {
            ValueIndexRegistration earlier = Pending[i];
            if(earlier.Method.DatatypeIri.Equals(registration.Method.DatatypeIri) && earlier.Axis == registration.Axis)
            {
                throw new ValueIndexRegistrationException($"Duplicate value-index registration: datatype {registration.Method.DatatypeIri} with the same axis is already registered.");
            }

            if(SharesPredicate(earlier.Axis, registration.Axis))
            {
                throw new ValueIndexRegistrationException($"Conflicting value-index registration: a declared predicate of datatype {registration.Method.DatatypeIri} is already claimed by an earlier registration.");
            }
        }
    }

    /// <summary>Whether two axis declarations claim any common predicate.</summary>
    /// <param name="left">The first axis.</param>
    /// <param name="right">The second axis.</param>
    /// <returns><see langword="true"/> when a predicate is shared.</returns>
    private static bool SharesPredicate(ValueAxisDeclaration left, ValueAxisDeclaration right)
    {
        bool startClash = left.StartPredicateIri.Equals(right.StartPredicateIri)
            || (right.EndPredicateIri is not null && left.StartPredicateIri.Equals(right.EndPredicateIri.Value));
        bool endClash = left.EndPredicateIri is not null
            && (left.EndPredicateIri.Value.Equals(right.StartPredicateIri)
                || (right.EndPredicateIri is not null && left.EndPredicateIri.Value.Equals(right.EndPredicateIri.Value)));

        return startClash || endClash;
    }

    /// <summary>The shape-sanity rung: the mandatory predecessor primitive, and overlap if and only if the axis is an interval pair.</summary>
    /// <param name="registration">The registration under acceptance.</param>
    /// <exception cref="ValueIndexRegistrationException">The declared shapes are inconsistent with the axis.</exception>
    private static void CheckShapeSanity(ValueIndexRegistration registration)
    {
        ValueIndexShapes shapes = registration.Method.DeclaredShapes;
        if((shapes & ValueIndexShapes.NearestPredecessor) == 0)
        {
            throw new ValueIndexRegistrationException($"Value-index registration for datatype {registration.Method.DatatypeIri} does not declare the mandatory nearest-predecessor primitive.");
        }

        bool declaresOverlap = (shapes & ValueIndexShapes.IntervalOverlap) != 0;
        if(declaresOverlap != registration.Axis.IsIntervalPair)
        {
            throw new ValueIndexRegistrationException($"Value-index registration for datatype {registration.Method.DatatypeIri} declares shapes inconsistent with its axis: interval overlap requires (and is required by) an interval-pair axis.");
        }
    }

    /// <summary>The self-test rung: the method builds the registrant's sample corpus and must answer every supplied case with exactly the expected hit set.</summary>
    /// <param name="registration">The registration under acceptance.</param>
    /// <exception cref="ValueIndexRegistrationException">The build declined or a case's hits diverged.</exception>
    private static void RunSelfTest(ValueIndexRegistration registration)
    {
        ValueIndexBuildOutcome outcome = registration.Method.Build(registration.SampleCorpus);
        if(outcome != ValueIndexBuildOutcome.Built)
        {
            throw new ValueIndexRegistrationException($"Value-index registration for datatype {registration.Method.DatatypeIri} failed acceptance: the sample-corpus build declined.");
        }

        for(int caseIndex = 0; caseIndex < registration.SelfTestCases.Count; caseIndex++)
        {
            ValueIndexSelfTestCase testCase = registration.SelfTestCases[caseIndex];
            HashSet<ValueProbeHit> expected = new(testCase.ExpectedHits);
            HashSet<ValueProbeHit> observed = [];
            ValueProbeRequest request = testCase.Request;
            using(ValueProbeCursor cursor = registration.Method.OpenProbe(in request))
            {
                while(cursor.TryAdvance(out ValueProbeHit hit))
                {
                    observed.Add(hit);
                }
            }

            if(!expected.SetEquals(observed))
            {
                throw new ValueIndexRegistrationException($"Value-index registration for datatype {registration.Method.DatatypeIri} failed acceptance: self-test case {caseIndex} yielded {observed.Count} hits where {expected.Count} were expected (set difference non-empty).");
            }
        }
    }
}
