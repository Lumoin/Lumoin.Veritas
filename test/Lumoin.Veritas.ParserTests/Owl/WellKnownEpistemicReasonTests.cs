using System;
using Lumoin.Veritas.Core.Epistemics;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The first-party selection-reason class family on the epistemic surface: the total mapping of every
/// <see cref="ReasoningSelectionReason"/> onto its band-1 code, the pinned enum underlying values, the
/// canonical names byte-equal to the enum member names, and the six registrations passing the whole
/// <see cref="EpistemicReasonRegistryBuilder"/> ladder with deferred coverage and queryable explanations.
/// </summary>
[TestClass]
internal sealed class WellKnownEpistemicReasonTests
{
    /// <summary>Every selection reason maps to a band-1 code whose int is the band base plus the enum's underlying value.</summary>
    [TestMethod]
    public void EverySelectionReasonMapsToItsBandOneCode()
    {
        foreach(ReasoningSelectionReason reason in Enum.GetValues<ReasoningSelectionReason>())
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.SelectionReason.ForSelectionReason(reason);
            Assert.AreEqual(1, code.ClassBand, "Every selection-reason code sits in the band-1 class family.");
            Assert.AreEqual(10000 + (int)reason, code.Code, "The code is the band base plus the enum's underlying value.");
        }
    }

    /// <summary>An undefined selection-reason value trips the invariant-violation arm of <see cref="WellKnownEpistemicReasons.SelectionReason.ForSelectionReason"/>.</summary>
    [TestMethod]
    public void UndefinedSelectionReasonThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WellKnownEpistemicReasons.SelectionReason.ForSelectionReason((ReasoningSelectionReason)99));
    }

    /// <summary>The selection-reason enum's underlying values are pinned to the contiguous range 0..5.</summary>
    [TestMethod]
    public void SelectionReasonUnderlyingValuesArePinnedZeroToFive()
    {
        ReasoningSelectionReason[] values = Enum.GetValues<ReasoningSelectionReason>();
        ReasoningSelectionReason[] pinned =
        [
            ReasoningSelectionReason.RdfsSufficient,
            ReasoningSelectionReason.RlSufficient,
            ReasoningSelectionReason.BeyondRlDelegated,
            ReasoningSelectionReason.BeyondRlReported,
            ReasoningSelectionReason.ElClassificationBuilt,
            ReasoningSelectionReason.ElClassificationReused,
        ];

        Assert.HasCount(pinned.Length, values);
        for(int i = 0; i < pinned.Length; i++)
        {
            Assert.AreEqual(i, (int)values[i], $"The selection-reason value at position {i} is pinned to underlying value {i}.");
            Assert.AreEqual(pinned[i], values[i], $"The selection-reason member at underlying value {i} is pinned.");
        }
    }

    /// <summary>Each registered canonical name byte-equals its <see cref="ReasoningSelectionReason"/> member name.</summary>
    [TestMethod]
    public void CanonicalNamesByteEqualTheEnumMemberNames()
    {
        EpistemicReasonRegistry registry = BuildSelectionRegistry();

        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.RdfsSufficient, "RdfsSufficient"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.RlSufficient, "RlSufficient"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.BeyondRlDelegated, "BeyondRlDelegated"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.BeyondRlReported, "BeyondRlReported"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.ElClassificationBuilt, "ElClassificationBuilt"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.SelectionReason.ElClassificationReused, "ElClassificationReused"u8);
    }

    /// <summary>The six selection-reason registrations pass the full three-rung acceptance ladder.</summary>
    [TestMethod]
    public void SelectionReasonRegistrationsPassTheFullLadder()
    {
        EpistemicReasonRegistry registry = BuildSelectionRegistry();

        Assert.IsFalse(registry.IsEmpty);
        Assert.HasCount(6, registry.Registrations);
    }

    /// <summary>Every registered code is explanation-queryable with non-empty WHY-text.</summary>
    [TestMethod]
    public void EveryRegisteredCodeIsExplanationQueryable()
    {
        EpistemicReasonRegistry registry = BuildSelectionRegistry();

        foreach(EpistemicReasonRegistration registration in registry.Registrations)
        {
            Assert.IsTrue(registry.TryGetExplanation(registration.Code, out ReadOnlyMemory<byte> explanation), "Every registered code resolves an explanation.");
            Assert.IsGreaterThan(0, explanation.Length, "The explanation text is non-empty.");
        }
    }

    /// <summary>Every selection-reason registration declares deferred projection coverage.</summary>
    [TestMethod]
    public void EveryRegistrationCoverageIsDeferred()
    {
        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.SelectionReason.CreateSelectionReasonRegistrations())
        {
            Assert.IsTrue(registration.Coverage.IsDeferred, "The selection-reason codes are registration identities only, coverage deferred.");
        }
    }

    /// <summary>The band-2 derivation-origin codes are pinned to 20000/20001 and both recover class band 2.</summary>
    [TestMethod]
    public void DerivationOriginCodesSitInBandTwo()
    {
        Assert.AreEqual(20000, WellKnownEpistemicReasons.DerivationOriginKind.DecidedUnderNoChoice.Code, "DecidedUnderNoChoice is pinned to the band-2 base.");
        Assert.AreEqual(20001, WellKnownEpistemicReasons.DerivationOriginKind.DerivedUnderChoice.Code, "DerivedUnderChoice is pinned to the band-2 base plus one.");
        Assert.AreEqual(2, WellKnownEpistemicReasons.DerivationOriginKind.DecidedUnderNoChoice.ClassBand, "DecidedUnderNoChoice recovers class band 2.");
        Assert.AreEqual(2, WellKnownEpistemicReasons.DerivationOriginKind.DerivedUnderChoice.ClassBand, "DerivedUnderChoice recovers class band 2.");
    }

    /// <summary>The band-3 conditionality-loss code is pinned to 30000 and recovers class band 3.</summary>
    [TestMethod]
    public void ConditionalityDroppedCodeSitsInBandThree()
    {
        Assert.AreEqual(30000, WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped.Code, "ConditionalityDropped is pinned to the band-3 base.");
        Assert.AreEqual(3, WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped.ClassBand, "ConditionalityDropped recovers class band 3.");
    }

    /// <summary>The pilot band-1 family plus the two minted families compose into one builder that freezes clean over all nine codes, each of which resolves — the append-only bands 2/3 do not collide with band 1 or each other.</summary>
    [TestMethod]
    public void AllFirstPartyFamiliesComposeCleanInOneBuilder()
    {
        EpistemicReasonRegistry registry = BuildAllFirstPartyRegistry();

        Assert.HasCount(9, registry.Registrations);

        EpistemicReasonCode[] codes =
        [
            WellKnownEpistemicReasons.SelectionReason.RdfsSufficient,
            WellKnownEpistemicReasons.SelectionReason.RlSufficient,
            WellKnownEpistemicReasons.SelectionReason.BeyondRlDelegated,
            WellKnownEpistemicReasons.SelectionReason.BeyondRlReported,
            WellKnownEpistemicReasons.SelectionReason.ElClassificationBuilt,
            WellKnownEpistemicReasons.SelectionReason.ElClassificationReused,
            WellKnownEpistemicReasons.DerivationOriginKind.DecidedUnderNoChoice,
            WellKnownEpistemicReasons.DerivationOriginKind.DerivedUnderChoice,
            WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped,
        ];

        foreach(EpistemicReasonCode code in codes)
        {
            Assert.IsTrue(registry.TryFind(code, out EpistemicReasonRegistration? registration), "Every composed code resolves.");
            Assert.IsNotNull(registration);
        }
    }

    /// <summary>Each minted-family registration is explanation-queryable with non-empty WHY-text and declares deferred projection coverage — the identities-only discipline for bands 2 and 3.</summary>
    [TestMethod]
    public void NewFamilyRegistrationsAreExplanationQueryableAndDeferred()
    {
        EpistemicReasonRegistry registry = BuildAllFirstPartyRegistry();

        EpistemicReasonCode[] newCodes =
        [
            WellKnownEpistemicReasons.DerivationOriginKind.DecidedUnderNoChoice,
            WellKnownEpistemicReasons.DerivationOriginKind.DerivedUnderChoice,
            WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped,
        ];

        foreach(EpistemicReasonCode code in newCodes)
        {
            Assert.IsTrue(registry.TryGetExplanation(code, out ReadOnlyMemory<byte> explanation), "Every minted code resolves an explanation.");
            Assert.IsGreaterThan(0, explanation.Length, "The minted explanation text is non-empty.");
        }

        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.DerivationOriginKind.CreateDerivationOriginRegistrations())
        {
            Assert.IsTrue(registration.Coverage.IsDeferred, "The derivation-origin codes are registration identities only, coverage deferred.");
        }

        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.ConditionalityLossLint.CreateConditionalityLossRegistrations())
        {
            Assert.IsTrue(registration.Coverage.IsDeferred, "The conditionality-loss code is a registration identity only, coverage deferred.");
        }
    }

    /// <summary>Each minted family's canonical name byte-equals its code's member name.</summary>
    [TestMethod]
    public void NewFamilyCanonicalNamesByteEqualTheirCodeNames()
    {
        EpistemicReasonRegistry registry = BuildAllFirstPartyRegistry();

        AssertCanonicalName(registry, WellKnownEpistemicReasons.DerivationOriginKind.DecidedUnderNoChoice, "DecidedUnderNoChoice"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.DerivationOriginKind.DerivedUnderChoice, "DerivedUnderChoice"u8);
        AssertCanonicalName(registry, WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped, "ConditionalityDropped"u8);
    }

    /// <summary>Builds the frozen registry over the six selection-reason registrations.</summary>
    /// <returns>The frozen registry.</returns>
    private static EpistemicReasonRegistry BuildSelectionRegistry()
    {
        EpistemicReasonRegistryBuilder builder = new();
        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.SelectionReason.CreateSelectionReasonRegistrations())
        {
            builder.Add(registration);
        }

        return builder.Build();
    }

    /// <summary>Builds the frozen registry over all nine first-party registrations: the six selection reasons, the two derivation-origin codes, and the single conditionality-loss code.</summary>
    /// <returns>The frozen registry.</returns>
    private static EpistemicReasonRegistry BuildAllFirstPartyRegistry()
    {
        EpistemicReasonRegistryBuilder builder = new();
        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.SelectionReason.CreateSelectionReasonRegistrations())
        {
            builder.Add(registration);
        }

        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.DerivationOriginKind.CreateDerivationOriginRegistrations())
        {
            builder.Add(registration);
        }

        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.ConditionalityLossLint.CreateConditionalityLossRegistrations())
        {
            builder.Add(registration);
        }

        return builder.Build();
    }

    /// <summary>Asserts the registry resolves the code to a registration whose canonical name byte-equals the expected bytes.</summary>
    /// <param name="registry">The registry to resolve through.</param>
    /// <param name="code">The code to resolve.</param>
    /// <param name="expected">The expected canonical name as <c>u8</c> bytes.</param>
    private static void AssertCanonicalName(EpistemicReasonRegistry registry, EpistemicReasonCode code, ReadOnlySpan<byte> expected)
    {
        Assert.IsTrue(registry.TryFind(code, out EpistemicReasonRegistration? registration), "The code resolves to its registration.");
        Assert.IsNotNull(registration);
        Assert.IsTrue(registration.CanonicalName.Span.SequenceEqual(expected), "The canonical name byte-equals the enum member name.");
    }
}
