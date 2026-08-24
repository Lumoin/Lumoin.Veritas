namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Produces a stable deduplication key for a graph traversal node.
/// </summary>
/// <typeparam name="TNode">The node type.</typeparam>
/// <typeparam name="TKey">The key type returned by the delegate.</typeparam>
/// <param name="node">The node whose key to compute.</param>
/// <returns>The deduplication key.</returns>
/// <remarks>
/// Named delegate used by <see cref="IterativeTraversal"/> and
/// <see cref="TraversalPrimitives"/> in place of <see cref="System.Func{TNode, TKey}"/>
/// for consistency with the project's named-delegate convention
/// (mirroring the AdjacencyDelegates family). Compatible with method
/// groups and lambdas at every call site, so existing callers that
/// pass a <c>static</c> method group or a lambda continue to work
/// without source changes.
/// </remarks>
public delegate TKey KeyOfDelegate<in TNode, out TKey>(TNode node);
