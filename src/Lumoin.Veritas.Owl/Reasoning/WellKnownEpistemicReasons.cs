using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core.Epistemics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The first-party epistemic reason codes the reasoning layer mints on the engine's epistemic
/// surface, grouped by the class family they belong to.
/// </summary>
/// <remarks>
/// Each nested class is one class family: it owns a contiguous digit band, exposes the
/// <see cref="EpistemicReasonCode"/> for every reason it names, maps its closed source enum onto
/// those codes, and produces the registrations a composition root feeds through the
/// <see cref="EpistemicReasonRegistryBuilder"/> ladder. The codes are the wire identities; the
/// canonical names are the human-facing <c>u8</c> source of truth; the explanations are cold
/// WHY-text queried off the decision path.
/// </remarks>
public static class WellKnownEpistemicReasons
{
    /// <summary>
    /// The class family for <see cref="ReasoningSelectionReason"/> — why the rendezvous selected
    /// the reasoning strategy it did — laid out over the band-1 code block.
    /// </summary>
    /// <remarks>
    /// Every code is <c>10000 + (int)</c> its <see cref="ReasoningSelectionReason"/> value, so the
    /// closed enum's identity is preserved as a code identity beside it and the class band recovers
    /// by integer division. Coverage is <see cref="EpistemicProjectionCoverage.Deferred"/>: the
    /// codes are registered as identities now, and projection plumbing rides the consumer lanes.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownEpistemicReasons.SelectionReason.X is the intended usage pattern.")]
    public static class SelectionReason
    {
        /// <summary>The band base — band index 1 times ten thousand — added to each source enum value to form a code.</summary>
        private const int BandBase = 10000;

        /// <summary>The class family this group registers under: band index 1, canonically named <c>ReasoningSelectionReason</c>.</summary>
        public static EpistemicReasonClassFamily Family { get; } = new(1, "ReasoningSelectionReason"u8.ToArray());

        /// <summary>The code for <see cref="ReasoningSelectionReason.RdfsSufficient"/>.</summary>
        public static EpistemicReasonCode RdfsSufficient { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.RdfsSufficient);

        /// <summary>The code for <see cref="ReasoningSelectionReason.RlSufficient"/>.</summary>
        public static EpistemicReasonCode RlSufficient { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.RlSufficient);

        /// <summary>The code for <see cref="ReasoningSelectionReason.BeyondRlDelegated"/>.</summary>
        public static EpistemicReasonCode BeyondRlDelegated { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.BeyondRlDelegated);

        /// <summary>The code for <see cref="ReasoningSelectionReason.BeyondRlReported"/>.</summary>
        public static EpistemicReasonCode BeyondRlReported { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.BeyondRlReported);

        /// <summary>The code for <see cref="ReasoningSelectionReason.ElClassificationBuilt"/>.</summary>
        public static EpistemicReasonCode ElClassificationBuilt { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.ElClassificationBuilt);

        /// <summary>The code for <see cref="ReasoningSelectionReason.ElClassificationReused"/>.</summary>
        public static EpistemicReasonCode ElClassificationReused { get; } = EpistemicReasonCode.Create(BandBase + (int)ReasoningSelectionReason.ElClassificationReused);

        /// <summary>Maps a selection reason onto its code.</summary>
        /// <param name="reason">The selection reason.</param>
        /// <returns>The reason's code.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ReasoningSelectionReason"/> — an invariant violation.</exception>
        public static EpistemicReasonCode ForSelectionReason(ReasoningSelectionReason reason)
        {
            return reason switch
            {
                ReasoningSelectionReason.RdfsSufficient => RdfsSufficient,
                ReasoningSelectionReason.RlSufficient => RlSufficient,
                ReasoningSelectionReason.BeyondRlDelegated => BeyondRlDelegated,
                ReasoningSelectionReason.BeyondRlReported => BeyondRlReported,
                ReasoningSelectionReason.ElClassificationBuilt => ElClassificationBuilt,
                ReasoningSelectionReason.ElClassificationReused => ElClassificationReused,
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Undefined reasoning selection reason."),
            };
        }

        /// <summary>The canonical <c>u8</c> name for <see cref="RdfsSufficient"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> RdfsSufficientName { get; } = "RdfsSufficient"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="RdfsSufficient"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> RdfsSufficientExplanation { get; } = "The TBox stays within the RDFS vocabulary so the streaming pass answers."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="RlSufficient"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> RlSufficientName { get; } = "RlSufficient"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="RlSufficient"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> RlSufficientExplanation { get; } = "The TBox is within the OWL 2 RL profile so the RL closure answers completely."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="BeyondRlDelegated"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> BeyondRlDelegatedName { get; } = "BeyondRlDelegated"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="BeyondRlDelegated"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> BeyondRlDelegatedExplanation { get; } = "The TBox exceeds OWL 2 RL so the sound RL closure ran and the exceeding axioms were handed to the description-logic delegate."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="BeyondRlReported"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> BeyondRlReportedName { get; } = "BeyondRlReported"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="BeyondRlReported"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> BeyondRlReportedExplanation { get; } = "The TBox exceeds OWL 2 RL and no description-logic delegate is wired so the exceeding axioms are reported, never silently dropped."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ElClassificationBuilt"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ElClassificationBuiltName { get; } = "ElClassificationBuilt"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ElClassificationBuilt"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ElClassificationBuiltExplanation { get; } = "The EL classification was computed for this store generation so this request paid the build that later consumers amortise."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ElClassificationReused"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ElClassificationReusedName { get; } = "ElClassificationReused"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ElClassificationReused"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ElClassificationReusedExplanation { get; } = "The EL classification for this store generation already existed so it was reused at zero cost."u8.ToArray();

        /// <summary>The registration for <see cref="RdfsSufficient"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration RdfsSufficientRegistration()
        {
            return Register(RdfsSufficient, RdfsSufficientName, RdfsSufficientExplanation);
        }

        /// <summary>The registration for <see cref="RlSufficient"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration RlSufficientRegistration()
        {
            return Register(RlSufficient, RlSufficientName, RlSufficientExplanation);
        }

        /// <summary>The registration for <see cref="BeyondRlDelegated"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration BeyondRlDelegatedRegistration()
        {
            return Register(BeyondRlDelegated, BeyondRlDelegatedName, BeyondRlDelegatedExplanation);
        }

        /// <summary>The registration for <see cref="BeyondRlReported"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration BeyondRlReportedRegistration()
        {
            return Register(BeyondRlReported, BeyondRlReportedName, BeyondRlReportedExplanation);
        }

        /// <summary>The registration for <see cref="ElClassificationBuilt"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration ElClassificationBuiltRegistration()
        {
            return Register(ElClassificationBuilt, ElClassificationBuiltName, ElClassificationBuiltExplanation);
        }

        /// <summary>The registration for <see cref="ElClassificationReused"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration ElClassificationReusedRegistration()
        {
            return Register(ElClassificationReused, ElClassificationReusedName, ElClassificationReusedExplanation);
        }

        /// <summary>Produces every selection-reason registration, in code order, for a composition root to feed the ladder.</summary>
        /// <returns>The six selection-reason registrations.</returns>
        public static IReadOnlyList<EpistemicReasonRegistration> CreateSelectionReasonRegistrations()
        {
            return
            [
                RdfsSufficientRegistration(),
                RlSufficientRegistration(),
                BeyondRlDelegatedRegistration(),
                BeyondRlReportedRegistration(),
                ElClassificationBuiltRegistration(),
                ElClassificationReusedRegistration(),
            ];
        }

        /// <summary>Binds a code, its canonical name, and its explanation into a deferred-coverage registration under <see cref="Family"/>.</summary>
        /// <param name="code">The code being registered.</param>
        /// <param name="canonicalName">The canonical name as <c>u8</c> bytes.</param>
        /// <param name="explanation">The cold WHY-text as <c>u8</c> bytes.</param>
        /// <returns>The registration.</returns>
        private static EpistemicReasonRegistration Register(EpistemicReasonCode code, ReadOnlyMemory<byte> canonicalName, ReadOnlyMemory<byte> explanation)
        {
            return new EpistemicReasonRegistration(Family, code, canonicalName, explanation, EpistemicProjectionCoverage.Deferred);
        }
    }

    /// <summary>
    /// The class family naming whether a derived head's truth stands independent of any
    /// unresolved disjunct in its derivation ancestry or instead rides an unrecorded choice —
    /// laid out over the band-2 code block.
    /// </summary>
    /// <remarks>
    /// The codes are assigned directly off <see cref="BandBase"/> with no backing closed enum:
    /// the family names a runtime provenance distinction rather than a source enumeration.
    /// Coverage is <see cref="EpistemicProjectionCoverage.Deferred"/>: the codes are registered as
    /// identities now, and projection plumbing rides the consumer lanes.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownEpistemicReasons.DerivationOriginKind.X is the intended usage pattern.")]
    public static class DerivationOriginKind
    {
        /// <summary>The band base — band index 2 times ten thousand — added to each code offset.</summary>
        private const int BandBase = 20000;

        /// <summary>The class family this group registers under: band index 2, canonically named <c>DerivationOriginKind</c>.</summary>
        public static EpistemicReasonClassFamily Family { get; } = new(2, "DerivationOriginKind"u8.ToArray());

        /// <summary>The code naming a head whose truth is fixed independent of any unresolved disjunct in its ancestry.</summary>
        public static EpistemicReasonCode DecidedUnderNoChoice { get; } = EpistemicReasonCode.Create(BandBase + 0);

        /// <summary>The code naming a head that depends on a disjunct choice that was not recorded or resolved.</summary>
        public static EpistemicReasonCode DerivedUnderChoice { get; } = EpistemicReasonCode.Create(BandBase + 1);

        /// <summary>The canonical <c>u8</c> name for <see cref="DecidedUnderNoChoice"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DecidedUnderNoChoiceName { get; } = "DecidedUnderNoChoice"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DecidedUnderNoChoice"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DecidedUnderNoChoiceExplanation { get; } = "The emitted head's truth is fixed independent of any unresolved disjunct in its derivation ancestry."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DerivedUnderChoice"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DerivedUnderChoiceName { get; } = "DerivedUnderChoice"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DerivedUnderChoice"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DerivedUnderChoiceExplanation { get; } = "The emitted head depends on a disjunct choice that was not recorded or resolved."u8.ToArray();

        /// <summary>The registration for <see cref="DecidedUnderNoChoice"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration DecidedUnderNoChoiceRegistration()
        {
            return Register(DecidedUnderNoChoice, DecidedUnderNoChoiceName, DecidedUnderNoChoiceExplanation);
        }

        /// <summary>The registration for <see cref="DerivedUnderChoice"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration DerivedUnderChoiceRegistration()
        {
            return Register(DerivedUnderChoice, DerivedUnderChoiceName, DerivedUnderChoiceExplanation);
        }

        /// <summary>Produces every derivation-origin registration, in code order, for a composition root to feed the ladder.</summary>
        /// <returns>The two derivation-origin registrations.</returns>
        public static IReadOnlyList<EpistemicReasonRegistration> CreateDerivationOriginRegistrations()
        {
            return
            [
                DecidedUnderNoChoiceRegistration(),
                DerivedUnderChoiceRegistration(),
            ];
        }

        /// <summary>Binds a code, its canonical name, and its explanation into a deferred-coverage registration under <see cref="Family"/>.</summary>
        /// <param name="code">The code being registered.</param>
        /// <param name="canonicalName">The canonical name as <c>u8</c> bytes.</param>
        /// <param name="explanation">The cold WHY-text as <c>u8</c> bytes.</param>
        /// <returns>The registration.</returns>
        private static EpistemicReasonRegistration Register(EpistemicReasonCode code, ReadOnlyMemory<byte> canonicalName, ReadOnlyMemory<byte> explanation)
        {
            return new EpistemicReasonRegistration(Family, code, canonicalName, explanation, EpistemicProjectionCoverage.Deferred);
        }
    }

    /// <summary>
    /// The class family naming the conditionality-loss lint — a ground-truth-free mechanism
    /// detector over a derivation step whose conclusion is strictly narrower in choice-conditions
    /// than the union of its premises — laid out over the band-3 code block.
    /// </summary>
    /// <remarks>
    /// The single code is assigned directly off <see cref="BandBase"/> with no backing closed enum.
    /// The lint names the mechanism and is not an assertion of wrongness. Coverage is
    /// <see cref="EpistemicProjectionCoverage.Deferred"/>: the code is registered as an identity now,
    /// and projection plumbing rides the consumer lanes.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownEpistemicReasons.ConditionalityLossLint.X is the intended usage pattern.")]
    public static class ConditionalityLossLint
    {
        /// <summary>The band base — band index 3 times ten thousand — added to each code offset.</summary>
        private const int BandBase = 30000;

        /// <summary>The class family this group registers under: band index 3, canonically named <c>ConditionalityLossLint</c>.</summary>
        public static EpistemicReasonClassFamily Family { get; } = new(3, "ConditionalityLossLint"u8.ToArray());

        /// <summary>The code naming a derivation step whose conclusion is strictly narrower in choice-conditions than the union of its premises with no recorded split.</summary>
        public static EpistemicReasonCode ConditionalityDropped { get; } = EpistemicReasonCode.Create(BandBase + 0);

        /// <summary>The canonical <c>u8</c> name for <see cref="ConditionalityDropped"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ConditionalityDroppedName { get; } = "ConditionalityDropped"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ConditionalityDropped"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ConditionalityDroppedExplanation { get; } = "A derivation step's conclusion is strictly narrower in choice-conditions than the union of its premises with no recorded split; this names the mechanism and is not an assertion of wrongness."u8.ToArray();

        /// <summary>The registration for <see cref="ConditionalityDropped"/>.</summary>
        /// <returns>The registration.</returns>
        public static EpistemicReasonRegistration ConditionalityDroppedRegistration()
        {
            return Register(ConditionalityDropped, ConditionalityDroppedName, ConditionalityDroppedExplanation);
        }

        /// <summary>Produces every conditionality-loss registration, in code order, for a composition root to feed the ladder.</summary>
        /// <returns>The single conditionality-loss registration.</returns>
        public static IReadOnlyList<EpistemicReasonRegistration> CreateConditionalityLossRegistrations()
        {
            return
            [
                ConditionalityDroppedRegistration(),
            ];
        }

        /// <summary>Binds a code, its canonical name, and its explanation into a deferred-coverage registration under <see cref="Family"/>.</summary>
        /// <param name="code">The code being registered.</param>
        /// <param name="canonicalName">The canonical name as <c>u8</c> bytes.</param>
        /// <param name="explanation">The cold WHY-text as <c>u8</c> bytes.</param>
        /// <returns>The registration.</returns>
        private static EpistemicReasonRegistration Register(EpistemicReasonCode code, ReadOnlyMemory<byte> canonicalName, ReadOnlyMemory<byte> explanation)
        {
            return new EpistemicReasonRegistration(Family, code, canonicalName, explanation, EpistemicProjectionCoverage.Deferred);
        }
    }

    /// <summary>
    /// The class family naming the entailment rules the materializers fire, minting one code per rule
    /// identity behind the unchanged <see cref="EntailmentRules"/> string catalog — laid out over the
    /// band-4 code block.
    /// </summary>
    /// <remarks>
    /// The codes are assigned directly off <see cref="BandBase"/> in declaration order with no backing
    /// closed enum. Each canonical name is the rule's wire string — the value carried by
    /// <see cref="InferenceTraceEvent.Rule"/> and <see cref="Rl.OwlRlResult.InconsistencyRule"/> — not the
    /// C# identifier, so the registry resolves a fired rule without changing the bare-string surface the
    /// materializers emit and the pins compare. Coverage is
    /// <see cref="EpistemicProjectionCoverage.Deferred"/>: the codes are registered as identities now, and
    /// projection plumbing rides the consumer lanes.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownEpistemicReasons.EntailmentRule.X is the intended usage pattern.")]
    public static class EntailmentRule
    {
        /// <summary>The band base — band index 4 times ten thousand — added to each code offset.</summary>
        private const int BandBase = 40000;

        /// <summary>The class family this group registers under: band index 4, canonically named <c>EntailmentRule</c>.</summary>
        public static EpistemicReasonClassFamily Family { get; } = new(4, "EntailmentRule"u8.ToArray());

        /// <summary>The code for the <see cref="EntailmentRules.Rdf1"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdf1 { get; } = EpistemicReasonCode.Create(BandBase + 0);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs2"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs2 { get; } = EpistemicReasonCode.Create(BandBase + 1);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs3"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs3 { get; } = EpistemicReasonCode.Create(BandBase + 2);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs5"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs5 { get; } = EpistemicReasonCode.Create(BandBase + 3);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs6"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs6 { get; } = EpistemicReasonCode.Create(BandBase + 4);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs7"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs7 { get; } = EpistemicReasonCode.Create(BandBase + 5);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs8"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs8 { get; } = EpistemicReasonCode.Create(BandBase + 6);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs9"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs9 { get; } = EpistemicReasonCode.Create(BandBase + 7);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs10"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs10 { get; } = EpistemicReasonCode.Create(BandBase + 8);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs11"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs11 { get; } = EpistemicReasonCode.Create(BandBase + 9);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs12"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs12 { get; } = EpistemicReasonCode.Create(BandBase + 10);

        /// <summary>The code for the <see cref="EntailmentRules.Rdfs13"/> entailment rule.</summary>
        public static EpistemicReasonCode Rdfs13 { get; } = EpistemicReasonCode.Create(BandBase + 11);

        /// <summary>The code for the <see cref="EntailmentRules.AxiomaticTyping"/> entailment rule.</summary>
        public static EpistemicReasonCode AxiomaticTyping { get; } = EpistemicReasonCode.Create(BandBase + 12);

        /// <summary>The code for the <see cref="EntailmentRules.EqSym"/> entailment rule.</summary>
        public static EpistemicReasonCode EqSym { get; } = EpistemicReasonCode.Create(BandBase + 13);

        /// <summary>The code for the <see cref="EntailmentRules.EqTrans"/> entailment rule.</summary>
        public static EpistemicReasonCode EqTrans { get; } = EpistemicReasonCode.Create(BandBase + 14);

        /// <summary>The code for the <see cref="EntailmentRules.EqRepS"/> entailment rule.</summary>
        public static EpistemicReasonCode EqRepS { get; } = EpistemicReasonCode.Create(BandBase + 15);

        /// <summary>The code for the <see cref="EntailmentRules.EqRepP"/> entailment rule.</summary>
        public static EpistemicReasonCode EqRepP { get; } = EpistemicReasonCode.Create(BandBase + 16);

        /// <summary>The code for the <see cref="EntailmentRules.EqRepO"/> entailment rule.</summary>
        public static EpistemicReasonCode EqRepO { get; } = EpistemicReasonCode.Create(BandBase + 17);

        /// <summary>The code for the <see cref="EntailmentRules.EqDiff1"/> entailment rule.</summary>
        public static EpistemicReasonCode EqDiff1 { get; } = EpistemicReasonCode.Create(BandBase + 18);

        /// <summary>The code for the <see cref="EntailmentRules.EqDiff2"/> entailment rule.</summary>
        public static EpistemicReasonCode EqDiff2 { get; } = EpistemicReasonCode.Create(BandBase + 19);

        /// <summary>The code for the <see cref="EntailmentRules.DifferentFromSymmetry"/> entailment rule.</summary>
        public static EpistemicReasonCode DifferentFromSymmetry { get; } = EpistemicReasonCode.Create(BandBase + 20);

        /// <summary>The code for the <see cref="EntailmentRules.PrpDom"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpDom { get; } = EpistemicReasonCode.Create(BandBase + 21);

        /// <summary>The code for the <see cref="EntailmentRules.PrpRng"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpRng { get; } = EpistemicReasonCode.Create(BandBase + 22);

        /// <summary>The code for the <see cref="EntailmentRules.PrpFp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpFp { get; } = EpistemicReasonCode.Create(BandBase + 23);

        /// <summary>The code for the <see cref="EntailmentRules.PrpIfp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpIfp { get; } = EpistemicReasonCode.Create(BandBase + 24);

        /// <summary>The code for the <see cref="EntailmentRules.PrpIrp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpIrp { get; } = EpistemicReasonCode.Create(BandBase + 25);

        /// <summary>The code for the <see cref="EntailmentRules.PrpSymp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpSymp { get; } = EpistemicReasonCode.Create(BandBase + 26);

        /// <summary>The code for the <see cref="EntailmentRules.PrpAsyp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpAsyp { get; } = EpistemicReasonCode.Create(BandBase + 27);

        /// <summary>The code for the <see cref="EntailmentRules.PrpTrp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpTrp { get; } = EpistemicReasonCode.Create(BandBase + 28);

        /// <summary>The code for the <see cref="EntailmentRules.ReflexiveInstantiation"/> entailment rule.</summary>
        public static EpistemicReasonCode ReflexiveInstantiation { get; } = EpistemicReasonCode.Create(BandBase + 29);

        /// <summary>The code for the <see cref="EntailmentRules.PrpSpo1"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpSpo1 { get; } = EpistemicReasonCode.Create(BandBase + 30);

        /// <summary>The code for the <see cref="EntailmentRules.PrpSpo2"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpSpo2 { get; } = EpistemicReasonCode.Create(BandBase + 31);

        /// <summary>The code for the <see cref="EntailmentRules.PrpEqp1"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpEqp1 { get; } = EpistemicReasonCode.Create(BandBase + 32);

        /// <summary>The code for the <see cref="EntailmentRules.PrpEqp2"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpEqp2 { get; } = EpistemicReasonCode.Create(BandBase + 33);

        /// <summary>The code for the <see cref="EntailmentRules.PrpPdw"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpPdw { get; } = EpistemicReasonCode.Create(BandBase + 34);

        /// <summary>The code for the <see cref="EntailmentRules.PrpAdp"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpAdp { get; } = EpistemicReasonCode.Create(BandBase + 35);

        /// <summary>The code for the <see cref="EntailmentRules.PrpInv1"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpInv1 { get; } = EpistemicReasonCode.Create(BandBase + 36);

        /// <summary>The code for the <see cref="EntailmentRules.PrpInv2"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpInv2 { get; } = EpistemicReasonCode.Create(BandBase + 37);

        /// <summary>The code for the <see cref="EntailmentRules.PrpKey"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpKey { get; } = EpistemicReasonCode.Create(BandBase + 38);

        /// <summary>The code for the <see cref="EntailmentRules.PrpNpa"/> entailment rule.</summary>
        public static EpistemicReasonCode PrpNpa { get; } = EpistemicReasonCode.Create(BandBase + 39);

        /// <summary>The code for the <see cref="EntailmentRules.ClsNothing2"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsNothing2 { get; } = EpistemicReasonCode.Create(BandBase + 40);

        /// <summary>The code for the <see cref="EntailmentRules.ClsInt1"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsInt1 { get; } = EpistemicReasonCode.Create(BandBase + 41);

        /// <summary>The code for the <see cref="EntailmentRules.ClsInt2"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsInt2 { get; } = EpistemicReasonCode.Create(BandBase + 42);

        /// <summary>The code for the <see cref="EntailmentRules.ClsUni"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsUni { get; } = EpistemicReasonCode.Create(BandBase + 43);

        /// <summary>The code for the <see cref="EntailmentRules.ClsCom"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsCom { get; } = EpistemicReasonCode.Create(BandBase + 44);

        /// <summary>The code for the <see cref="EntailmentRules.ClsSvf1"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsSvf1 { get; } = EpistemicReasonCode.Create(BandBase + 45);

        /// <summary>The code for the <see cref="EntailmentRules.ClsSvf2"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsSvf2 { get; } = EpistemicReasonCode.Create(BandBase + 46);

        /// <summary>The code for the <see cref="EntailmentRules.ClsAvf"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsAvf { get; } = EpistemicReasonCode.Create(BandBase + 47);

        /// <summary>The code for the <see cref="EntailmentRules.ClsHv1"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsHv1 { get; } = EpistemicReasonCode.Create(BandBase + 48);

        /// <summary>The code for the <see cref="EntailmentRules.ClsHv2"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsHv2 { get; } = EpistemicReasonCode.Create(BandBase + 49);

        /// <summary>The code for the <see cref="EntailmentRules.ClsMaxc1"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsMaxc1 { get; } = EpistemicReasonCode.Create(BandBase + 50);

        /// <summary>The code for the <see cref="EntailmentRules.ClsMaxc2"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsMaxc2 { get; } = EpistemicReasonCode.Create(BandBase + 51);

        /// <summary>The code for the <see cref="EntailmentRules.ClsMaxqc1"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsMaxqc1 { get; } = EpistemicReasonCode.Create(BandBase + 52);

        /// <summary>The code for the <see cref="EntailmentRules.ClsMaxqc4"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsMaxqc4 { get; } = EpistemicReasonCode.Create(BandBase + 53);

        /// <summary>The code for the <see cref="EntailmentRules.ClsOo"/> entailment rule.</summary>
        public static EpistemicReasonCode ClsOo { get; } = EpistemicReasonCode.Create(BandBase + 54);

        /// <summary>The code for the <see cref="EntailmentRules.CaxSco"/> entailment rule.</summary>
        public static EpistemicReasonCode CaxSco { get; } = EpistemicReasonCode.Create(BandBase + 55);

        /// <summary>The code for the <see cref="EntailmentRules.CaxEqc1"/> entailment rule.</summary>
        public static EpistemicReasonCode CaxEqc1 { get; } = EpistemicReasonCode.Create(BandBase + 56);

        /// <summary>The code for the <see cref="EntailmentRules.CaxEqc2"/> entailment rule.</summary>
        public static EpistemicReasonCode CaxEqc2 { get; } = EpistemicReasonCode.Create(BandBase + 57);

        /// <summary>The code for the <see cref="EntailmentRules.CaxDw"/> entailment rule.</summary>
        public static EpistemicReasonCode CaxDw { get; } = EpistemicReasonCode.Create(BandBase + 58);

        /// <summary>The code for the <see cref="EntailmentRules.CaxAdc"/> entailment rule.</summary>
        public static EpistemicReasonCode CaxAdc { get; } = EpistemicReasonCode.Create(BandBase + 59);

        /// <summary>The code for the <see cref="EntailmentRules.DtDiff"/> entailment rule.</summary>
        public static EpistemicReasonCode DtDiff { get; } = EpistemicReasonCode.Create(BandBase + 60);

        /// <summary>The code for the <see cref="EntailmentRules.DtNotType"/> entailment rule.</summary>
        public static EpistemicReasonCode DtNotType { get; } = EpistemicReasonCode.Create(BandBase + 61);

        /// <summary>The code for the <see cref="EntailmentRules.DtRangeIntersection"/> entailment rule.</summary>
        public static EpistemicReasonCode DtRangeIntersection { get; } = EpistemicReasonCode.Create(BandBase + 62);

        /// <summary>The code for the <see cref="EntailmentRules.ChainTransitivity"/> entailment rule.</summary>
        public static EpistemicReasonCode ChainTransitivity { get; } = EpistemicReasonCode.Create(BandBase + 63);

        /// <summary>The code for the <see cref="EntailmentRules.TransitivityChain"/> entailment rule.</summary>
        public static EpistemicReasonCode TransitivityChain { get; } = EpistemicReasonCode.Create(BandBase + 64);

        /// <summary>The code for the <see cref="EntailmentRules.ScmCls"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmCls { get; } = EpistemicReasonCode.Create(BandBase + 65);

        /// <summary>The code for the <see cref="EntailmentRules.ScmSco"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmSco { get; } = EpistemicReasonCode.Create(BandBase + 66);

        /// <summary>The code for the <see cref="EntailmentRules.ScmEqc1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmEqc1 { get; } = EpistemicReasonCode.Create(BandBase + 67);

        /// <summary>The code for the <see cref="EntailmentRules.ScmEqc2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmEqc2 { get; } = EpistemicReasonCode.Create(BandBase + 68);

        /// <summary>The code for the <see cref="EntailmentRules.ScmSpo"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmSpo { get; } = EpistemicReasonCode.Create(BandBase + 69);

        /// <summary>The code for the <see cref="EntailmentRules.ScmEqp1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmEqp1 { get; } = EpistemicReasonCode.Create(BandBase + 70);

        /// <summary>The code for the <see cref="EntailmentRules.ScmEqp2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmEqp2 { get; } = EpistemicReasonCode.Create(BandBase + 71);

        /// <summary>The code for the <see cref="EntailmentRules.ScmDom1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmDom1 { get; } = EpistemicReasonCode.Create(BandBase + 72);

        /// <summary>The code for the <see cref="EntailmentRules.ScmDom2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmDom2 { get; } = EpistemicReasonCode.Create(BandBase + 73);

        /// <summary>The code for the <see cref="EntailmentRules.ScmRng1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmRng1 { get; } = EpistemicReasonCode.Create(BandBase + 74);

        /// <summary>The code for the <see cref="EntailmentRules.ScmRng2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmRng2 { get; } = EpistemicReasonCode.Create(BandBase + 75);

        /// <summary>The code for the <see cref="EntailmentRules.ScmInt"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmInt { get; } = EpistemicReasonCode.Create(BandBase + 76);

        /// <summary>The code for the <see cref="EntailmentRules.ScmUni"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmUni { get; } = EpistemicReasonCode.Create(BandBase + 77);

        /// <summary>The code for the <see cref="EntailmentRules.EqRef"/> entailment rule.</summary>
        public static EpistemicReasonCode EqRef { get; } = EpistemicReasonCode.Create(BandBase + 78);

        /// <summary>The code for the <see cref="EntailmentRules.ScmOp"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmOp { get; } = EpistemicReasonCode.Create(BandBase + 79);

        /// <summary>The code for the <see cref="EntailmentRules.ScmDp"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmDp { get; } = EpistemicReasonCode.Create(BandBase + 80);

        /// <summary>The code for the <see cref="EntailmentRules.ScmHv"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmHv { get; } = EpistemicReasonCode.Create(BandBase + 81);

        /// <summary>The code for the <see cref="EntailmentRules.ScmSvf1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmSvf1 { get; } = EpistemicReasonCode.Create(BandBase + 82);

        /// <summary>The code for the <see cref="EntailmentRules.ScmSvf2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmSvf2 { get; } = EpistemicReasonCode.Create(BandBase + 83);

        /// <summary>The code for the <see cref="EntailmentRules.ScmAvf1"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmAvf1 { get; } = EpistemicReasonCode.Create(BandBase + 84);

        /// <summary>The code for the <see cref="EntailmentRules.ScmAvf2"/> entailment rule.</summary>
        public static EpistemicReasonCode ScmAvf2 { get; } = EpistemicReasonCode.Create(BandBase + 85);

        /// <summary>The code for the <see cref="EntailmentRules.InverseCharacteristicTransfer"/> entailment rule.</summary>
        public static EpistemicReasonCode InverseCharacteristicTransfer { get; } = EpistemicReasonCode.Create(BandBase + 86);

        /// <summary>The code for the <see cref="EntailmentRules.SingletonEnumerationCharacteristic"/> entailment rule.</summary>
        public static EpistemicReasonCode SingletonEnumerationCharacteristic { get; } = EpistemicReasonCode.Create(BandBase + 87);

        /// <summary>The code for the <see cref="EntailmentRules.ComplementOfSymmetry"/> entailment rule.</summary>
        public static EpistemicReasonCode ComplementOfSymmetry { get; } = EpistemicReasonCode.Create(BandBase + 88);

        /// <summary>The code for the <see cref="EntailmentRules.OneOfMemberSubset"/> entailment rule.</summary>
        public static EpistemicReasonCode OneOfMemberSubset { get; } = EpistemicReasonCode.Create(BandBase + 89);

        /// <summary>The code for the <see cref="EntailmentRules.UnionOfMemberSubset"/> entailment rule.</summary>
        public static EpistemicReasonCode UnionOfMemberSubset { get; } = EpistemicReasonCode.Create(BandBase + 90);

        /// <summary>The code for the <see cref="EntailmentRules.UnionExcludedMiddle"/> entailment rule.</summary>
        public static EpistemicReasonCode UnionExcludedMiddle { get; } = EpistemicReasonCode.Create(BandBase + 91);

        /// <summary>The code for the <see cref="EntailmentRules.UnionValueDichotomy"/> entailment rule.</summary>
        public static EpistemicReasonCode UnionValueDichotomy { get; } = EpistemicReasonCode.Create(BandBase + 92);

        /// <summary>The code for the <see cref="EntailmentRules.FunctionalMaxOneUniversal"/> entailment rule.</summary>
        public static EpistemicReasonCode FunctionalMaxOneUniversal { get; } = EpistemicReasonCode.Create(BandBase + 93);

        /// <summary>The code for the <see cref="EntailmentRules.EmptyEnumerationNothing"/> entailment rule.</summary>
        public static EpistemicReasonCode EmptyEnumerationNothing { get; } = EpistemicReasonCode.Create(BandBase + 94);

        /// <summary>The code for the <see cref="EntailmentRules.IntersectionRangeCompletion"/> entailment rule.</summary>
        public static EpistemicReasonCode IntersectionRangeCompletion { get; } = EpistemicReasonCode.Create(BandBase + 95);

        /// <summary>The code for the <see cref="EntailmentRules.DeMorganSubset"/> entailment rule.</summary>
        public static EpistemicReasonCode DeMorganSubset { get; } = EpistemicReasonCode.Create(BandBase + 96);

        /// <summary>The code for the <see cref="EntailmentRules.CardinalityShorthand"/> entailment rule.</summary>
        public static EpistemicReasonCode CardinalityShorthand { get; } = EpistemicReasonCode.Create(BandBase + 97);

        /// <summary>The code for the <see cref="EntailmentRules.SomeValuesFromWitness"/> entailment rule.</summary>
        public static EpistemicReasonCode SomeValuesFromWitness { get; } = EpistemicReasonCode.Create(BandBase + 98);

        /// <summary>The code for the <see cref="EntailmentRules.NilStructureClash"/> entailment rule.</summary>
        public static EpistemicReasonCode NilStructureClash { get; } = EpistemicReasonCode.Create(BandBase + 99);

        /// <summary>The code for the <see cref="EntailmentRules.ThingEnumerationClash"/> entailment rule.</summary>
        public static EpistemicReasonCode ThingEnumerationClash { get; } = EpistemicReasonCode.Create(BandBase + 100);

        /// <summary>The code for the <see cref="EntailmentRules.MinCardinalityOneMembership"/> entailment rule.</summary>
        public static EpistemicReasonCode MinCardinalityOneMembership { get; } = EpistemicReasonCode.Create(BandBase + 101);

        /// <summary>The code for the <see cref="EntailmentRules.TypeDomainUniversalSubsumption"/> entailment rule.</summary>
        public static EpistemicReasonCode TypeDomainUniversalSubsumption { get; } = EpistemicReasonCode.Create(BandBase + 102);

        /// <summary>The code for the <see cref="EntailmentRules.SharedHasValuePropertyCollapse"/> entailment rule.</summary>
        public static EpistemicReasonCode SharedHasValuePropertyCollapse { get; } = EpistemicReasonCode.Create(BandBase + 103);

        /// <summary>The code for the <see cref="EntailmentRules.DisjointRangeVacuousSubproperty"/> entailment rule.</summary>
        public static EpistemicReasonCode DisjointRangeVacuousSubproperty { get; } = EpistemicReasonCode.Create(BandBase + 104);

        /// <summary>The code for the <see cref="EntailmentRules.DisjointRangeClash"/> entailment rule.</summary>
        public static EpistemicReasonCode DisjointRangeClash { get; } = EpistemicReasonCode.Create(BandBase + 105);

        /// <summary>The code for the <see cref="EntailmentRules.DatatypeAliasRetype"/> entailment rule.</summary>
        public static EpistemicReasonCode DatatypeAliasRetype { get; } = EpistemicReasonCode.Create(BandBase + 106);

        /// <summary>The code for the <see cref="EntailmentRules.FibreCardinalityCertificate"/> entailment rule.</summary>
        public static EpistemicReasonCode FibreCardinalityCertificate { get; } = EpistemicReasonCode.Create(BandBase + 107);

        /// <summary>The code for the <see cref="EntailmentRules.DtDisjointIdentity"/> entailment rule.</summary>
        public static EpistemicReasonCode DtDisjointIdentity { get; } = EpistemicReasonCode.Create(BandBase + 108);

        /// <summary>Maps an entailment rule's wire string onto its code.</summary>
        /// <param name="rule">The entailment rule's wire string, as carried by the trace and inconsistency surfaces.</param>
        /// <returns>The rule's code.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined entailment rule — an invariant violation.</exception>
        public static EpistemicReasonCode ForRule(string rule)
        {
            return rule switch
            {
                EntailmentRules.Rdf1 => Rdf1,
                EntailmentRules.Rdfs2 => Rdfs2,
                EntailmentRules.Rdfs3 => Rdfs3,
                EntailmentRules.Rdfs5 => Rdfs5,
                EntailmentRules.Rdfs6 => Rdfs6,
                EntailmentRules.Rdfs7 => Rdfs7,
                EntailmentRules.Rdfs8 => Rdfs8,
                EntailmentRules.Rdfs9 => Rdfs9,
                EntailmentRules.Rdfs10 => Rdfs10,
                EntailmentRules.Rdfs11 => Rdfs11,
                EntailmentRules.Rdfs12 => Rdfs12,
                EntailmentRules.Rdfs13 => Rdfs13,
                EntailmentRules.AxiomaticTyping => AxiomaticTyping,
                EntailmentRules.EqSym => EqSym,
                EntailmentRules.EqTrans => EqTrans,
                EntailmentRules.EqRepS => EqRepS,
                EntailmentRules.EqRepP => EqRepP,
                EntailmentRules.EqRepO => EqRepO,
                EntailmentRules.EqDiff1 => EqDiff1,
                EntailmentRules.EqDiff2 => EqDiff2,
                EntailmentRules.DifferentFromSymmetry => DifferentFromSymmetry,
                EntailmentRules.PrpDom => PrpDom,
                EntailmentRules.PrpRng => PrpRng,
                EntailmentRules.PrpFp => PrpFp,
                EntailmentRules.PrpIfp => PrpIfp,
                EntailmentRules.PrpIrp => PrpIrp,
                EntailmentRules.PrpSymp => PrpSymp,
                EntailmentRules.PrpAsyp => PrpAsyp,
                EntailmentRules.PrpTrp => PrpTrp,
                EntailmentRules.ReflexiveInstantiation => ReflexiveInstantiation,
                EntailmentRules.PrpSpo1 => PrpSpo1,
                EntailmentRules.PrpSpo2 => PrpSpo2,
                EntailmentRules.PrpEqp1 => PrpEqp1,
                EntailmentRules.PrpEqp2 => PrpEqp2,
                EntailmentRules.PrpPdw => PrpPdw,
                EntailmentRules.PrpAdp => PrpAdp,
                EntailmentRules.PrpInv1 => PrpInv1,
                EntailmentRules.PrpInv2 => PrpInv2,
                EntailmentRules.PrpKey => PrpKey,
                EntailmentRules.PrpNpa => PrpNpa,
                EntailmentRules.ClsNothing2 => ClsNothing2,
                EntailmentRules.ClsInt1 => ClsInt1,
                EntailmentRules.ClsInt2 => ClsInt2,
                EntailmentRules.ClsUni => ClsUni,
                EntailmentRules.ClsCom => ClsCom,
                EntailmentRules.ClsSvf1 => ClsSvf1,
                EntailmentRules.ClsSvf2 => ClsSvf2,
                EntailmentRules.ClsAvf => ClsAvf,
                EntailmentRules.ClsHv1 => ClsHv1,
                EntailmentRules.ClsHv2 => ClsHv2,
                EntailmentRules.ClsMaxc1 => ClsMaxc1,
                EntailmentRules.ClsMaxc2 => ClsMaxc2,
                EntailmentRules.ClsMaxqc1 => ClsMaxqc1,
                EntailmentRules.ClsMaxqc4 => ClsMaxqc4,
                EntailmentRules.ClsOo => ClsOo,
                EntailmentRules.CaxSco => CaxSco,
                EntailmentRules.CaxEqc1 => CaxEqc1,
                EntailmentRules.CaxEqc2 => CaxEqc2,
                EntailmentRules.CaxDw => CaxDw,
                EntailmentRules.CaxAdc => CaxAdc,
                EntailmentRules.DtDiff => DtDiff,
                EntailmentRules.DtNotType => DtNotType,
                EntailmentRules.DtRangeIntersection => DtRangeIntersection,
                EntailmentRules.ChainTransitivity => ChainTransitivity,
                EntailmentRules.TransitivityChain => TransitivityChain,
                EntailmentRules.ScmCls => ScmCls,
                EntailmentRules.ScmSco => ScmSco,
                EntailmentRules.ScmEqc1 => ScmEqc1,
                EntailmentRules.ScmEqc2 => ScmEqc2,
                EntailmentRules.ScmSpo => ScmSpo,
                EntailmentRules.ScmEqp1 => ScmEqp1,
                EntailmentRules.ScmEqp2 => ScmEqp2,
                EntailmentRules.ScmDom1 => ScmDom1,
                EntailmentRules.ScmDom2 => ScmDom2,
                EntailmentRules.ScmRng1 => ScmRng1,
                EntailmentRules.ScmRng2 => ScmRng2,
                EntailmentRules.ScmInt => ScmInt,
                EntailmentRules.ScmUni => ScmUni,
                EntailmentRules.EqRef => EqRef,
                EntailmentRules.ScmOp => ScmOp,
                EntailmentRules.ScmDp => ScmDp,
                EntailmentRules.ScmHv => ScmHv,
                EntailmentRules.ScmSvf1 => ScmSvf1,
                EntailmentRules.ScmSvf2 => ScmSvf2,
                EntailmentRules.ScmAvf1 => ScmAvf1,
                EntailmentRules.ScmAvf2 => ScmAvf2,
                EntailmentRules.InverseCharacteristicTransfer => InverseCharacteristicTransfer,
                EntailmentRules.SingletonEnumerationCharacteristic => SingletonEnumerationCharacteristic,
                EntailmentRules.ComplementOfSymmetry => ComplementOfSymmetry,
                EntailmentRules.OneOfMemberSubset => OneOfMemberSubset,
                EntailmentRules.UnionOfMemberSubset => UnionOfMemberSubset,
                EntailmentRules.UnionExcludedMiddle => UnionExcludedMiddle,
                EntailmentRules.UnionValueDichotomy => UnionValueDichotomy,
                EntailmentRules.FunctionalMaxOneUniversal => FunctionalMaxOneUniversal,
                EntailmentRules.EmptyEnumerationNothing => EmptyEnumerationNothing,
                EntailmentRules.IntersectionRangeCompletion => IntersectionRangeCompletion,
                EntailmentRules.DeMorganSubset => DeMorganSubset,
                EntailmentRules.CardinalityShorthand => CardinalityShorthand,
                EntailmentRules.SomeValuesFromWitness => SomeValuesFromWitness,
                EntailmentRules.NilStructureClash => NilStructureClash,
                EntailmentRules.ThingEnumerationClash => ThingEnumerationClash,
                EntailmentRules.MinCardinalityOneMembership => MinCardinalityOneMembership,
                EntailmentRules.TypeDomainUniversalSubsumption => TypeDomainUniversalSubsumption,
                EntailmentRules.SharedHasValuePropertyCollapse => SharedHasValuePropertyCollapse,
                EntailmentRules.DisjointRangeVacuousSubproperty => DisjointRangeVacuousSubproperty,
                EntailmentRules.DisjointRangeClash => DisjointRangeClash,
                EntailmentRules.DatatypeAliasRetype => DatatypeAliasRetype,
                EntailmentRules.FibreCardinalityCertificate => FibreCardinalityCertificate,
                EntailmentRules.DtDisjointIdentity => DtDisjointIdentity,
                _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Undefined entailment rule."),
            };
        }

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdf1Name { get; } = "rdf1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdf1Explanation { get; } = "The predicate of any statement is an rdf:Property."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs2Name { get; } = "rdfs2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs2Explanation { get; } = "A property's domain types its subjects."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs3"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs3Name { get; } = "rdfs3"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs3"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs3Explanation { get; } = "A property's range types its objects."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs5"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs5Name { get; } = "rdfs5"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs5"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs5Explanation { get; } = "Subproperty transitivity over schema statements."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs6"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs6Name { get; } = "rdfs6"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs6"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs6Explanation { get; } = "Every rdf:Property is a subproperty of itself."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs7"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs7Name { get; } = "rdfs7"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs7"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs7Explanation { get; } = "A statement holds under every superproperty."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs8"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs8Name { get; } = "rdfs8"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs8"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs8Explanation { get; } = "Every rdfs:Class is a subclass of rdfs:Resource."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs9"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs9Name { get; } = "rdfs9"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs9"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs9Explanation { get; } = "An instance of a class is an instance of every superclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs10"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs10Name { get; } = "rdfs10"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs10"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs10Explanation { get; } = "Every rdfs:Class is a subclass of itself."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs11"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs11Name { get; } = "rdfs11"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs11"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs11Explanation { get; } = "Subclass transitivity over schema statements."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs12"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs12Name { get; } = "rdfs12"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs12"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs12Explanation { get; } = "Every rdfs:ContainerMembershipProperty is a subproperty of rdfs:member."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="Rdfs13"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs13Name { get; } = "rdfs13"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="Rdfs13"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> Rdfs13Explanation { get; } = "Every rdfs:Datatype is a subclass of rdfs:Literal."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="AxiomaticTyping"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> AxiomaticTypingName { get; } = "axiomatic-typing"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="AxiomaticTyping"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> AxiomaticTypingExplanation { get; } = "The rdfs2/rdfs3 consequences of the RDF(S) axiomatic schema: vocabulary domains and ranges typing the subjects and objects of schema statements."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqSym"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqSymName { get; } = "eq-sym"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqSym"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqSymExplanation { get; } = "owl:sameAs is symmetric."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqTrans"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqTransName { get; } = "eq-trans"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqTrans"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqTransExplanation { get; } = "owl:sameAs is transitive."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqRepS"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepSName { get; } = "eq-rep-s"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqRepS"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepSExplanation { get; } = "A statement holds with a same-as subject substituted."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqRepP"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepPName { get; } = "eq-rep-p"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqRepP"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepPExplanation { get; } = "A statement holds with a same-as predicate substituted."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqRepO"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepOName { get; } = "eq-rep-o"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqRepO"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRepOExplanation { get; } = "A statement holds with a same-as object substituted."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqDiff1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqDiff1Name { get; } = "eq-diff1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqDiff1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqDiff1Explanation { get; } = "owl:sameAs and owl:differentFrom between the same pair contradict."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqDiff2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqDiff2Name { get; } = "eq-diff2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqDiff2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqDiff2Explanation { get; } = "Two same-as members of an owl:AllDifferent list contradict."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DifferentFromSymmetry"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DifferentFromSymmetryName { get; } = "different-from-symmetry"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DifferentFromSymmetry"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DifferentFromSymmetryExplanation { get; } = "Implementation completion: owl:differentFrom is materialised symmetrically for the eq-diff checks."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpDom"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpDomName { get; } = "prp-dom"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpDom"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpDomExplanation { get; } = "A property's domain types its subjects."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpRng"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpRngName { get; } = "prp-rng"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpRng"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpRngExplanation { get; } = "A property's range types its objects."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpFp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpFpName { get; } = "prp-fp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpFp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpFpExplanation { get; } = "A functional property's values for one subject equate."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpIfp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpIfpName { get; } = "prp-ifp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpIfp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpIfpExplanation { get; } = "An inverse-functional property's subjects for one value equate."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpIrp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpIrpName { get; } = "prp-irp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpIrp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpIrpExplanation { get; } = "An irreflexive property with a reflexive statement contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpSymp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSympName { get; } = "prp-symp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpSymp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSympExplanation { get; } = "A symmetric property's statements reverse."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpAsyp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpAsypName { get; } = "prp-asyp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpAsyp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpAsypExplanation { get; } = "An asymmetric property with statements both ways contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpTrp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpTrpName { get; } = "prp-trp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpTrp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpTrpExplanation { get; } = "A transitive property's statements compose."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ReflexiveInstantiation"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ReflexiveInstantiationName { get; } = "reflexive-instantiation"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ReflexiveInstantiation"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ReflexiveInstantiationExplanation { get; } = "Implementation completion: a reflexive property instantiates over the named individuals."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpSpo1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSpo1Name { get; } = "prp-spo1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpSpo1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSpo1Explanation { get; } = "A statement holds under every superproperty."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpSpo2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSpo2Name { get; } = "prp-spo2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpSpo2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpSpo2Explanation { get; } = "A property chain entails the superproperty between its endpoints."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpEqp1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpEqp1Name { get; } = "prp-eqp1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpEqp1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpEqp1Explanation { get; } = "A statement of a property holds for its equivalent."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpEqp2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpEqp2Name { get; } = "prp-eqp2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpEqp2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpEqp2Explanation { get; } = "A statement of a property holds for its equivalent, the other way."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpPdw"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpPdwName { get; } = "prp-pdw"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpPdw"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpPdwExplanation { get; } = "Disjoint properties sharing a statement contradict; disjointness is materialised symmetrically."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpAdp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpAdpName { get; } = "prp-adp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpAdp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpAdpExplanation { get; } = "Pairwise disjointness of an owl:AllDisjointProperties list, statements and contradiction both."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpInv1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpInv1Name { get; } = "prp-inv1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpInv1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpInv1Explanation { get; } = "An inverse property's statements reverse."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpInv2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpInv2Name { get; } = "prp-inv2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpInv2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpInv2Explanation { get; } = "An inverse property's statements reverse, the other way."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpKey"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpKeyName { get; } = "prp-key"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpKey"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpKeyExplanation { get; } = "Instances sharing a value for every key property equate."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="PrpNpa"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpNpaName { get; } = "prp-npa"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="PrpNpa"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> PrpNpaExplanation { get; } = "A negative property assertion with the asserted statement contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsNothing2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsNothing2Name { get; } = "cls-nothing2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsNothing2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsNothing2Explanation { get; } = "An instance of owl:Nothing contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsInt1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsInt1Name { get; } = "cls-int1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsInt1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsInt1Explanation { get; } = "An instance of every intersection member is an instance of the intersection."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsInt2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsInt2Name { get; } = "cls-int2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsInt2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsInt2Explanation { get; } = "An instance of an intersection is an instance of every member."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsUni"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsUniName { get; } = "cls-uni"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsUni"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsUniExplanation { get; } = "An instance of a union member is an instance of the union."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsCom"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsComName { get; } = "cls-com"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsCom"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsComExplanation { get; } = "An instance of a class and its complement contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsSvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsSvf1Name { get; } = "cls-svf1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsSvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsSvf1Explanation { get; } = "A value in the filler puts the subject in the some-values restriction."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsSvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsSvf2Name { get; } = "cls-svf2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsSvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsSvf2Explanation { get; } = "Any value puts the subject in a some-values-from-owl:Thing restriction."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsAvf"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsAvfName { get; } = "cls-avf"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsAvf"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsAvfExplanation { get; } = "An all-values restriction types every value of its instances."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsHv1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsHv1Name { get; } = "cls-hv1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsHv1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsHv1Explanation { get; } = "An instance of a has-value restriction carries the value."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsHv2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsHv2Name { get; } = "cls-hv2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsHv2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsHv2Explanation { get; } = "Carrying the value puts the subject in the has-value restriction."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsMaxc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxc1Name { get; } = "cls-maxc1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsMaxc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxc1Explanation { get; } = "A max-0 cardinality restriction with any edge contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsMaxc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxc2Name { get; } = "cls-maxc2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsMaxc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxc2Explanation { get; } = "A max-1 cardinality restriction equates its instances' values."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsMaxqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxqc1Name { get; } = "cls-maxqc1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsMaxqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxqc1Explanation { get; } = "A qualified max-0 restriction with a qualified edge contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsMaxqc4"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxqc4Name { get; } = "cls-maxqc4"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsMaxqc4"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsMaxqc4Explanation { get; } = "A qualified max-1 restriction equates its instances' qualified values."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ClsOo"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsOoName { get; } = "cls-oo"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ClsOo"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ClsOoExplanation { get; } = "Every member of an enumeration is an instance of it."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CaxSco"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxScoName { get; } = "cax-sco"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CaxSco"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxScoExplanation { get; } = "An instance of a subclass is an instance of the superclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CaxEqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxEqc1Name { get; } = "cax-eqc1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CaxEqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxEqc1Explanation { get; } = "An instance of a class is an instance of its equivalent."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CaxEqc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxEqc2Name { get; } = "cax-eqc2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CaxEqc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxEqc2Explanation { get; } = "An instance of a class is an instance of its equivalent, the other way."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CaxDw"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxDwName { get; } = "cax-dw"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CaxDw"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxDwExplanation { get; } = "Disjoint classes sharing an instance contradict; disjointness is materialised symmetrically."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CaxAdc"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxAdcName { get; } = "cax-adc"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CaxAdc"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CaxAdcExplanation { get; } = "Pairwise disjointness of an owl:AllDisjointClasses list, statements and contradiction both."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DtDiff"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtDiffName { get; } = "dt-diff"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DtDiff"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtDiffExplanation { get; } = "owl:sameAs between literals denoting distinct data values contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DtNotType"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtNotTypeName { get; } = "dt-not-type"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DtNotType"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtNotTypeExplanation { get; } = "A literal outside its asserted datatype's value space contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DtRangeIntersection"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtRangeIntersectionName { get; } = "dt-range-intersection"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DtRangeIntersection"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtRangeIntersectionExplanation { get; } = "Two ranges confine a property's values to the intersection of their value spaces, so every datatype-map space containing that intersection is a range too."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ChainTransitivity"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ChainTransitivityName { get; } = "chain-trans"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ChainTransitivity"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ChainTransitivityExplanation { get; } = "A self property chain states exactly transitivity, so the owl:TransitiveProperty typing materialises from the chain."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="TransitivityChain"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> TransitivityChainName { get; } = "trans-chain"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="TransitivityChain"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> TransitivityChainExplanation { get; } = "Transitivity states exactly the self property chain, so the chain structure materialises from the typing on deterministic list nodes, keeping the fixpoint idempotent."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmCls"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmClsName { get; } = "scm-cls"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmCls"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmClsExplanation { get; } = "Every declared class is its own sub- and equivalent class, below owl:Thing and above owl:Nothing."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmSco"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmScoName { get; } = "scm-sco"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmSco"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmScoExplanation { get; } = "Subclass transitivity over schema statements."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmEqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqc1Name { get; } = "scm-eqc1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmEqc1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqc1Explanation { get; } = "Equivalent classes are mutual subclasses; equivalence is materialised symmetrically."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmEqc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqc2Name { get; } = "scm-eqc2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmEqc2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqc2Explanation { get; } = "Mutual subclasses are equivalent."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmSpo"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSpoName { get; } = "scm-spo"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmSpo"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSpoExplanation { get; } = "Subproperty transitivity over schema statements."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmEqp1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqp1Name { get; } = "scm-eqp1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmEqp1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqp1Explanation { get; } = "Equivalent properties are mutual subproperties; equivalence is materialised symmetrically."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmEqp2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqp2Name { get; } = "scm-eqp2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmEqp2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmEqp2Explanation { get; } = "Mutual subproperties are equivalent."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmDom1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDom1Name { get; } = "scm-dom1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmDom1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDom1Explanation { get; } = "A domain's superclasses are domains."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmDom2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDom2Name { get; } = "scm-dom2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmDom2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDom2Explanation { get; } = "A superproperty's domains are domains."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmRng1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmRng1Name { get; } = "scm-rng1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmRng1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmRng1Explanation { get; } = "A range's superclasses are ranges."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmRng2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmRng2Name { get; } = "scm-rng2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmRng2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmRng2Explanation { get; } = "A superproperty's ranges are ranges."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmInt"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmIntName { get; } = "scm-int"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmInt"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmIntExplanation { get; } = "An intersection is a subclass of every member."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmUni"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmUniName { get; } = "scm-uni"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmUni"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmUniExplanation { get; } = "Every member is a subclass of the union."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EqRef"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRefName { get; } = "eq-ref"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EqRef"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EqRefExplanation { get; } = "Every term of a statement is owl:sameAs itself."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmOp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmOpName { get; } = "scm-op"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmOp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmOpExplanation { get; } = "A declared object property is its own sub- and equivalent property."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmDp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDpName { get; } = "scm-dp"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmDp"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmDpExplanation { get; } = "A declared datatype property is its own sub- and equivalent property."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmHv"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmHvName { get; } = "scm-hv"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmHv"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmHvExplanation { get; } = "Has-value restrictions on one value order by their properties' subsumption."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmSvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSvf1Name { get; } = "scm-svf1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmSvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSvf1Explanation { get; } = "Some-values restrictions on one property order by their fillers' subsumption."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmSvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSvf2Name { get; } = "scm-svf2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmSvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmSvf2Explanation { get; } = "Some-values restrictions on one filler order by their properties' subsumption."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmAvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmAvf1Name { get; } = "scm-avf1"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmAvf1"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmAvf1Explanation { get; } = "All-values restrictions on one property order by their fillers' subsumption."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ScmAvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmAvf2Name { get; } = "scm-avf2"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ScmAvf2"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ScmAvf2Explanation { get; } = "All-values restrictions on one filler order contravariantly: the superproperty's restriction subsumes under the subproperty's."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="InverseCharacteristicTransfer"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> InverseCharacteristicTransferName { get; } = "inverse-characteristic-transfer"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="InverseCharacteristicTransfer"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> InverseCharacteristicTransferExplanation { get; } = "The functional and inverse-functional characteristics transfer across owl:inverseOf, exchanging kinds."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="SingletonEnumerationCharacteristic"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SingletonEnumerationCharacteristicName { get; } = "singleton-enumeration-characteristic"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="SingletonEnumerationCharacteristic"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SingletonEnumerationCharacteristicExplanation { get; } = "A singleton-enumeration range makes a property functional; a singleton-enumeration domain makes it inverse functional."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ComplementOfSymmetry"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ComplementOfSymmetryName { get; } = "complement-of-symmetry"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ComplementOfSymmetry"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ComplementOfSymmetryExplanation { get; } = "owl:complementOf denotes a symmetric relation between class extensions, so the reversed statement holds."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="OneOfMemberSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> OneOfMemberSubsetName { get; } = "one-of-member-subset"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="OneOfMemberSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> OneOfMemberSubsetExplanation { get; } = "An enumeration whose member set is a subset of another enumeration's is its subclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="UnionOfMemberSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionOfMemberSubsetName { get; } = "union-of-member-subset"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="UnionOfMemberSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionOfMemberSubsetExplanation { get; } = "A union whose disjunct set is a subset of another union's is its subclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="UnionExcludedMiddle"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionExcludedMiddleName { get; } = "union-excluded-middle"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="UnionExcludedMiddle"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionExcludedMiddleExplanation { get; } = "A union holding a class and a complement of that class covers everything, so owl:Thing is its subclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="UnionValueDichotomy"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionValueDichotomyName { get; } = "union-value-dichotomy"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="UnionValueDichotomy"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> UnionValueDichotomyExplanation { get; } = "A union of a some-values-from-owl:Thing restriction and a max-0 restriction on one property covers everything, so owl:Thing is its subclass."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="FunctionalMaxOneUniversal"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> FunctionalMaxOneUniversalName { get; } = "functional-max-one-universal"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="FunctionalMaxOneUniversal"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> FunctionalMaxOneUniversalExplanation { get; } = "A functional property confines every individual to at most one value, so owl:Thing subsumes under any max-1 restriction on it."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="EmptyEnumerationNothing"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EmptyEnumerationNothingName { get; } = "empty-enumeration-nothing"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="EmptyEnumerationNothing"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> EmptyEnumerationNothingExplanation { get; } = "An enumeration over the empty list denotes the empty class, so it is a subclass of owl:Nothing."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="IntersectionRangeCompletion"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> IntersectionRangeCompletionName { get; } = "intersection-range-completion"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="IntersectionRangeCompletion"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> IntersectionRangeCompletionExplanation { get; } = "A property ranged by every member of an intersection is ranged by the intersection."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DeMorganSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DeMorganSubsetName { get; } = "de-morgan-subset"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DeMorganSubset"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DeMorganSubsetExplanation { get; } = "An intersection of complements orders against a complement of a union by De Morgan duality, one subsumption direction per disjunct-set containment."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="CardinalityShorthand"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CardinalityShorthandName { get; } = "cardinality-shorthand"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="CardinalityShorthand"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> CardinalityShorthandExplanation { get; } = "An exact cardinality is the same-bound min- and max-cardinality pair, so their intersection also carries the intersection over the single exact restriction."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="SomeValuesFromWitness"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SomeValuesFromWitnessName { get; } = "some-values-from-witness"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="SomeValuesFromWitness"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SomeValuesFromWitnessExplanation { get; } = "Every member of a some-values-from restriction has a value for the property inside the filler, carried by a fresh deterministic witness per member, restriction, property, and filler."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="NilStructureClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> NilStructureClashName { get; } = "nil-structure-clash"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="NilStructureClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> NilStructureClashExplanation { get; } = "rdf:nil is the empty collection and carries no rdf:first or rdf:rest edge, so either edge on it contradicts."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="ThingEnumerationClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ThingEnumerationClashName { get; } = "thing-enumeration-clash"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="ThingEnumerationClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> ThingEnumerationClashExplanation { get; } = "An enumeration of owl:Thing confines the infinite RDF-Based universe to a finite sequence, so the axiom contradicts at any arity."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="MinCardinalityOneMembership"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> MinCardinalityOneMembershipName { get; } = "min-cardinality-one-membership"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="MinCardinalityOneMembership"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> MinCardinalityOneMembershipExplanation { get; } = "One asserted value places the subject in a min-cardinality-1 restriction on the property, because the restriction conditions determine the extension exactly."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="TypeDomainUniversalSubsumption"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> TypeDomainUniversalSubsumptionName { get; } = "type-domain-universal-subsumption"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="TypeDomainUniversalSubsumption"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> TypeDomainUniversalSubsumptionExplanation { get; } = "A declared domain of rdf:type subsumes every class, because ICEXT is the rdf:type slice and every class member is an rdf:type subject."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="SharedHasValuePropertyCollapse"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SharedHasValuePropertyCollapseName { get; } = "shared-hasvalue-property-collapse"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="SharedHasValuePropertyCollapse"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> SharedHasValuePropertyCollapseExplanation { get; } = "Two functional properties sharing one has-value node as domain and onProperty target have the same extension, so each subsumes the other."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DisjointRangeVacuousSubproperty"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DisjointRangeVacuousSubpropertyName { get; } = "disjoint-range-vacuous-subproperty"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DisjointRangeVacuousSubproperty"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DisjointRangeVacuousSubpropertyExplanation { get; } = "A property ranged by two datatypes with disjoint value spaces has the empty extension, which is a subproperty of every property."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DisjointRangeClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DisjointRangeClashName { get; } = "disjoint-range-clash"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DisjointRangeClash"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DisjointRangeClashExplanation { get; } = "A statement of a property ranged by two datatypes with disjoint value spaces contradicts, because its object would denote a value in both spaces."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DatatypeAliasRetype"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DatatypeAliasRetypeName { get; } = "datatype-alias-retype"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DatatypeAliasRetype"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DatatypeAliasRetypeExplanation { get; } = "A literal typed by an alias IRI held owl:sameAs a datatype-map member is owl:sameAs its retype onto the member, because literal denotation runs through the datatype the type IRI denotes."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="FibreCardinalityCertificate"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> FibreCardinalityCertificateName { get; } = "fibre-cardinality-certificate"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="FibreCardinalityCertificate"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> FibreCardinalityCertificateExplanation { get; } = "A singleton enumeration anchors a proven class-extension count that inverse cardinality equivalences and functional fibre products compose, and the anchored read-back pins the counted bound owl:sameAs the minted digit literal of the proven count."u8.ToArray();

        /// <summary>The canonical <c>u8</c> name for <see cref="DtDisjointIdentity"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtDisjointIdentityName { get; } = "dt-disjoint-identity"u8.ToArray();

        /// <summary>The cold WHY-text for <see cref="DtDisjointIdentity"/>, interned once.</summary>
        private static ReadOnlyMemory<byte> DtDisjointIdentityExplanation { get; } = "An owl:sameAs between two datatypes with known-disjoint value spaces contradicts, because the datatype map denotes them as distinct resources in every interpretation."u8.ToArray();

        /// <summary>Produces every entailment-rule registration, in code order, for a composition root to feed the ladder.</summary>
        /// <returns>The one hundred nine entailment-rule registrations.</returns>
        public static IReadOnlyList<EpistemicReasonRegistration> CreateEntailmentRuleRegistrations()
        {
            return
            [
                Register(Rdf1, Rdf1Name, Rdf1Explanation),
                Register(Rdfs2, Rdfs2Name, Rdfs2Explanation),
                Register(Rdfs3, Rdfs3Name, Rdfs3Explanation),
                Register(Rdfs5, Rdfs5Name, Rdfs5Explanation),
                Register(Rdfs6, Rdfs6Name, Rdfs6Explanation),
                Register(Rdfs7, Rdfs7Name, Rdfs7Explanation),
                Register(Rdfs8, Rdfs8Name, Rdfs8Explanation),
                Register(Rdfs9, Rdfs9Name, Rdfs9Explanation),
                Register(Rdfs10, Rdfs10Name, Rdfs10Explanation),
                Register(Rdfs11, Rdfs11Name, Rdfs11Explanation),
                Register(Rdfs12, Rdfs12Name, Rdfs12Explanation),
                Register(Rdfs13, Rdfs13Name, Rdfs13Explanation),
                Register(AxiomaticTyping, AxiomaticTypingName, AxiomaticTypingExplanation),
                Register(EqSym, EqSymName, EqSymExplanation),
                Register(EqTrans, EqTransName, EqTransExplanation),
                Register(EqRepS, EqRepSName, EqRepSExplanation),
                Register(EqRepP, EqRepPName, EqRepPExplanation),
                Register(EqRepO, EqRepOName, EqRepOExplanation),
                Register(EqDiff1, EqDiff1Name, EqDiff1Explanation),
                Register(EqDiff2, EqDiff2Name, EqDiff2Explanation),
                Register(DifferentFromSymmetry, DifferentFromSymmetryName, DifferentFromSymmetryExplanation),
                Register(PrpDom, PrpDomName, PrpDomExplanation),
                Register(PrpRng, PrpRngName, PrpRngExplanation),
                Register(PrpFp, PrpFpName, PrpFpExplanation),
                Register(PrpIfp, PrpIfpName, PrpIfpExplanation),
                Register(PrpIrp, PrpIrpName, PrpIrpExplanation),
                Register(PrpSymp, PrpSympName, PrpSympExplanation),
                Register(PrpAsyp, PrpAsypName, PrpAsypExplanation),
                Register(PrpTrp, PrpTrpName, PrpTrpExplanation),
                Register(ReflexiveInstantiation, ReflexiveInstantiationName, ReflexiveInstantiationExplanation),
                Register(PrpSpo1, PrpSpo1Name, PrpSpo1Explanation),
                Register(PrpSpo2, PrpSpo2Name, PrpSpo2Explanation),
                Register(PrpEqp1, PrpEqp1Name, PrpEqp1Explanation),
                Register(PrpEqp2, PrpEqp2Name, PrpEqp2Explanation),
                Register(PrpPdw, PrpPdwName, PrpPdwExplanation),
                Register(PrpAdp, PrpAdpName, PrpAdpExplanation),
                Register(PrpInv1, PrpInv1Name, PrpInv1Explanation),
                Register(PrpInv2, PrpInv2Name, PrpInv2Explanation),
                Register(PrpKey, PrpKeyName, PrpKeyExplanation),
                Register(PrpNpa, PrpNpaName, PrpNpaExplanation),
                Register(ClsNothing2, ClsNothing2Name, ClsNothing2Explanation),
                Register(ClsInt1, ClsInt1Name, ClsInt1Explanation),
                Register(ClsInt2, ClsInt2Name, ClsInt2Explanation),
                Register(ClsUni, ClsUniName, ClsUniExplanation),
                Register(ClsCom, ClsComName, ClsComExplanation),
                Register(ClsSvf1, ClsSvf1Name, ClsSvf1Explanation),
                Register(ClsSvf2, ClsSvf2Name, ClsSvf2Explanation),
                Register(ClsAvf, ClsAvfName, ClsAvfExplanation),
                Register(ClsHv1, ClsHv1Name, ClsHv1Explanation),
                Register(ClsHv2, ClsHv2Name, ClsHv2Explanation),
                Register(ClsMaxc1, ClsMaxc1Name, ClsMaxc1Explanation),
                Register(ClsMaxc2, ClsMaxc2Name, ClsMaxc2Explanation),
                Register(ClsMaxqc1, ClsMaxqc1Name, ClsMaxqc1Explanation),
                Register(ClsMaxqc4, ClsMaxqc4Name, ClsMaxqc4Explanation),
                Register(ClsOo, ClsOoName, ClsOoExplanation),
                Register(CaxSco, CaxScoName, CaxScoExplanation),
                Register(CaxEqc1, CaxEqc1Name, CaxEqc1Explanation),
                Register(CaxEqc2, CaxEqc2Name, CaxEqc2Explanation),
                Register(CaxDw, CaxDwName, CaxDwExplanation),
                Register(CaxAdc, CaxAdcName, CaxAdcExplanation),
                Register(DtDiff, DtDiffName, DtDiffExplanation),
                Register(DtNotType, DtNotTypeName, DtNotTypeExplanation),
                Register(DtRangeIntersection, DtRangeIntersectionName, DtRangeIntersectionExplanation),
                Register(ChainTransitivity, ChainTransitivityName, ChainTransitivityExplanation),
                Register(TransitivityChain, TransitivityChainName, TransitivityChainExplanation),
                Register(ScmCls, ScmClsName, ScmClsExplanation),
                Register(ScmSco, ScmScoName, ScmScoExplanation),
                Register(ScmEqc1, ScmEqc1Name, ScmEqc1Explanation),
                Register(ScmEqc2, ScmEqc2Name, ScmEqc2Explanation),
                Register(ScmSpo, ScmSpoName, ScmSpoExplanation),
                Register(ScmEqp1, ScmEqp1Name, ScmEqp1Explanation),
                Register(ScmEqp2, ScmEqp2Name, ScmEqp2Explanation),
                Register(ScmDom1, ScmDom1Name, ScmDom1Explanation),
                Register(ScmDom2, ScmDom2Name, ScmDom2Explanation),
                Register(ScmRng1, ScmRng1Name, ScmRng1Explanation),
                Register(ScmRng2, ScmRng2Name, ScmRng2Explanation),
                Register(ScmInt, ScmIntName, ScmIntExplanation),
                Register(ScmUni, ScmUniName, ScmUniExplanation),
                Register(EqRef, EqRefName, EqRefExplanation),
                Register(ScmOp, ScmOpName, ScmOpExplanation),
                Register(ScmDp, ScmDpName, ScmDpExplanation),
                Register(ScmHv, ScmHvName, ScmHvExplanation),
                Register(ScmSvf1, ScmSvf1Name, ScmSvf1Explanation),
                Register(ScmSvf2, ScmSvf2Name, ScmSvf2Explanation),
                Register(ScmAvf1, ScmAvf1Name, ScmAvf1Explanation),
                Register(ScmAvf2, ScmAvf2Name, ScmAvf2Explanation),
                Register(InverseCharacteristicTransfer, InverseCharacteristicTransferName, InverseCharacteristicTransferExplanation),
                Register(SingletonEnumerationCharacteristic, SingletonEnumerationCharacteristicName, SingletonEnumerationCharacteristicExplanation),
                Register(ComplementOfSymmetry, ComplementOfSymmetryName, ComplementOfSymmetryExplanation),
                Register(OneOfMemberSubset, OneOfMemberSubsetName, OneOfMemberSubsetExplanation),
                Register(UnionOfMemberSubset, UnionOfMemberSubsetName, UnionOfMemberSubsetExplanation),
                Register(UnionExcludedMiddle, UnionExcludedMiddleName, UnionExcludedMiddleExplanation),
                Register(UnionValueDichotomy, UnionValueDichotomyName, UnionValueDichotomyExplanation),
                Register(FunctionalMaxOneUniversal, FunctionalMaxOneUniversalName, FunctionalMaxOneUniversalExplanation),
                Register(EmptyEnumerationNothing, EmptyEnumerationNothingName, EmptyEnumerationNothingExplanation),
                Register(IntersectionRangeCompletion, IntersectionRangeCompletionName, IntersectionRangeCompletionExplanation),
                Register(DeMorganSubset, DeMorganSubsetName, DeMorganSubsetExplanation),
                Register(CardinalityShorthand, CardinalityShorthandName, CardinalityShorthandExplanation),
                Register(SomeValuesFromWitness, SomeValuesFromWitnessName, SomeValuesFromWitnessExplanation),
                Register(NilStructureClash, NilStructureClashName, NilStructureClashExplanation),
                Register(ThingEnumerationClash, ThingEnumerationClashName, ThingEnumerationClashExplanation),
                Register(MinCardinalityOneMembership, MinCardinalityOneMembershipName, MinCardinalityOneMembershipExplanation),
                Register(TypeDomainUniversalSubsumption, TypeDomainUniversalSubsumptionName, TypeDomainUniversalSubsumptionExplanation),
                Register(SharedHasValuePropertyCollapse, SharedHasValuePropertyCollapseName, SharedHasValuePropertyCollapseExplanation),
                Register(DisjointRangeVacuousSubproperty, DisjointRangeVacuousSubpropertyName, DisjointRangeVacuousSubpropertyExplanation),
                Register(DisjointRangeClash, DisjointRangeClashName, DisjointRangeClashExplanation),
                Register(DatatypeAliasRetype, DatatypeAliasRetypeName, DatatypeAliasRetypeExplanation),
                Register(FibreCardinalityCertificate, FibreCardinalityCertificateName, FibreCardinalityCertificateExplanation),
                Register(DtDisjointIdentity, DtDisjointIdentityName, DtDisjointIdentityExplanation)
            ];
        }

        /// <summary>Binds a code, its canonical name, and its explanation into a deferred-coverage registration under <see cref="Family"/>.</summary>
        /// <param name="code">The code being registered.</param>
        /// <param name="canonicalName">The canonical name as <c>u8</c> bytes.</param>
        /// <param name="explanation">The cold WHY-text as <c>u8</c> bytes.</param>
        /// <returns>The registration.</returns>
        private static EpistemicReasonRegistration Register(EpistemicReasonCode code, ReadOnlyMemory<byte> canonicalName, ReadOnlyMemory<byte> explanation)
        {
            return new EpistemicReasonRegistration(Family, code, canonicalName, explanation, EpistemicProjectionCoverage.Deferred);
        }
    }
}
