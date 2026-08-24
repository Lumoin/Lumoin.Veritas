using System;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Owl.Contexts;

internal static partial class ContextHabitatRecognizer
{
    /// <summary>
    /// The habitat probe registry: ONE ordered table of eleven rows whose ARRAY
    /// POSITION is the answer order. <see cref="Classify"/> walks it top to
    /// bottom and the first row the census admits whose match step does not
    /// decline answers for the module; no other artifact in the tree states that
    /// order. Each row declares its own labels, its admission on each of the two
    /// census paths, the construct kinds its signal may ride, the decider faces
    /// its family owns, and the match step itself, and a row's documentation
    /// speaks of that row alone.
    /// </summary>
    private static readonly HabitatProbeEntry[] probeOrder =
    [
        //Shape E, the role-free enumeration algebra. Answers EnumerationAlgebra; no alternate label.
        //Never admitted on the nominal-free path, always admitted on the nominal path. Its declared
        //carrier is a nominal: the admission demands at least one one-of present, which is the
        //construct kind the census's nominal mention covers, so the nominal-free path carries no
        //module this row could claim. Faces: the certifying enumeration-algebra face and the two
        //pair-composition faces.
        new HabitatProbeEntry(
            EnumerationHabitatClass.EnumerationAlgebra,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Never,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Nominal,
            EnumerationDeciderFaces.Certifying | EnumerationDeciderFaces.EnumerationPairClash | EnumerationDeciderFaces.EnumerationPairCertify,
            MatchEnumerationAlgebra),

        //Shape N, the nominal-funnel counting shape. Answers NominalCounting, or the alternate label
        //Mixed where an enumeration-algebra one-of axiom stands beside the funnel and cap signals.
        //Never admitted on the nominal-free path, always admitted on the nominal path. Its declared
        //carriers are a nominal and an object count: both the funnel and the cap are anchored on a
        //one-of, which the census's nominal mention covers, so the nominal-free path carries no module
        //this row could claim, and the cap the signal demands is checked by the match step itself
        //rather than by the census's counting mention, which is why the nominal path admits the row
        //whatever that mention reads. Face: the clash-only nominal-counting face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.NominalCounting,
            EnumerationHabitatClass.Mixed,
            HabitatPathAdmission.Never,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Nominal | HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.ClashOnly,
            MatchNominalFunnelAndCap),

        //Shape K, the branching modal-gadget module. Answers ModalGadgetTree; no alternate label.
        //Always admitted on both paths. Its declared carriers are an object count and a data count:
        //the census's counting mention covers object number restrictions only, while this habitat's
        //gadget layer may be carried by data cardinality restrictions alone, so the row cannot sit
        //behind that mention on either path. Faces: the monotone composition clash face and the
        //minted-skolem-tree certify face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.ModalGadgetTree,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Always,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.ObjectCounting | HabitatSignalCarriers.DataCounting,
            EnumerationDeciderFaces.ModalGadgetClash | EnumerationDeciderFaces.ModalGadgetCertify,
            MatchModalGadgetTree),

        //Shape G, the nominal-free boolean-cardinality gadget. Answers BooleanCardinalityGadget; no
        //alternate label. Admitted on the nominal-free path only where the census mentions counting,
        //never admitted on the nominal path, the habitat being nominal-free. Its declared carrier is
        //an object count — the object cardinality gadget its equivalence reads — which is the
        //construct kind the census's counting mention covers, and the nominal-free path admits the row
        //only where that mention is present. Faces: the two bounded assignment-evaluation gadget
        //faces.
        new HabitatProbeEntry(
            EnumerationHabitatClass.BooleanCardinalityGadget,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.WhenCounting,
            HabitatPathAdmission.Never,
            HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.GadgetClash | EnumerationDeciderFaces.GadgetCertify,
            MatchBooleanCardinalityGadget),

        //Shape P, the nominal-free partition-counting template. Answers PartitionCounting; no
        //alternate label. Admitted on the nominal-free path only where the census mentions counting,
        //never admitted on the nominal path, the habitat being nominal-free. Its declared carrier is
        //an object count — the unqualified max-cardinality conjunct the template demands — which is
        //the construct kind the census's counting mention covers, and the nominal-free path admits the
        //row only where that mention is present. Faces: the two closed-form partition faces.
        new HabitatProbeEntry(
            EnumerationHabitatClass.PartitionCounting,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.WhenCounting,
            HabitatPathAdmission.Never,
            HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.PartitionClash | EnumerationDeciderFaces.PartitionCertify,
            MatchPartitionCounting),

        //Shape S, the spy-point domain-bound encoding. Answers SpyPointDomainBound; no alternate
        //label. Never admitted on the nominal-free path, always admitted on the nominal path. Its
        //declared carriers are a nominal and an object count: the signal demands an existential into a
        //one-of, which the census's nominal mention covers, so the nominal-free path carries no module
        //this row could claim, and the told cap is checked by the match step itself rather than by the
        //census's counting mention. Face: the closed-form spy-point clash face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.SpyPointDomainBound,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Never,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Nominal | HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.SpyPointClash,
            MatchSpyPointDomainBound),

        //Shape B, the bijection-chain cardinality arithmetic. Answers BijectionChainArithmetic; no
        //alternate label. Admitted on the nominal-free path only where the census mentions counting,
        //always admitted on the nominal path, the habitat being nominal-bearing and nominal-free
        //alike. Its declared carrier is an object count — the told functional or inverse-functional
        //characteristic the signal demands — which is a construct kind the census's counting mention
        //covers, and the nominal-free path admits the row only where that mention is present. KNOWN
        //UNREACHABLE CORNER: a bijection chain told without a cardinality restriction and without a
        //functional or inverse-functional characteristic — which the row's own characteristic
        //requirement already excludes — sets neither census bit, and the nominal-free path does not
        //admit the row without a counting mention, so such a module never reaches the match step.
        //Faces: the monotone propagation clash face and the whole-module two-route certificate face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.BijectionChainArithmetic,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.WhenCounting,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.BijectionChainClash | EnumerationDeciderFaces.BijectionChainCertify,
            MatchBijectionChainArithmetic),

        //Shape R, the restriction-rich ground ontology. Answers RestrictionRichGround; no alternate
        //label. Admitted on the nominal-free path only where the census mentions counting, always
        //admitted on the nominal path. Its declared carriers are a nominal and an object count: the
        //obligation-position restrictions the signal counts include told has-value pins, which the
        //census's nominal mention covers, and told cardinality restrictions, which its counting
        //mention covers, and the nominal-free path admits the row only where that counting mention is
        //present. KNOWN UNREACHABLE CORNER: a module clearing the obligation threshold on
        //allValuesFrom restrictions ALONE — no value pin, no cardinality, no nominal anywhere — sets
        //neither census bit, and the nominal-free path does not admit the row without a counting
        //mention, so such a module never reaches the match step even though its obligations are
        //squarely repairable. Faces: the monotone told-only ground clash face and the whole-module
        //repaired-described-model certify face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.RestrictionRichGround,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.WhenCounting,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Nominal | HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.RepairingGroundClash | EnumerationDeciderFaces.RepairingCertify,
            MatchRestrictionRichGround),

        //Shape W, the told-ground witness encoding. Answers ToldGroundWitness; no alternate label.
        //Admitted on the nominal-free path only where the census mentions counting, always admitted on
        //the nominal path. Its declared carrier is a told inverse-role pairing, a construct the survey
        //scans but does not pass to the walk, so none of the row's three told ingredients sets either
        //census bit and the nominal-free path admits the row only where some other construct in the
        //module carries the counting mention. KNOWN UNREACHABLE CORNER: a told-ground witness module
        //without nominals and without a cardinality mention sets neither census bit, and the
        //nominal-free path does not admit the row without a counting mention, so such a module never
        //reaches the match step. Faces: the monotone ground-membership clash face and the whole-module
        //described-model certify face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.ToldGroundWitness,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.WhenCounting,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Inverse,
            EnumerationDeciderFaces.ToldGroundWitnessClash | EnumerationDeciderFaces.ToldGroundWitnessCertify,
            MatchToldGroundWitness),

        //Shape M, the bounded skolem-expansion modal module. Answers ModalRoleExpansion; no alternate
        //label. Always admitted on both paths. Its declared carriers are an object count and a data
        //count: the census's counting mention covers object number restrictions only, while this
        //habitat's clash template may be carried by data cardinality restrictions alone, so the row
        //cannot sit behind that mention on either path. Face: the clash-only modal role-expansion
        //face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.ModalRoleExpansion,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Always,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.ObjectCounting | HabitatSignalCarriers.DataCounting,
            EnumerationDeciderFaces.ModalExpansionClash,
            MatchModalRoleExpansion),

        //Shape D, the diagonal-pinned role module. Answers NominalPinnedRole; no alternate label.
        //Never admitted on the nominal-free path, always admitted on the nominal path. Its declared
        //carriers are a nominal and an object count: the signal demands a range resolving to a
        //one-of, which the census's nominal mention covers, so the nominal-free path carries no
        //module this row could claim, and the told inverse-functional characteristic the signal
        //demands is a counting-mention carrier checked by the match step itself rather than by the
        //census's counting mention, which is why the nominal path admits the row whatever that
        //mention reads. Face: the clash-only nominal-pinned-role face.
        new HabitatProbeEntry(
            EnumerationHabitatClass.NominalPinnedRole,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Never,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.Nominal | HabitatSignalCarriers.ObjectCounting,
            EnumerationDeciderFaces.NominalPinnedRoleClash,
            MatchNominalPinnedRole),
    ];

    /// <summary>
    /// The union of every registered row's declared decider faces, folded once
    /// at type initialisation. The field is declared textually after
    /// <see cref="probeOrder"/> in this same part, so the table is populated
    /// before the fold reads it.
    /// </summary>
    private static readonly EnumerationDeciderFaces everyFaceLit = FoldFaces();

    /// <summary>
    /// Folds every registered row's <see cref="HabitatProbeEntry.Faces"/> into
    /// one selection.
    /// </summary>
    /// <returns>The union of every registered row's declared faces.</returns>
    private static EnumerationDeciderFaces FoldFaces()
    {
        EnumerationDeciderFaces faces = EnumerationDeciderFaces.None;
        ReadOnlySpan<HabitatProbeEntry> rows = ProbeOrder;
        for(int i = 0; i < rows.Length; i++)
        {
            faces |= rows[i].Faces;
        }

        return faces;
    }

    /// <summary>The production every-face-lit decider selection: every face any registered row's family owns. A family that registers a row lights its faces by construction, and a face no row owns is never lit.</summary>
    internal static EnumerationDeciderFaces EveryFaceLit => everyFaceLit;

    /// <summary>The registry read as a span — the rows in answer order, iterated by index with no enumerator, no boxing and no per-call allocation.</summary>
    internal static ReadOnlySpan<HabitatProbeEntry> ProbeOrder => probeOrder;

    /// <summary>
    /// Classifies the module's habitat from axiom shapes by walking
    /// <see cref="ProbeOrder"/> once, top to bottom: the FIRST row the census
    /// admits whose match step does not decline answers, and a module no
    /// admitted row claims is none. Admission is total over the four census
    /// states — every row answers admitted or not for both counting states on
    /// both paths — so the walk has exactly one loop exit and one terminal and
    /// no module leaves it through an unnamed one. The walk is syntactic,
    /// side-effect-free and zero-allocation on the none path: the table is built
    /// once at type initialisation, the span iterates by index and each row binds
    /// by reference without a copy. Each row's own conditions, admission,
    /// carriers and faces are documented at the row.
    /// </summary>
    /// <param name="module">The module to classify.</param>
    /// <param name="mentionsNominals">The survey census's nominal-mention bit, selecting which admission column each row is read on.</param>
    /// <param name="mentionsCounting">The survey census's counting-mention bit, read by a row whose selected column is counting-gated.</param>
    /// <returns>The habitat class.</returns>
    public static EnumerationHabitatClass Classify(ReasoningModule module, bool mentionsNominals, bool mentionsCounting)
    {
        ReadOnlySpan<HabitatProbeEntry> rows = ProbeOrder;
        for(int i = 0; i < rows.Length; i++)
        {
            ref readonly HabitatProbeEntry row = ref rows[i];
            if(!row.Admits(mentionsNominals, mentionsCounting))
            {
                continue;
            }

            EnumerationHabitatClass label = row.Match(module);
            if(label != EnumerationHabitatClass.None)
            {
                return label;
            }
        }

        return EnumerationHabitatClass.None;
    }
}
