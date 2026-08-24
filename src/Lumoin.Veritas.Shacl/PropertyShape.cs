using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// A SHACL property shape — constraints apply to value nodes reached from
/// the focus node by evaluating <see cref="Path"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §2.1.2. The value-node set for a given focus node
/// is the image of that focus node under the property path.
/// </remarks>
public sealed record PropertyShape: Shape
{
    /// <summary>The property path evaluated against each focus node to produce value nodes.</summary>
    public required PropertyPath Path { get; init; }
}
