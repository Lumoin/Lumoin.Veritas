using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>One registrant-supplied acceptance case: a probe over the sample corpus and the hits it must yield.</summary>
/// <remarks>
/// The expected hits are stated by the registrant against the sample corpus — the non-circular ground
/// truth of the acceptance self-test (derived outside the method under test, e.g. from an
/// independently computed reference table). Order is not asserted beyond the cursor's documented ascending-axis contract; the
/// self-test compares hit SETS.
/// </remarks>
/// <param name="Request">The probe request.</param>
/// <param name="ExpectedHits">The hits the probe must yield over the sample corpus.</param>
public sealed record ValueIndexSelfTestCase(ValueProbeRequest Request, IReadOnlyList<ValueProbeHit> ExpectedHits);

/// <summary>
/// One value-index registration: the access method, its declared axis, and the registrant-supplied
/// acceptance material the registry's self-test runs at composition time.
/// </summary>
/// <remarks>
/// Registration happens before the store has data, so the self-test runs over
/// <see cref="SampleCorpus"/> — a registrant-supplied <see cref="ValueSegmentSource"/> — and certifies
/// the method against ITS OWN declared sample; general answer identity is carried by the standing
/// certification battery, not by registration.
/// </remarks>
public sealed class ValueIndexRegistration
{
    /// <summary>Constructs a registration.</summary>
    /// <param name="method">The access method.</param>
    /// <param name="axis">The declared axis.</param>
    /// <param name="sampleCorpus">The sample corpus the acceptance self-test builds from.</param>
    /// <param name="selfTestCases">The acceptance cases the built sample index must answer.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public ValueIndexRegistration(ValueAccessMethod method, ValueAxisDeclaration axis, ValueSegmentSource sampleCorpus, IReadOnlyList<ValueIndexSelfTestCase> selfTestCases)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(sampleCorpus);
        ArgumentNullException.ThrowIfNull(selfTestCases);

        Method = method;
        Axis = axis;
        SampleCorpus = sampleCorpus;
        SelfTestCases = selfTestCases;
    }

    /// <summary>The access method.</summary>
    public ValueAccessMethod Method { get; }

    /// <summary>The declared axis.</summary>
    public ValueAxisDeclaration Axis { get; }

    /// <summary>The sample corpus the acceptance self-test builds from.</summary>
    public ValueSegmentSource SampleCorpus { get; }

    /// <summary>The acceptance cases the built sample index must answer.</summary>
    public IReadOnlyList<ValueIndexSelfTestCase> SelfTestCases { get; }
}
