namespace Lumoin.Veritas.Json;

/// <summary>
/// Produces a copy of the given node whose lifetime is independent of any
/// parent document the source node might have been part of.
/// </summary>
/// <remarks>
/// <para>
/// Some adapters tie node handles to a parent document that must remain alive
/// for the handles to be valid. <c>System.Text.Json</c>'s
/// <c>JsonElement</c> is one such case: it is invalidated when its owning
/// <c>JsonDocument</c> is disposed. JSON-LD context processing stores nested
/// <c>@context</c> values inside <see cref="LinkedData.TermDefinition{TNode}"/> instances that
/// outlive the document they were extracted from, so a detached copy is required.
/// </para>
/// <para>
/// Adapters whose nodes are already lifetime-independent may return the node
/// unchanged.
/// </para>
/// </remarks>
/// <param name="handle">The handle to clone.</param>
/// <returns>A handle to a node equivalent in content but with an independent lifetime.</returns>
public delegate JsonNode CloneNodeDelegate(object handle);
