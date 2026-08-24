using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>Receives one CHARGED Pred offer — the landing predecessor, the combination's premise clause ids, and the gate outcome — so an attached recognizer can classify the offer flood by the provenance of its premises. The premise span is the odometer's own scratch buffer, valid for the duration of the call only: a recognizer that needs the ids past the call copies them. A budget-refused attempt reaches no offer and therefore no probe, so the probe's denominators close against the Pred offer counter. Never attached in production.</summary>
/// <param name="predecessor">The context the conclusion was offered to — the premises' home, whose clause face resolves each id.</param>
/// <param name="premiseIds">The combination's premise clause ids in <paramref name="predecessor"/>; empty for the zero-slot degenerate run.</param>
/// <param name="outcome">The gate that resolved the offer.</param>
internal delegate void PredOfferProbeDelegate(Context predecessor, ReadOnlySpan<int> premiseIds, ClauseOfferOutcome outcome);

/// <summary>
/// The one registry of record-only observation slots a saturation run exposes:
/// every recognizer attaches through a typed slot here and observes, never
/// decides. Record-only is structural rather than conventional — a slot's
/// delegate returns nothing, receives no mutable engine surface, and the
/// registry holds no engine reference, so no attachment can alter a derivation.
/// A <see langword="null"/> slot is the zero-cost default: the engine pays one
/// property read and one null check per observation point.
/// </summary>
/// <remarks>
/// The dark-by-default split is deliberate. A registry recognizer is armed by a
/// measurement caller and costs a set membership probe per premise, so it stays
/// unattached in production; the always-on statistics counters are O(1)
/// increments inside charge switches the run already pays for, and surface as
/// ordinary statistics columns instead.
/// </remarks>
internal sealed class SaturationRecognizerRegistry
{
    /// <summary>The per-Pred-offer observation slot, or <see langword="null"/> when no recognizer is attached.</summary>
    public PredOfferProbeDelegate? PredOfferProbe { get; set; }
}

/// <summary>
/// The provenance class a Pred combination's premises fall into against the
/// n-zero r-Pred broadcast population: the four-way partition of a combination
/// by how many of its premises are broadcast images.
/// </summary>
internal enum BroadcastProvenanceClass
{
    /// <summary>Every premise is a broadcast image, and at least one premise exists.</summary>
    AllBroadcast,

    /// <summary>At least one premise is a broadcast image and at least one is not.</summary>
    Mixed,

    /// <summary>No premise is a broadcast image, and at least one premise exists.</summary>
    NonBroadcast,

    /// <summary>The combination carries no premise at all — the zero-slot degenerate run.</summary>
    EmptyPremise,
}

/// <summary>
/// The record-only recognizer classifying each charged Pred offer by the
/// broadcast provenance of its premises: it holds the engine's live broadcast
/// image view, catches up to the view's growth by a count watermark at each
/// observation, and tests each premise for REFERENCE membership in the
/// accumulated image set. Per class it records an offer count and an
/// exact-duplicate count, so the broadcast share of the DUPLICATE flood is
/// readable beside the broadcast share of offers.
/// </summary>
/// <remarks>
/// Reference identity is the discriminating test, and it is a CONDITIONAL
/// witness. A broadcast image is built once and handed to every ordinary
/// context unchanged, and the insertion path preserves the reference whenever
/// head normalization takes its no-rebuild path — a head of pure concept and
/// role atoms, or one whose equality and inequality literals are already
/// canonically oriented at that landing. The single case the test does not
/// cover is an equality-bearing broadcast head that a particular landing
/// reorients: the insertion rebuilds the clause, the recognizer sees a
/// non-member, and the combination is classified as carrying one premise fewer
/// from the broadcast stratum than it truly does. The error is therefore
/// ONE-SIDED — this recognizer can UNDERCOUNT broadcast provenance, never
/// overcount it — and a reading taken over atom-only broadcast heads carries no
/// error at all.
/// </remarks>
internal sealed class RootBroadcastProvenanceRecognizer
{
    /// <summary>The engine's live broadcast image view — a growing list the recognizer never mutates.</summary>
    private IReadOnlyList<DlClause> BroadcastImages { get; }

    /// <summary>The broadcast images seen so far, compared by REFERENCE so a rebuilt image is deliberately not a member.</summary>
    private HashSet<DlClause> BroadcastImageSet { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The count of <see cref="BroadcastImages"/> already folded into <see cref="BroadcastImageSet"/> — the catch-up watermark, which makes the fold amortized O(1) per broadcast.</summary>
    private int Watermark { get; set; }

    /// <summary>The charged Pred offers whose every premise was a broadcast image.</summary>
    public long AllBroadcastOffers { get; private set; }

    /// <summary>The all-broadcast offers the insertion gate absorbed as exact duplicates.</summary>
    public long AllBroadcastDuplicateHits { get; private set; }

    /// <summary>The charged Pred offers mixing at least one broadcast premise with at least one non-broadcast premise.</summary>
    public long MixedOffers { get; private set; }

    /// <summary>The mixed offers the insertion gate absorbed as exact duplicates.</summary>
    public long MixedDuplicateHits { get; private set; }

    /// <summary>The charged Pred offers carrying premises, none of them a broadcast image.</summary>
    public long NonBroadcastOffers { get; private set; }

    /// <summary>The non-broadcast offers the insertion gate absorbed as exact duplicates.</summary>
    public long NonBroadcastDuplicateHits { get; private set; }

    /// <summary>The charged Pred offers carrying no premise — the zero-slot degenerate run's offers.</summary>
    public long EmptyPremiseOffers { get; private set; }

    /// <summary>The empty-premise offers the insertion gate absorbed as exact duplicates.</summary>
    public long EmptyPremiseDuplicateHits { get; private set; }

    /// <summary>The premise slots probed across every observed offer — the denominator of the per-premise match rate.</summary>
    public long PremisesProbed { get; private set; }

    /// <summary>The probed premise slots that resolved to a broadcast image by reference.</summary>
    public long PremisesMatched { get; private set; }

    /// <summary>Builds a recognizer over an engine's broadcast image view.</summary>
    /// <param name="broadcastImages">The engine's live broadcast image view.</param>
    /// <exception cref="ArgumentNullException"><paramref name="broadcastImages"/> is <see langword="null"/>.</exception>
    public RootBroadcastProvenanceRecognizer(IReadOnlyList<DlClause> broadcastImages)
    {
        ArgumentNullException.ThrowIfNull(broadcastImages);
        BroadcastImages = broadcastImages;
    }

    /// <summary>Observes one charged Pred offer: catches the image set up to the view, probes each premise for reference membership, and charges the combination's class and its duplicate share. The method group a caller attaches to the registry's Pred slot.</summary>
    /// <param name="predecessor">The context the conclusion was offered to.</param>
    /// <param name="premiseIds">The combination's premise clause ids.</param>
    /// <param name="outcome">The gate that resolved the offer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="predecessor"/> is <see langword="null"/>.</exception>
    public void Observe(Context predecessor, ReadOnlySpan<int> premiseIds, ClauseOfferOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        CatchUpToView();

        int matched = 0;
        for(int i = 0; i < premiseIds.Length; i++)
        {
            PremisesProbed++;
            if(BroadcastImageSet.Contains(predecessor.At(premiseIds[i])))
            {
                matched++;
                PremisesMatched++;
            }
        }

        bool duplicate = outcome == ClauseOfferOutcome.ExactDuplicate;
        switch(Classify(premiseIds.Length, matched))
        {
            case(BroadcastProvenanceClass.AllBroadcast):
            {
                AllBroadcastOffers++;
                if(duplicate)
                {
                    AllBroadcastDuplicateHits++;
                }

                break;
            }
            case(BroadcastProvenanceClass.Mixed):
            {
                MixedOffers++;
                if(duplicate)
                {
                    MixedDuplicateHits++;
                }

                break;
            }
            case(BroadcastProvenanceClass.NonBroadcast):
            {
                NonBroadcastOffers++;
                if(duplicate)
                {
                    NonBroadcastDuplicateHits++;
                }

                break;
            }
            default:
            {
                EmptyPremiseOffers++;
                if(duplicate)
                {
                    EmptyPremiseDuplicateHits++;
                }

                break;
            }
        }
    }

    /// <summary>Folds every image the view gained since the last observation into the reference set, advancing the watermark.</summary>
    private void CatchUpToView()
    {
        while(Watermark < BroadcastImages.Count)
        {
            BroadcastImageSet.Add(BroadcastImages[Watermark]);
            Watermark++;
        }
    }

    /// <summary>Classifies one combination from its premise count and its broadcast match count.</summary>
    /// <param name="premiseCount">The combination's premise count.</param>
    /// <param name="matchedCount">The premises that resolved to a broadcast image.</param>
    /// <returns>The provenance class.</returns>
    private static BroadcastProvenanceClass Classify(int premiseCount, int matchedCount)
    {
        return premiseCount switch
        {
            0 => BroadcastProvenanceClass.EmptyPremise,
            _ when matchedCount == premiseCount => BroadcastProvenanceClass.AllBroadcast,
            _ when matchedCount == 0 => BroadcastProvenanceClass.NonBroadcast,
            _ => BroadcastProvenanceClass.Mixed,
        };
    }
}
