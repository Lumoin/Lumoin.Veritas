using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// The root of a parsed Turtle or TriG document. Carries the
/// document's identity, the directives in source order, the
/// statements in source order, and a node-id-keyed lookup table for
/// resolving <see cref="DocumentNodeRef"/> values back to their AST
/// nodes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentId"/> is content-addressed by the convention
/// described on <see cref="Core.Sourcing.DocumentId"/>: the caller
/// mints it once by applying the application's chosen
/// <c>VeritasHash</c> to the document's canonical bytes. The parser
/// itself never computes the hash — it accepts the identifier as a
/// parameter so the same parsed document carries the same identity
/// across machines and library builds.
/// </para>
/// </remarks>
[DebuggerDisplay("TurtleDocument {Statements.Length} statements, {Prefixes.Length} prefixes, {NodeCount} nodes")]
public sealed class TurtleDocument
{
    /// <summary>
    /// Initialises a new <see cref="TurtleDocument"/>.
    /// </summary>
    /// <param name="documentId">The content-addressed document identifier.</param>
    /// <param name="prefixes">The prefix declarations in source order.</param>
    /// <param name="baseDeclarations">The base declarations in source order.</param>
    /// <param name="versions">The version declarations in source order.</param>
    /// <param name="statements">All top-level statements (triples and TriG graph blocks) in source order.</param>
    /// <param name="nodes">A node-id-keyed lookup table covering every AST node in the document.</param>
    public TurtleDocument(
        DocumentId documentId,
        ImmutableArray<PrefixDeclaration> prefixes,
        ImmutableArray<BaseDeclaration> baseDeclarations,
        ImmutableArray<VersionDeclaration> versions,
        ImmutableArray<Statement> statements,
        IReadOnlyDictionary<int, TurtleAstNode> nodes)
    {
        DocumentId = documentId;
        Prefixes = prefixes;
        BaseDeclarations = baseDeclarations;
        Versions = versions;
        Statements = statements;
        Nodes = nodes;
    }

    /// <summary>Gets the content-addressed document identifier.</summary>
    public DocumentId DocumentId { get; }

    /// <summary>Gets the prefix declarations encountered during parsing in source order.</summary>
    public ImmutableArray<PrefixDeclaration> Prefixes { get; }

    /// <summary>Gets the base declarations encountered during parsing in source order.</summary>
    public ImmutableArray<BaseDeclaration> BaseDeclarations { get; }

    /// <summary>Gets the version declarations encountered during parsing in source order.</summary>
    public ImmutableArray<VersionDeclaration> Versions { get; }

    /// <summary>Gets all top-level statements in source order.</summary>
    public ImmutableArray<Statement> Statements { get; }

    /// <summary>Gets the node-id-keyed lookup table used by <see cref="GetNode(int)"/>.</summary>
    public IReadOnlyDictionary<int, TurtleAstNode> Nodes { get; }

    /// <summary>Gets the total number of AST nodes in the document.</summary>
    public int NodeCount => Nodes.Count;

    /// <summary>
    /// Resolves a node identifier — typically read from a
    /// <see cref="DocumentNodeRef"/> on an emitted quad — back to its
    /// AST node.
    /// </summary>
    /// <param name="nodeId">The identifier to look up.</param>
    /// <returns>The matching AST node, or <c>null</c> when the identifier is unknown.</returns>
    public TurtleAstNode? GetNode(int nodeId)
    {
        return Nodes.TryGetValue(nodeId, out TurtleAstNode? node) ? node : null;
    }
}
