using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Marker interface for document-tree nodes that can be carried by Linked
/// Data types in a format-neutral way: a JSON node from a JSON-LD parser, a
/// CBOR node from a CBOR-LD parser, or any other format-specific tree node
/// that needs to ride inside <see cref="TermDefinition.LocalContext"/> or
/// similar slots.
/// </summary>
/// <remarks>
/// <para>
/// The interface intentionally exposes no members. Its job is to give
/// LinkedData types a single typed slot for "any document-tree node"
/// without LinkedData taking a dependency on a specific format's node
/// abstraction. Format adapters cast to their concrete node type at the
/// boundary.
/// </para>
/// <para>
/// Format-specific node types — for example
/// <c>Lumoin.Veritas.JsonLd.JsonLdNode</c> — implement this interface so
/// they can be assigned to LinkedData-typed slots with no wrapping. Where
/// the implementer is a value type (struct), assignment to a
/// <see cref="ILinkedDataDocumentNode"/>-typed reference boxes once at the
/// boundary; this is the architectural cost of the interface, paid once
/// per stored node.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "The interface is a deliberate marker: it tags format-specific document-tree node types (e.g. JsonLdNode, a future CborLdNode) so LinkedData types can hold them in a single typed slot without depending on any one format's node abstraction. Adding members would couple the marker to one format's surface and defeat its purpose.")]
public interface ILinkedDataDocumentNode
{
}
