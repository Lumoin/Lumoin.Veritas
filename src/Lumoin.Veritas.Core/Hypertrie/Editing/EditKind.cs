namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// The kind of edit recorded in an <see cref="EditBuffer"/> or a
/// committed <see cref="JournalEntry"/>: an addition or a removal
/// of a single triple.
/// </summary>
public enum EditKind
{
    /// <summary>The triple is to be added to the graph.</summary>
    Add,

    /// <summary>The triple is to be removed from the graph.</summary>
    Remove
}
