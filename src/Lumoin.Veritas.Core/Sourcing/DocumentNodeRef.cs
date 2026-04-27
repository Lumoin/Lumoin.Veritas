using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// A stable, value-equatable reference to a node within a parsed or
/// otherwise-structured document: an ordinal identity that consumers
/// resolve through a document-specific lookup to obtain whatever the
/// format calls a "node."
/// </summary>
/// <remarks>
/// <para>
/// <b>Format-neutral identity.</b> The reference is meaningful for any
/// document origin, not only text. For an N-Quads dump, the
/// <see cref="Index"/> is the ordinal of the parsed quad in document
/// order. For a future Turtle parser that produces an AST, it is the
/// parser-assigned identity of the AST node. For data ingested from
/// columnar storage such as Parquet, it is the row index. Each origin
/// chooses what "node" means; this type only carries the ordinal.
/// </para>
/// <para>
/// <b>Opaque to consumers.</b> The internal structure of a
/// <see cref="DocumentNodeRef"/> is part of the producer's contract
/// with the document model, not part of the consumer-facing surface.
/// Consumers receive references from producer outputs, pass them
/// around as opaque tokens, and resolve them through the producer's
/// lookup APIs. They do not construct references by hand and do not
/// depend on the internal layout of the fields. This discipline
/// allows the internal representation to evolve — for example,
/// towards content-addressed identity for collaborative editing
/// scenarios — without breaking consumer code.
/// </para>
/// <para>
/// <b>Document-scoped identity.</b> A reference is meaningful only
/// within the producer output that minted it.
/// <see cref="DocumentId"/> identifies which document it came from;
/// <see cref="Index"/> is the producer-assigned identity within that
/// document, typically the position of the node in document order.
/// Two references with the same <see cref="DocumentId"/> and
/// <see cref="Index"/> denote the same logical node; references with
/// different <see cref="DocumentId"/>s denote nodes in different
/// documents and are not directly comparable for "same node"
/// semantics even when their <see cref="Index"/>es coincide.
/// </para>
/// <para>
/// <b>Cross-replica identity.</b> Because <see cref="DocumentId"/> is
/// content-addressed, two parses or reads of the same canonical bytes
/// — by the same machine or different machines — produce the same
/// <see cref="DocumentId"/>. Provided the producer assigns
/// <see cref="Index"/> values deterministically (typically by document
/// order), the same logical node has the same
/// <see cref="DocumentNodeRef"/> across replicas. This is the property
/// collaborative-editing layers rely on for stable cross-replica
/// references; the producer does not implement collaborative editing
/// itself but admits it through this determinism.
/// </para>
/// </remarks>
/// <param name="DocumentId">The identity of the document the node belongs to.</param>
/// <param name="Index">The producer-assigned identity of the node within the document.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct DocumentNodeRef(DocumentId DocumentId, int Index)
{
    /// <summary>
    /// Gets the debugger label rendering the reference compactly.
    /// Used by the type's <see cref="DebuggerDisplayAttribute"/>.
    /// </summary>
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"DocumentNodeRef #{Index} in {DocumentId.Hash:X16}");
}
