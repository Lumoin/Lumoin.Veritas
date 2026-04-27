namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The computation status of a single child node during a
/// <see cref="GraphKFold"/> reduction pass.
/// </summary>
/// <remarks>
/// <para>
/// Status is maintained by <see cref="ReductionState{TResult}"/> and
/// observed by algebras through
/// <see cref="ChildHandles{TResult}.IsComputed"/>. The <see cref="Computing"/>
/// state exists to detect recursion: if an algebra's child requests (via
/// <see cref="ForceRequest.Force(int)"/>) lead back to a node that is
/// currently being reduced, the driver raises a specific error rather than
/// entering an infinite loop.
/// </para>
/// </remarks>
public enum ChildStatus
{
    /// <summary>The child's result has not been requested yet.</summary>
    NotComputed = 0,

    /// <summary>
    /// The child's algebra is currently being executed. Encountering this
    /// state on a fresh force request indicates recursion: the fold graph
    /// contains a cycle that the algebra traverses.
    /// </summary>
    Computing = 1,

    /// <summary>The child's folded result is available.</summary>
    Computed = 2
}
