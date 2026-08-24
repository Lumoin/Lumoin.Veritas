using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Lumoin.Veritas.Core.Epistemics;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The first-party entailment-rule class family on the epistemic surface: the total mapping of every
/// <see cref="EntailmentRules"/> wire string onto a distinct band-4 code, the canonical names byte-equal to
/// the pinned rule strings, and the one hundred eight registrations passing the whole
/// <see cref="EpistemicReasonRegistryBuilder"/> ladder with deferred coverage and queryable explanations —
/// composing beside the three earlier families without collision.
/// </summary>
[TestClass]
internal sealed class EntailmentRuleEpistemicReasonTests
{
    /// <summary>Every reflected entailment-rule string maps to a distinct band-4 code, so adding a rule forces minting a code.</summary>
    [TestMethod]
    public void EveryEntailmentRuleMapsToItsBandFourCode()
    {
        IReadOnlyList<string> wireStrings = AllEntailmentRuleWireStrings;
        Assert.HasCount(109, wireStrings, "The reflected entailment-rule catalog holds one hundred nine wire strings.");

        HashSet<int> codes = [];
        foreach(string wire in wireStrings)
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.EntailmentRule.ForRule(wire);
            Assert.AreEqual(4, code.ClassBand, "Every entailment-rule code sits in the band-4 class family.");
            Assert.IsTrue(codes.Add(code.Code), "Every entailment-rule string maps to a distinct code.");
        }

        Assert.HasCount(109, codes, "The one hundred nine rules map to one hundred nine distinct codes.");
    }

    /// <summary>The entailment-rule codes are exactly the contiguous band-4 range 40000..40108, with no gaps or duplicates.</summary>
    [TestMethod]
    public void EntailmentRuleCodesAreContiguousFortyThousandBand()
    {
        HashSet<int> codes = [];
        foreach(string wire in AllEntailmentRuleWireStrings)
        {
            codes.Add(WellKnownEpistemicReasons.EntailmentRule.ForRule(wire).Code);
        }

        Assert.HasCount(109, codes, "There are one hundred nine distinct codes.");
        for(int expected = 40000; expected <= 40108; expected++)
        {
            Assert.Contains(expected, codes, $"The contiguous band-4 code {expected} is minted.");
        }
    }

    /// <summary>Each registered canonical name byte-equals its <see cref="EntailmentRules"/> string value — the parity binding the two source-of-truths.</summary>
    [TestMethod]
    public void EntailmentRuleCanonicalNamesByteEqualTheRuleStrings()
    {
        EpistemicReasonRegistry registry = BuildEntailmentRuleRegistry();

        foreach(string wire in AllEntailmentRuleWireStrings)
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.EntailmentRule.ForRule(wire);
            Assert.IsTrue(registry.TryFind(code, out EpistemicReasonRegistration? registration), "The rule's code resolves to its registration.");
            Assert.IsNotNull(registration);

            byte[] expected = Encoding.UTF8.GetBytes(wire);
            Assert.IsTrue(registration.CanonicalName.Span.SequenceEqual(expected), $"The canonical name byte-equals the '{wire}' rule string.");
        }
    }

    /// <summary>The rule-to-code mapping is a bijection: every wire string forward-resolves and every registration's canonical name maps back to its own code.</summary>
    [TestMethod]
    public void EntailmentRuleMappingIsTotalBothDirections()
    {
        EpistemicReasonRegistry registry = BuildEntailmentRuleRegistry();

        Assert.HasCount(109, registry.Registrations, "Every rule registers once, with no orphan code.");

        foreach(string wire in AllEntailmentRuleWireStrings)
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.EntailmentRule.ForRule(wire);
            Assert.IsTrue(registry.TryFind(code, out EpistemicReasonRegistration? forward), "Forward: the rule string resolves to a registration.");
            Assert.IsNotNull(forward);
        }

        foreach(EpistemicReasonRegistration registration in registry.Registrations)
        {
            string canonical = Encoding.UTF8.GetString(registration.CanonicalName.Span);
            EpistemicReasonCode roundTrip = WellKnownEpistemicReasons.EntailmentRule.ForRule(canonical);
            Assert.AreEqual(registration.Code.Code, roundTrip.Code, "Reverse: a registration's canonical name maps back to its own code.");
        }
    }

    /// <summary>The one hundred nine entailment-rule registrations pass the full three-rung acceptance ladder.</summary>
    [TestMethod]
    public void EntailmentRuleRegistrationsPassTheFullLadder()
    {
        EpistemicReasonRegistry registry = BuildEntailmentRuleRegistry();

        Assert.IsFalse(registry.IsEmpty);
        Assert.HasCount(109, registry.Registrations);
    }

    /// <summary>Every registered entailment-rule code is explanation-queryable with non-empty WHY-text.</summary>
    [TestMethod]
    public void EveryEntailmentRuleCodeIsExplanationQueryable()
    {
        EpistemicReasonRegistry registry = BuildEntailmentRuleRegistry();

        foreach(EpistemicReasonRegistration registration in registry.Registrations)
        {
            Assert.IsTrue(registry.TryGetExplanation(registration.Code, out ReadOnlyMemory<byte> explanation), "Every registered code resolves an explanation.");
            Assert.IsGreaterThan(0, explanation.Length, "The explanation text is non-empty.");
        }
    }

    /// <summary>Every entailment-rule registration declares deferred projection coverage — identities-only.</summary>
    [TestMethod]
    public void EveryEntailmentRuleCoverageIsDeferred()
    {
        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.EntailmentRule.CreateEntailmentRuleRegistrations())
        {
            Assert.IsTrue(registration.Coverage.IsDeferred, "The entailment-rule codes are registration identities only, coverage deferred.");
        }
    }

    /// <summary>All four first-party families compose into one builder that freezes clean over all one hundred eighteen codes, each of which resolves — the append-only band 4 does not collide with bands 1, 2, or 3.</summary>
    [TestMethod]
    public void AllFourFirstPartyFamiliesComposeCleanInOneBuilder()
    {
        EpistemicReasonRegistry registry = BuildAllFourFamiliesRegistry();

        Assert.HasCount(118, registry.Registrations);

        foreach(EpistemicReasonRegistration registration in registry.Registrations)
        {
            Assert.IsTrue(registry.TryFind(registration.Code, out EpistemicReasonRegistration? resolved), "Every composed code resolves.");
            Assert.IsNotNull(resolved);
        }
    }

    /// <summary>An undefined rule string trips the invariant-violation arm of <see cref="WellKnownEpistemicReasons.EntailmentRule.ForRule"/>.</summary>
    [TestMethod]
    public void EntailmentRuleUndefinedRuleStringThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WellKnownEpistemicReasons.EntailmentRule.ForRule("not-a-rule"));
    }

    /// <summary>Every entailment-rule code sits inside the family's reserved band-4 block, with the catalog occupying the contiguous head 40000..40108 and leaving append-only headroom below the block's inclusive end.</summary>
    [TestMethod]
    public void EntailmentRuleCodesStayWithinTheReservedBandFourBlock()
    {
        EpistemicReasonClassFamily family = WellKnownEpistemicReasons.EntailmentRule.Family;
        Assert.AreEqual(4, family.BandIndex, "The entailment-rule family owns band index 4.");
        Assert.AreEqual(40000, family.BlockStart, "The band-4 block starts at forty thousand.");
        Assert.AreEqual(49999, family.BlockInclusiveEnd, "The band-4 block ends at forty-nine thousand nine hundred ninety-nine.");

        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        foreach(string wire in AllEntailmentRuleWireStrings)
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.EntailmentRule.ForRule(wire);
            Assert.AreEqual(family.BandIndex, code.ClassBand, $"The '{wire}' code's high digits alone place it in the family's band.");
            minimum = Math.Min(minimum, code.Code);
            maximum = Math.Max(maximum, code.Code);
        }

        Assert.AreEqual(40000, minimum, "The catalog's lowest code is the band base.");
        Assert.AreEqual(40108, maximum, "The catalog's highest code is the band base plus one hundred eight.");
        Assert.IsLessThan(family.BlockInclusiveEnd, maximum, "The catalog leaves append-only headroom below the block's inclusive end.");
    }

    /// <summary>The minted band-4 family stays dark: the process-wide empty registry the default engine options carry composes no entailment-rule code, so the default surface resolves none of them.</summary>
    [TestMethod]
    public void EntailmentRuleFamilyStaysDarkInTheEmptyRegistry()
    {
        Assert.IsTrue(EpistemicReasonRegistry.Empty.IsEmpty, "The shared registry the default engine options carry holds no registrations.");
        Assert.IsEmpty(EpistemicReasonRegistry.Empty.Registrations);

        foreach(string wire in AllEntailmentRuleWireStrings)
        {
            EpistemicReasonCode code = WellKnownEpistemicReasons.EntailmentRule.ForRule(wire);
            Assert.IsFalse(EpistemicReasonRegistry.Empty.TryFind(code, out _), $"The empty registry resolves no entailment-rule code, so '{wire}' stays dark by default.");
            Assert.IsFalse(EpistemicReasonRegistry.Empty.TryGetExplanation(code, out _), $"The empty registry answers no explanation for '{wire}'.");
        }
    }

    /// <summary>The pinned wire strings, reflected once off <see cref="EntailmentRules"/> — the identities the family mints codes for.</summary>
    private static IReadOnlyList<string> AllEntailmentRuleWireStrings { get; } = ReflectEntailmentRuleWireStrings();

    /// <summary>Reflects the public constant string values of <see cref="EntailmentRules"/> at runtime, so adding a rule surfaces through the census rows.</summary>
    /// <returns>Every entailment-rule wire string.</returns>
    private static List<string> ReflectEntailmentRuleWireStrings()
    {
        FieldInfo[] fields = typeof(EntailmentRules).GetFields(BindingFlags.Public | BindingFlags.Static);
        List<string> values = [];
        for(int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if(field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            {
                values.Add((string)field.GetRawConstantValue()!);
            }
        }

        return values;
    }

    /// <summary>Builds the frozen registry over the entailment-rule registrations only.</summary>
    /// <returns>The frozen registry.</returns>
    private static EpistemicReasonRegistry BuildEntailmentRuleRegistry()
    {
        EpistemicReasonRegistryBuilder builder = new();
        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.EntailmentRule.CreateEntailmentRuleRegistrations())
        {
            builder.Add(registration);
        }

        return builder.Build();
    }

    /// <summary>Builds the frozen registry over all four first-party families: selection reasons, derivation-origin codes, the conditionality-loss code, and the entailment rules.</summary>
    /// <returns>The frozen registry.</returns>
    private static EpistemicReasonRegistry BuildAllFourFamiliesRegistry()
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

        foreach(EpistemicReasonRegistration registration in WellKnownEpistemicReasons.EntailmentRule.CreateEntailmentRuleRegistrations())
        {
            builder.Add(registration);
        }

        return builder.Build();
    }
}
