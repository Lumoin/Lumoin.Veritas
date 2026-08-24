using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Database;

/// <summary>
/// One world in a mutable database's worlds listing: the world's name, its current content-addressed
/// state identifier, and the name of the world it was forked from. The state identifier is the
/// revision token a caching or streaming consumer scopes by — two worlds that converge to identical
/// content share one identifier — and the parent name is fork lineage recorded at fork time, standing
/// even after the parent's name is dropped.
/// </summary>
/// <param name="Name">The world's registered name.</param>
/// <param name="StateId">The world's current committed state identifier.</param>
/// <param name="Parent">The name of the world this one was forked from, or <see langword="null"/> for the primary world.</param>
public readonly record struct WorldDescriptor(string Name, NodeIdentifier StateId, string? Parent);
