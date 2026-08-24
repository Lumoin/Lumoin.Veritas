using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for the node-kind parameter values accepted by
/// <c>sh:nodeKind</c>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.6.3, <c>sh:nodeKind</c> takes one of six values.
/// <see cref="TryGetNodeKind(ReadOnlySpan{byte}, out NodeKind)"/> maps
/// the IRI byte sequence to the <see cref="NodeKind"/> enum.
/// </remarks>
public static class ShaclNodeKindVocabulary
{
    private static byte[] BlankNodeBytes { get; } = "http://www.w3.org/ns/shacl#BlankNode"u8.ToArray();
    private static byte[] IRIBytes { get; } = "http://www.w3.org/ns/shacl#IRI"u8.ToArray();
    private static byte[] LiteralBytes { get; } = "http://www.w3.org/ns/shacl#Literal"u8.ToArray();
    private static byte[] BlankNodeOrIRIBytes { get; } = "http://www.w3.org/ns/shacl#BlankNodeOrIRI"u8.ToArray();
    private static byte[] BlankNodeOrLiteralBytes { get; } = "http://www.w3.org/ns/shacl#BlankNodeOrLiteral"u8.ToArray();
    private static byte[] IRIOrLiteralBytes { get; } = "http://www.w3.org/ns/shacl#IRIOrLiteral"u8.ToArray();

    /// <summary><c>sh:BlankNode</c></summary>
    public static Utf8String BlankNode { get; } = new(BlankNodeBytes);

    /// <summary><c>sh:IRI</c></summary>
    public static Utf8String IRI { get; } = new(IRIBytes);

    /// <summary><c>sh:Literal</c></summary>
    public static Utf8String Literal { get; } = new(LiteralBytes);

    /// <summary><c>sh:BlankNodeOrIRI</c></summary>
    public static Utf8String BlankNodeOrIRI { get; } = new(BlankNodeOrIRIBytes);

    /// <summary><c>sh:BlankNodeOrLiteral</c></summary>
    public static Utf8String BlankNodeOrLiteral { get; } = new(BlankNodeOrLiteralBytes);

    /// <summary><c>sh:IRIOrLiteral</c></summary>
    public static Utf8String IRIOrLiteral { get; } = new(IRIOrLiteralBytes);

    /// <summary>
    /// Attempts to map a SHACL node-kind IRI (as UTF-8 bytes) to the
    /// corresponding <see cref="NodeKind"/> enum value.
    /// </summary>
    /// <param name="iri">The UTF-8 bytes of a candidate node-kind IRI.</param>
    /// <param name="nodeKind">
    /// When this method returns <c>true</c>, contains the matched
    /// <see cref="NodeKind"/>. Otherwise <see cref="NodeKind.BlankNode"/>
    /// (the default enum value).
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="iri"/> exactly matches one of the six
    /// SHACL node-kind IRIs; otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetNodeKind(ReadOnlySpan<byte> iri, out NodeKind nodeKind)
    {
        if(iri.SequenceEqual(BlankNodeBytes))
        {
            nodeKind = NodeKind.BlankNode;
            return true;
        }

        if(iri.SequenceEqual(IRIBytes))
        {
            nodeKind = NodeKind.IRI;
            return true;
        }

        if(iri.SequenceEqual(LiteralBytes))
        {
            nodeKind = NodeKind.Literal;
            return true;
        }

        if(iri.SequenceEqual(BlankNodeOrIRIBytes))
        {
            nodeKind = NodeKind.BlankNodeOrIRI;
            return true;
        }

        if(iri.SequenceEqual(BlankNodeOrLiteralBytes))
        {
            nodeKind = NodeKind.BlankNodeOrLiteral;
            return true;
        }

        if(iri.SequenceEqual(IRIOrLiteralBytes))
        {
            nodeKind = NodeKind.IRIOrLiteral;
            return true;
        }

        nodeKind = default;

        return false;
    }

    /// <summary>
    /// Attempts to map a SHACL node-kind IRI (as a <see cref="Utf8String"/>)
    /// to the corresponding <see cref="NodeKind"/> enum value.
    /// </summary>
    /// <param name="iri">The candidate node-kind IRI.</param>
    /// <param name="nodeKind">
    /// When this method returns <c>true</c>, contains the matched
    /// <see cref="NodeKind"/>. Otherwise the default enum value.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="iri"/> exactly matches one of the six
    /// SHACL node-kind IRIs; otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetNodeKind(Utf8String iri, out NodeKind nodeKind) => TryGetNodeKind(iri.Span, out nodeKind);
}
