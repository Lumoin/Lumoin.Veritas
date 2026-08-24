using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for SHACL shape classes, target predicates, and
/// structurally-important non-validating properties.
/// </summary>
/// <remarks>
/// Each public member is a <see cref="Utf8String"/> backed by a static
/// byte array allocated once at module load. Equality with IRIs that were
/// interned through a <see cref="Utf8StringPool"/> elsewhere is by byte
/// content, so these can be passed directly to <c>TermDictionary</c>
/// without being re-interned.
/// </remarks>
public static class ShaclCoreVocabulary
{
    //sh:Shape
    private static byte[] ShapeBytes { get; } = "http://www.w3.org/ns/shacl#Shape"u8.ToArray();

    //sh:NodeShape
    private static byte[] NodeShapeBytes { get; } = "http://www.w3.org/ns/shacl#NodeShape"u8.ToArray();

    //sh:PropertyShape
    private static byte[] PropertyShapeBytes { get; } = "http://www.w3.org/ns/shacl#PropertyShape"u8.ToArray();

    //sh:ShapeClass (SHACL 1.2)
    private static byte[] ShapeClassBytes { get; } = "http://www.w3.org/ns/shacl#ShapeClass"u8.ToArray();

    //sh:targetClass
    private static byte[] TargetClassBytes { get; } = "http://www.w3.org/ns/shacl#targetClass"u8.ToArray();

    //sh:targetNode
    private static byte[] TargetNodeBytes { get; } = "http://www.w3.org/ns/shacl#targetNode"u8.ToArray();

    //sh:targetSubjectsOf
    private static byte[] TargetSubjectsOfBytes { get; } = "http://www.w3.org/ns/shacl#targetSubjectsOf"u8.ToArray();

    //sh:targetObjectsOf
    private static byte[] TargetObjectsOfBytes { get; } = "http://www.w3.org/ns/shacl#targetObjectsOf"u8.ToArray();

    //sh:targetWhere (SHACL 1.2)
    private static byte[] TargetWhereBytes { get; } = "http://www.w3.org/ns/shacl#targetWhere"u8.ToArray();

    //sh:severity
    private static byte[] SeverityBytes { get; } = "http://www.w3.org/ns/shacl#severity"u8.ToArray();

    //sh:deactivated
    private static byte[] DeactivatedBytes { get; } = "http://www.w3.org/ns/shacl#deactivated"u8.ToArray();

    //sh:message
    private static byte[] MessageBytes { get; } = "http://www.w3.org/ns/shacl#message"u8.ToArray();

    //sh:path
    private static byte[] PathBytes { get; } = "http://www.w3.org/ns/shacl#path"u8.ToArray();

    /// <summary><c>sh:Shape</c> — the root class for all shapes.</summary>
    public static Utf8String Shape { get; } = new(ShapeBytes);

    /// <summary><c>sh:NodeShape</c> — a shape targeting nodes as a whole.</summary>
    public static Utf8String NodeShape { get; } = new(NodeShapeBytes);

    /// <summary><c>sh:PropertyShape</c> — a shape targeting values reachable via a property path.</summary>
    public static Utf8String PropertyShape { get; } = new(PropertyShapeBytes);

    /// <summary><c>sh:ShapeClass</c> — a class that is simultaneously a shape (SHACL 1.2).</summary>
    public static Utf8String ShapeClass { get; } = new(ShapeClassBytes);

    /// <summary><c>sh:targetClass</c></summary>
    public static Utf8String TargetClass { get; } = new(TargetClassBytes);

    /// <summary><c>sh:targetNode</c></summary>
    public static Utf8String TargetNode { get; } = new(TargetNodeBytes);

    /// <summary><c>sh:targetSubjectsOf</c></summary>
    public static Utf8String TargetSubjectsOf { get; } = new(TargetSubjectsOfBytes);

    /// <summary><c>sh:targetObjectsOf</c></summary>
    public static Utf8String TargetObjectsOf { get; } = new(TargetObjectsOfBytes);

    /// <summary><c>sh:targetWhere</c> — node-expression target (SHACL 1.2).</summary>
    public static Utf8String TargetWhere { get; } = new(TargetWhereBytes);

    /// <summary><c>sh:severity</c> — per-shape severity override.</summary>
    public static Utf8String Severity { get; } = new(SeverityBytes);

    /// <summary><c>sh:deactivated</c> — per-shape deactivation flag.</summary>
    public static Utf8String Deactivated { get; } = new(DeactivatedBytes);

    /// <summary><c>sh:message</c> — per-shape message literal.</summary>
    public static Utf8String Message { get; } = new(MessageBytes);

    /// <summary><c>sh:path</c> — property-path predicate for property shapes.</summary>
    public static Utf8String Path { get; } = new(PathBytes);

    /// <summary>Every IRI constant in this vocabulary, in declaration order — the SHACL core term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
    public static IReadOnlyList<Utf8String> All { get; } =
    [
        Shape, NodeShape, PropertyShape, ShapeClass, TargetClass, TargetNode,
        TargetSubjectsOf, TargetObjectsOf, TargetWhere, Severity, Deactivated, Message, Path,
    ];
}
