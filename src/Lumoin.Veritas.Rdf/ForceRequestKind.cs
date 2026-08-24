namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The kinds of request an algebra iterator can yield to the
/// <see cref="GraphKFold"/> driver.
/// </summary>
public enum ForceRequestKind
{
    /// <summary>No action; the driver resumes the algebra immediately.</summary>
    Skip = 0,

    /// <summary>
    /// Force the child at the attached index. The driver computes the
    /// child's folded result (if not already computed) and makes it
    /// available to the algebra on resumption.
    /// </summary>
    Force = 1
}
