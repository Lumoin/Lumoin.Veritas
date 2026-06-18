namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// Which clause a resident <see cref="EvalFrameKind.Transform"/> cursor is evaluating on its current turn:
/// the location pattern once, then the update and (optional) delete clause once per matched node.
/// </summary>
internal enum TransformPhase
{
    /// <summary>Evaluating the location pattern over the cloned input to collect the matched nodes.</summary>
    Pattern,

    /// <summary>Evaluating the update clause under the current matched node, before merging its object into the match.</summary>
    Update,

    /// <summary>Evaluating the delete clause under the current matched node, before removing the named keys from the match.</summary>
    Delete
}
