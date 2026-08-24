using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// ONE habitat probe registration: the row's labels, its admission on each of
/// the two census paths, the construct kinds its signal may ride, the decider
/// faces its family owns, and the match step the walk invokes. The row carries
/// no rank of its own — its POSITION in
/// <see cref="ContextHabitatRecognizer.ProbeOrder"/> is the answer order.
/// </summary>
internal readonly struct HabitatProbeEntry
{
    /// <summary>The habitat label the row answers where its signal matches.</summary>
    public EnumerationHabitatClass Label { get; }

    /// <summary>The second label the row's match step may answer where the row's signal admits two; <see cref="EnumerationHabitatClass.None"/> on a single-label row.</summary>
    public EnumerationHabitatClass AlternateLabel { get; }

    /// <summary>The row's admission on the nominal-free path — the path a module whose census carries no nominal mention takes.</summary>
    public HabitatPathAdmission OnNominalFree { get; }

    /// <summary>The row's admission on the nominal path — the path a module whose census carries a nominal mention takes.</summary>
    public HabitatPathAdmission OnNominal { get; }

    /// <summary>The construct kinds the row's own census signal may ride.</summary>
    public HabitatSignalCarriers Carriers { get; }

    /// <summary>The decider faces the row's family owns — the bits the production every-face-lit selection folds from.</summary>
    public EnumerationDeciderFaces Faces { get; }

    /// <summary>The row's match step, answering the row's label, its alternate label, or none.</summary>
    public HabitatProbeDelegate Match { get; }

    /// <summary>Initialises one habitat probe registration.</summary>
    /// <param name="label">The habitat label the row answers.</param>
    /// <param name="alternateLabel">The second label the row's match step may answer; <see cref="EnumerationHabitatClass.None"/> on a single-label row.</param>
    /// <param name="onNominalFree">The row's admission on the nominal-free path.</param>
    /// <param name="onNominal">The row's admission on the nominal path.</param>
    /// <param name="carriers">The construct kinds the row's signal may ride.</param>
    /// <param name="faces">The decider faces the row's family owns.</param>
    /// <param name="match">The row's match step.</param>
    public HabitatProbeEntry(
        EnumerationHabitatClass label,
        EnumerationHabitatClass alternateLabel,
        HabitatPathAdmission onNominalFree,
        HabitatPathAdmission onNominal,
        HabitatSignalCarriers carriers,
        EnumerationDeciderFaces faces,
        HabitatProbeDelegate match)
    {
        Label = label;
        AlternateLabel = alternateLabel;
        OnNominalFree = onNominalFree;
        OnNominal = onNominal;
        Carriers = carriers;
        Faces = faces;
        Match = match;
    }

    /// <summary>
    /// Whether the census admits the row for evaluation: the nominal mention
    /// selects the path column, and that column decides against the counting
    /// mention. The answer is TOTAL over the four census states — every column
    /// value answers for both counting states — so no state reaches the row
    /// through an unnamed exit.
    /// </summary>
    /// <param name="mentionsNominals">The survey census's nominal-mention bit, selecting the path column.</param>
    /// <param name="mentionsCounting">The survey census's counting-mention bit, read by a counting-gated column.</param>
    /// <returns><see langword="true"/> when the census admits the row for evaluation.</returns>
    public readonly bool Admits(bool mentionsNominals, bool mentionsCounting)
    {
        HabitatPathAdmission admission = mentionsNominals ? OnNominal : OnNominalFree;

        return admission switch
        {
            HabitatPathAdmission.Never => false,
            HabitatPathAdmission.Always => true,
            HabitatPathAdmission.WhenCounting => mentionsCounting,
            _ => false,
        };
    }
}
