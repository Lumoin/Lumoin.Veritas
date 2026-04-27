namespace Lumoin.Veritas.Shacl;

/// <summary>
/// A SHACL node shape — constraints apply to the focus node itself.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §2.1.1. Node shapes have no <c>sh:path</c>; each
/// constraint's value-node set is the singleton containing the focus node.
/// </remarks>
public sealed record NodeShape: Shape;
