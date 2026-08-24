namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// ONE habitat probe row's admission on ONE census path: whether the walk
/// evaluates that row's match step at all. The three values are TOTAL over the
/// path's census state — a row is either never evaluated on the path, always
/// evaluated, or evaluated exactly where the survey census mentions counting —
/// so a row's reachability is read off its two declared columns and the two
/// census bits, and no state is left without an answer.
/// </summary>
internal enum HabitatPathAdmission
{
    /// <summary>The path never evaluates the row: no module the census routes down this path can be answered by the row.</summary>
    Never = 0,

    /// <summary>The path always evaluates the row, whatever the census's counting mention.</summary>
    Always = 1,

    /// <summary>The path evaluates the row exactly where the survey census mentions counting.</summary>
    WhenCounting = 2,
}
