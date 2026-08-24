namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Which factorising route a join-route decision engages inside the batched pipeline.
/// </summary>
public enum FactorizationEngagement
{
    /// <summary>No engagement was decided, so the <see cref="QueryEnginePolicy"/> factorisation flags apply verbatim. The default.</summary>
    Unspecified = 0,

    /// <summary>Neither factorising route is engaged: the plan runs the streamed join.</summary>
    None = 1,

    /// <summary>A qualifying star routes through the factorising join, keeping the intermediates product-of-unions until the final flatten.</summary>
    Star = 2,

    /// <summary>A qualifying three-pattern chain routes through the join then the nesting step, staying factorised across the branch-variable join.</summary>
    Chain = 3
}
