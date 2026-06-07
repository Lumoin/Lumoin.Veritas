using System.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One named graph's entry in a dataset's graph directory: the
/// root handle, the content-addressed root identifier, and the
/// triple count — everything needed to mint a queryable store on
/// demand, at a fraction of the cost of holding a store and
/// snapshot object per graph. The directory plus one
/// <see cref="Lumoin.Veritas.Core.Hypertrie.HypertrieRootSetPin"/>
/// is the dataset-level snapshot.
/// </summary>
/// <param name="Root">The graph's root handle in the shared arena.</param>
/// <param name="Id">The content-addressed identifier of <paramref name="Root"/>; the graph's identity in dataset state hashing and journal transitions.</param>
/// <param name="Count">The graph's distinct triple count, carried so on-demand minting skips the counting enumeration.</param>
[DebuggerDisplay("GraphDirectoryEntry Id={Id.Value:X16} Count={Count}")]
public readonly record struct GraphDirectoryEntry(NodeHandle Root, NodeIdentifier Id, int Count);
