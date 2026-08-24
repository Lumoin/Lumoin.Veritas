using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for the parameters of SHACL constraint components.
/// </summary>
/// <remarks>
/// Covers every constraint-component parameter defined by SHACL 1.2 Core
/// §6, including SHACL 1.2 additions (reification, lists, root class).
/// Each value is a <see cref="Utf8String"/> backed by a static byte array.
/// </remarks>
public static class ShaclConstraintVocabulary
{
    //Cardinality
    private static byte[] MinCountBytes { get; } = "http://www.w3.org/ns/shacl#minCount"u8.ToArray();
    private static byte[] MaxCountBytes { get; } = "http://www.w3.org/ns/shacl#maxCount"u8.ToArray();

    //Value range
    private static byte[] MinExclusiveBytes { get; } = "http://www.w3.org/ns/shacl#minExclusive"u8.ToArray();
    private static byte[] MaxExclusiveBytes { get; } = "http://www.w3.org/ns/shacl#maxExclusive"u8.ToArray();
    private static byte[] MinInclusiveBytes { get; } = "http://www.w3.org/ns/shacl#minInclusive"u8.ToArray();
    private static byte[] MaxInclusiveBytes { get; } = "http://www.w3.org/ns/shacl#maxInclusive"u8.ToArray();

    //String / literal
    private static byte[] MinLengthBytes { get; } = "http://www.w3.org/ns/shacl#minLength"u8.ToArray();
    private static byte[] MaxLengthBytes { get; } = "http://www.w3.org/ns/shacl#maxLength"u8.ToArray();
    private static byte[] PatternBytes { get; } = "http://www.w3.org/ns/shacl#pattern"u8.ToArray();
    private static byte[] FlagsBytes { get; } = "http://www.w3.org/ns/shacl#flags"u8.ToArray();
    private static byte[] SingleLineBytes { get; } = "http://www.w3.org/ns/shacl#singleLine"u8.ToArray();
    private static byte[] LanguageInBytes { get; } = "http://www.w3.org/ns/shacl#languageIn"u8.ToArray();
    private static byte[] UniqueLangBytes { get; } = "http://www.w3.org/ns/shacl#uniqueLang"u8.ToArray();

    //Term inspection
    private static byte[] ClassBytes { get; } = "http://www.w3.org/ns/shacl#class"u8.ToArray();
    private static byte[] DatatypeBytes { get; } = "http://www.w3.org/ns/shacl#datatype"u8.ToArray();
    private static byte[] NodeKindBytes { get; } = "http://www.w3.org/ns/shacl#nodeKind"u8.ToArray();
    private static byte[] RootClassBytes { get; } = "http://www.w3.org/ns/shacl#rootClass"u8.ToArray();

    //Value set
    private static byte[] HasValueBytes { get; } = "http://www.w3.org/ns/shacl#hasValue"u8.ToArray();
    private static byte[] InBytes { get; } = "http://www.w3.org/ns/shacl#in"u8.ToArray();

    //Pair comparison
    private static byte[] EqualsToBytes { get; } = "http://www.w3.org/ns/shacl#equals"u8.ToArray();
    private static byte[] DisjointBytes { get; } = "http://www.w3.org/ns/shacl#disjoint"u8.ToArray();
    private static byte[] LessThanBytes { get; } = "http://www.w3.org/ns/shacl#lessThan"u8.ToArray();
    private static byte[] LessThanOrEqualsBytes { get; } = "http://www.w3.org/ns/shacl#lessThanOrEquals"u8.ToArray();

    //Boolean combinators
    private static byte[] NotBytes { get; } = "http://www.w3.org/ns/shacl#not"u8.ToArray();
    private static byte[] AndBytes { get; } = "http://www.w3.org/ns/shacl#and"u8.ToArray();
    private static byte[] OrBytes { get; } = "http://www.w3.org/ns/shacl#or"u8.ToArray();
    private static byte[] XoneBytes { get; } = "http://www.w3.org/ns/shacl#xone"u8.ToArray();

    //Shape references
    private static byte[] NodeBytes { get; } = "http://www.w3.org/ns/shacl#node"u8.ToArray();
    private static byte[] PropertyBytes { get; } = "http://www.w3.org/ns/shacl#property"u8.ToArray();
    private static byte[] QualifiedValueShapeBytes { get; } = "http://www.w3.org/ns/shacl#qualifiedValueShape"u8.ToArray();
    private static byte[] QualifiedMinCountBytes { get; } = "http://www.w3.org/ns/shacl#qualifiedMinCount"u8.ToArray();
    private static byte[] QualifiedMaxCountBytes { get; } = "http://www.w3.org/ns/shacl#qualifiedMaxCount"u8.ToArray();
    private static byte[] QualifiedValueShapesDisjointBytes { get; } = "http://www.w3.org/ns/shacl#qualifiedValueShapesDisjoint"u8.ToArray();
    private static byte[] SubsetOfBytes { get; } = "http://www.w3.org/ns/shacl#subsetOf"u8.ToArray();

    //Closed world
    private static byte[] ClosedBytes { get; } = "http://www.w3.org/ns/shacl#closed"u8.ToArray();
    private static byte[] IgnoredPropertiesBytes { get; } = "http://www.w3.org/ns/shacl#ignoredProperties"u8.ToArray();

    //Uniqueness
    private static byte[] UniqueValuesForBytes { get; } = "http://www.w3.org/ns/shacl#uniqueValuesFor"u8.ToArray();

    //Reification (SHACL 1.2)
    private static byte[] ReifierShapeBytes { get; } = "http://www.w3.org/ns/shacl#reifierShape"u8.ToArray();
    private static byte[] ReificationRequiredBytes { get; } = "http://www.w3.org/ns/shacl#reificationRequired"u8.ToArray();

    //Lists (SHACL 1.2)
    private static byte[] MemberShapeBytes { get; } = "http://www.w3.org/ns/shacl#memberShape"u8.ToArray();
    private static byte[] MinListLengthBytes { get; } = "http://www.w3.org/ns/shacl#minListLength"u8.ToArray();
    private static byte[] MaxListLengthBytes { get; } = "http://www.w3.org/ns/shacl#maxListLength"u8.ToArray();
    private static byte[] UniqueMembersBytes { get; } = "http://www.w3.org/ns/shacl#uniqueMembers"u8.ToArray();

    /// <summary><c>sh:minCount</c></summary>
    public static Utf8String MinCount { get; } = new(MinCountBytes);

    /// <summary><c>sh:maxCount</c></summary>
    public static Utf8String MaxCount { get; } = new(MaxCountBytes);

    /// <summary><c>sh:minExclusive</c></summary>
    public static Utf8String MinExclusive { get; } = new(MinExclusiveBytes);

    /// <summary><c>sh:maxExclusive</c></summary>
    public static Utf8String MaxExclusive { get; } = new(MaxExclusiveBytes);

    /// <summary><c>sh:minInclusive</c></summary>
    public static Utf8String MinInclusive { get; } = new(MinInclusiveBytes);

    /// <summary><c>sh:maxInclusive</c></summary>
    public static Utf8String MaxInclusive { get; } = new(MaxInclusiveBytes);

    /// <summary><c>sh:minLength</c></summary>
    public static Utf8String MinLength { get; } = new(MinLengthBytes);

    /// <summary><c>sh:maxLength</c></summary>
    public static Utf8String MaxLength { get; } = new(MaxLengthBytes);

    /// <summary><c>sh:pattern</c></summary>
    public static Utf8String Pattern { get; } = new(PatternBytes);

    /// <summary><c>sh:flags</c></summary>
    public static Utf8String Flags { get; } = new(FlagsBytes);

    /// <summary><c>sh:singleLine</c></summary>
    public static Utf8String SingleLine { get; } = new(SingleLineBytes);

    /// <summary><c>sh:languageIn</c></summary>
    public static Utf8String LanguageIn { get; } = new(LanguageInBytes);

    /// <summary><c>sh:uniqueLang</c></summary>
    public static Utf8String UniqueLang { get; } = new(UniqueLangBytes);

    /// <summary><c>sh:class</c></summary>
    public static Utf8String Class { get; } = new(ClassBytes);

    /// <summary><c>sh:datatype</c></summary>
    public static Utf8String Datatype { get; } = new(DatatypeBytes);

    /// <summary><c>sh:nodeKind</c></summary>
    public static Utf8String NodeKind { get; } = new(NodeKindBytes);

    /// <summary><c>sh:rootClass</c></summary>
    public static Utf8String RootClass { get; } = new(RootClassBytes);

    /// <summary><c>sh:hasValue</c></summary>
    public static Utf8String HasValue { get; } = new(HasValueBytes);

    /// <summary><c>sh:in</c></summary>
    public static Utf8String In { get; } = new(InBytes);

    /// <summary><c>sh:equals</c></summary>
    public static Utf8String EqualsTo { get; } = new(EqualsToBytes);

    /// <summary><c>sh:disjoint</c></summary>
    public static Utf8String Disjoint { get; } = new(DisjointBytes);

    /// <summary><c>sh:lessThan</c></summary>
    public static Utf8String LessThan { get; } = new(LessThanBytes);

    /// <summary><c>sh:lessThanOrEquals</c></summary>
    public static Utf8String LessThanOrEquals { get; } = new(LessThanOrEqualsBytes);

    /// <summary><c>sh:not</c></summary>
    public static Utf8String Not { get; } = new(NotBytes);

    /// <summary><c>sh:and</c></summary>
    public static Utf8String And { get; } = new(AndBytes);

    /// <summary><c>sh:or</c></summary>
    public static Utf8String Or { get; } = new(OrBytes);

    /// <summary><c>sh:xone</c></summary>
    public static Utf8String Xone { get; } = new(XoneBytes);

    /// <summary><c>sh:node</c></summary>
    public static Utf8String Node { get; } = new(NodeBytes);

    /// <summary><c>sh:property</c></summary>
    public static Utf8String Property { get; } = new(PropertyBytes);

    /// <summary><c>sh:qualifiedValueShape</c></summary>
    public static Utf8String QualifiedValueShape { get; } = new(QualifiedValueShapeBytes);

    /// <summary><c>sh:qualifiedMinCount</c></summary>
    public static Utf8String QualifiedMinCount { get; } = new(QualifiedMinCountBytes);

    /// <summary><c>sh:qualifiedMaxCount</c></summary>
    public static Utf8String QualifiedMaxCount { get; } = new(QualifiedMaxCountBytes);

    /// <summary><c>sh:qualifiedValueShapesDisjoint</c></summary>
    public static Utf8String QualifiedValueShapesDisjoint { get; } = new(QualifiedValueShapesDisjointBytes);

    /// <summary><c>sh:subsetOf</c></summary>
    public static Utf8String SubsetOf { get; } = new(SubsetOfBytes);

    /// <summary><c>sh:closed</c></summary>
    public static Utf8String Closed { get; } = new(ClosedBytes);

    /// <summary><c>sh:ignoredProperties</c></summary>
    public static Utf8String IgnoredProperties { get; } = new(IgnoredPropertiesBytes);

    /// <summary><c>sh:uniqueValuesFor</c></summary>
    public static Utf8String UniqueValuesFor { get; } = new(UniqueValuesForBytes);

    /// <summary><c>sh:reifierShape</c> (SHACL 1.2).</summary>
    public static Utf8String ReifierShape { get; } = new(ReifierShapeBytes);

    /// <summary><c>sh:reificationRequired</c> (SHACL 1.2).</summary>
    public static Utf8String ReificationRequired { get; } = new(ReificationRequiredBytes);

    /// <summary><c>sh:memberShape</c> (SHACL 1.2).</summary>
    public static Utf8String MemberShape { get; } = new(MemberShapeBytes);

    /// <summary><c>sh:minListLength</c> (SHACL 1.2).</summary>
    public static Utf8String MinListLength { get; } = new(MinListLengthBytes);

    /// <summary><c>sh:maxListLength</c> (SHACL 1.2).</summary>
    public static Utf8String MaxListLength { get; } = new(MaxListLengthBytes);

    /// <summary><c>sh:uniqueMembers</c> (SHACL 1.2).</summary>
    public static Utf8String UniqueMembers { get; } = new(UniqueMembersBytes);

    /// <summary><c>sh:sparql</c> — links a shape to a SPARQL-based constraint (SHACL-SPARQL §5.1).</summary>
    public static Utf8String Sparql { get; } = new("http://www.w3.org/ns/shacl#sparql"u8.ToArray());

    /// <summary><c>sh:select</c> — the SELECT query text of a SPARQL-based constraint (SHACL-SPARQL §5.1).</summary>
    public static Utf8String Select { get; } = new("http://www.w3.org/ns/shacl#select"u8.ToArray());

    /// <summary><c>sh:ask</c> — the ASK query text of a SPARQL-based constraint validator (SHACL-SPARQL §6); not yet evaluated.</summary>
    public static Utf8String Ask { get; } = new("http://www.w3.org/ns/shacl#ask"u8.ToArray());

    /// <summary><c>sh:prefixes</c> — links a SPARQL-based constraint to the prefix-declaration subject(s) whose <c>sh:declare</c> triples supply its namespace bindings (SHACL-SPARQL §5.2.1).</summary>
    public static Utf8String Prefixes { get; } = new("http://www.w3.org/ns/shacl#prefixes"u8.ToArray());

    /// <summary><c>sh:declare</c> — links a prefix-declaration subject to one namespace declaration (SHACL-SPARQL §5.2.1).</summary>
    public static Utf8String Declare { get; } = new("http://www.w3.org/ns/shacl#declare"u8.ToArray());

    /// <summary><c>sh:prefix</c> — the prefix string of a namespace declaration (SHACL-SPARQL §5.2.1).</summary>
    public static Utf8String Prefix { get; } = new("http://www.w3.org/ns/shacl#prefix"u8.ToArray());

    /// <summary><c>sh:namespace</c> — the namespace IRI (an <c>xsd:anyURI</c> literal) of a namespace declaration (SHACL-SPARQL §5.2.1).</summary>
    public static Utf8String Namespace { get; } = new("http://www.w3.org/ns/shacl#namespace"u8.ToArray());

    /// <summary><c>sh:parameter</c> — declares a parameter of a SPARQL-based constraint component (SHACL-SPARQL §6).</summary>
    public static Utf8String Parameter { get; } = new("http://www.w3.org/ns/shacl#parameter"u8.ToArray());

    /// <summary><c>sh:optional</c> — marks a constraint-component parameter as optional (SHACL-SPARQL §6).</summary>
    public static Utf8String Optional { get; } = new("http://www.w3.org/ns/shacl#optional"u8.ToArray());

    /// <summary><c>sh:validator</c> — a constraint component's generic SPARQL validator (SHACL-SPARQL §6.2).</summary>
    public static Utf8String Validator { get; } = new("http://www.w3.org/ns/shacl#validator"u8.ToArray());

    /// <summary><c>sh:nodeValidator</c> — a constraint component's node-shape SPARQL validator (SHACL-SPARQL §6.2).</summary>
    public static Utf8String NodeValidator { get; } = new("http://www.w3.org/ns/shacl#nodeValidator"u8.ToArray());

    /// <summary><c>sh:propertyValidator</c> — a constraint component's property-shape SPARQL validator (SHACL-SPARQL §6.2).</summary>
    public static Utf8String PropertyValidator { get; } = new("http://www.w3.org/ns/shacl#propertyValidator"u8.ToArray());

    /// <summary>Every IRI constant in this vocabulary, in declaration order — the SHACL constraint-parameter term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
    public static IReadOnlyList<Utf8String> All { get; } =
    [
        MinCount, MaxCount, MinExclusive, MaxExclusive, MinInclusive, MaxInclusive,
        MinLength, MaxLength, Pattern, Flags, SingleLine, LanguageIn, UniqueLang,
        Class, Datatype, NodeKind, RootClass, HasValue, In, EqualsTo, Disjoint,
        LessThan, LessThanOrEquals, Not, And, Or, Xone, Node, Property,
        QualifiedValueShape, QualifiedMinCount, QualifiedMaxCount, QualifiedValueShapesDisjoint,
        SubsetOf, Closed, IgnoredProperties, UniqueValuesFor, ReifierShape, ReificationRequired,
        MemberShape, MinListLength, MaxListLength, UniqueMembers, Sparql, Select, Ask,
        Prefixes, Declare, Prefix, Namespace, Parameter, Optional, Validator, NodeValidator, PropertyValidator,
    ];
}
