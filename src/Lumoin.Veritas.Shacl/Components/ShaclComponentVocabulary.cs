using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// UTF-8 IRI constants for the SHACL 1.2 Core built-in constraint
/// component identifiers — the <c>sh:XxxConstraintComponent</c> IRIs that
/// appear as objects of <c>sh:sourceConstraintComponent</c> on
/// validation results.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="ShaclConstraintVocabulary"/> which holds the
/// <em>parameter</em> IRIs (<c>sh:minCount</c>, <c>sh:class</c>, etc.).
/// The component IRIs in this class name the components themselves
/// (<c>sh:MinCountConstraintComponent</c>, <c>sh:ClassConstraintComponent</c>)
/// and are what the validator emits to attribute each validation result
/// to its source constraint component.
/// </para>
/// </remarks>
public static class ShaclComponentVocabulary
{
    //Cardinality
    private static byte[] MinCountComponentBytes { get; } = "http://www.w3.org/ns/shacl#MinCountConstraintComponent"u8.ToArray();
    private static byte[] MaxCountComponentBytes { get; } = "http://www.w3.org/ns/shacl#MaxCountConstraintComponent"u8.ToArray();

    //Value range
    private static byte[] MinExclusiveComponentBytes { get; } = "http://www.w3.org/ns/shacl#MinExclusiveConstraintComponent"u8.ToArray();
    private static byte[] MaxExclusiveComponentBytes { get; } = "http://www.w3.org/ns/shacl#MaxExclusiveConstraintComponent"u8.ToArray();
    private static byte[] MinInclusiveComponentBytes { get; } = "http://www.w3.org/ns/shacl#MinInclusiveConstraintComponent"u8.ToArray();
    private static byte[] MaxInclusiveComponentBytes { get; } = "http://www.w3.org/ns/shacl#MaxInclusiveConstraintComponent"u8.ToArray();

    //String / literal
    private static byte[] MinLengthComponentBytes { get; } = "http://www.w3.org/ns/shacl#MinLengthConstraintComponent"u8.ToArray();
    private static byte[] MaxLengthComponentBytes { get; } = "http://www.w3.org/ns/shacl#MaxLengthConstraintComponent"u8.ToArray();
    private static byte[] PatternComponentBytes { get; } = "http://www.w3.org/ns/shacl#PatternConstraintComponent"u8.ToArray();
    private static byte[] SingleLineComponentBytes { get; } = "http://www.w3.org/ns/shacl#SingleLineConstraintComponent"u8.ToArray();
    private static byte[] LanguageInComponentBytes { get; } = "http://www.w3.org/ns/shacl#LanguageInConstraintComponent"u8.ToArray();
    private static byte[] UniqueLangComponentBytes { get; } = "http://www.w3.org/ns/shacl#UniqueLangConstraintComponent"u8.ToArray();

    //Term inspection
    private static byte[] ClassComponentBytes { get; } = "http://www.w3.org/ns/shacl#ClassConstraintComponent"u8.ToArray();
    private static byte[] DatatypeComponentBytes { get; } = "http://www.w3.org/ns/shacl#DatatypeConstraintComponent"u8.ToArray();
    private static byte[] NodeKindComponentBytes { get; } = "http://www.w3.org/ns/shacl#NodeKindConstraintComponent"u8.ToArray();
    private static byte[] RootClassComponentBytes { get; } = "http://www.w3.org/ns/shacl#RootClassConstraintComponent"u8.ToArray();

    //Value set
    private static byte[] HasValueComponentBytes { get; } = "http://www.w3.org/ns/shacl#HasValueConstraintComponent"u8.ToArray();
    private static byte[] InComponentBytes { get; } = "http://www.w3.org/ns/shacl#InConstraintComponent"u8.ToArray();

    //Pair comparison
    private static byte[] EqualsComponentBytes { get; } = "http://www.w3.org/ns/shacl#EqualsConstraintComponent"u8.ToArray();
    private static byte[] DisjointComponentBytes { get; } = "http://www.w3.org/ns/shacl#DisjointConstraintComponent"u8.ToArray();
    private static byte[] LessThanComponentBytes { get; } = "http://www.w3.org/ns/shacl#LessThanConstraintComponent"u8.ToArray();
    private static byte[] LessThanOrEqualsComponentBytes { get; } = "http://www.w3.org/ns/shacl#LessThanOrEqualsConstraintComponent"u8.ToArray();

    //Boolean combinators
    private static byte[] NotComponentBytes { get; } = "http://www.w3.org/ns/shacl#NotConstraintComponent"u8.ToArray();
    private static byte[] AndComponentBytes { get; } = "http://www.w3.org/ns/shacl#AndConstraintComponent"u8.ToArray();
    private static byte[] OrComponentBytes { get; } = "http://www.w3.org/ns/shacl#OrConstraintComponent"u8.ToArray();
    private static byte[] XoneComponentBytes { get; } = "http://www.w3.org/ns/shacl#XoneConstraintComponent"u8.ToArray();

    //Shape references
    private static byte[] NodeComponentBytes { get; } = "http://www.w3.org/ns/shacl#NodeConstraintComponent"u8.ToArray();
    private static byte[] PropertyComponentBytes { get; } = "http://www.w3.org/ns/shacl#PropertyConstraintComponent"u8.ToArray();
    private static byte[] QualifiedMinCountComponentBytes { get; } = "http://www.w3.org/ns/shacl#QualifiedMinCountConstraintComponent"u8.ToArray();
    private static byte[] QualifiedMaxCountComponentBytes { get; } = "http://www.w3.org/ns/shacl#QualifiedMaxCountConstraintComponent"u8.ToArray();

    //Closed world
    private static byte[] ClosedComponentBytes { get; } = "http://www.w3.org/ns/shacl#ClosedConstraintComponent"u8.ToArray();

    //Uniqueness / other
    private static byte[] UniqueValuesForComponentBytes { get; } = "http://www.w3.org/ns/shacl#UniqueValuesForConstraintComponent"u8.ToArray();
    private static byte[] SubsetOfComponentBytes { get; } = "http://www.w3.org/ns/shacl#SubsetOfConstraintComponent"u8.ToArray();

    //Reification (SHACL 1.2)
    private static byte[] ReifierShapeComponentBytes { get; } = "http://www.w3.org/ns/shacl#ReifierShapeConstraintComponent"u8.ToArray();

    //Lists (SHACL 1.2)
    private static byte[] MemberShapeComponentBytes { get; } = "http://www.w3.org/ns/shacl#MemberShapeConstraintComponent"u8.ToArray();
    private static byte[] MinListLengthComponentBytes { get; } = "http://www.w3.org/ns/shacl#MinListLengthConstraintComponent"u8.ToArray();
    private static byte[] MaxListLengthComponentBytes { get; } = "http://www.w3.org/ns/shacl#MaxListLengthConstraintComponent"u8.ToArray();
    private static byte[] UniqueMembersComponentBytes { get; } = "http://www.w3.org/ns/shacl#UniqueMembersConstraintComponent"u8.ToArray();

    /// <summary><c>sh:MinCountConstraintComponent</c></summary>
    public static Utf8String MinCount { get; } = new(MinCountComponentBytes);

    /// <summary><c>sh:MaxCountConstraintComponent</c></summary>
    public static Utf8String MaxCount { get; } = new(MaxCountComponentBytes);

    /// <summary><c>sh:MinExclusiveConstraintComponent</c></summary>
    public static Utf8String MinExclusive { get; } = new(MinExclusiveComponentBytes);

    /// <summary><c>sh:MaxExclusiveConstraintComponent</c></summary>
    public static Utf8String MaxExclusive { get; } = new(MaxExclusiveComponentBytes);

    /// <summary><c>sh:MinInclusiveConstraintComponent</c></summary>
    public static Utf8String MinInclusive { get; } = new(MinInclusiveComponentBytes);

    /// <summary><c>sh:MaxInclusiveConstraintComponent</c></summary>
    public static Utf8String MaxInclusive { get; } = new(MaxInclusiveComponentBytes);

    /// <summary><c>sh:MinLengthConstraintComponent</c></summary>
    public static Utf8String MinLength { get; } = new(MinLengthComponentBytes);

    /// <summary><c>sh:MaxLengthConstraintComponent</c></summary>
    public static Utf8String MaxLength { get; } = new(MaxLengthComponentBytes);

    /// <summary><c>sh:PatternConstraintComponent</c></summary>
    public static Utf8String Pattern { get; } = new(PatternComponentBytes);

    /// <summary><c>sh:SingleLineConstraintComponent</c></summary>
    public static Utf8String SingleLine { get; } = new(SingleLineComponentBytes);

    /// <summary><c>sh:LanguageInConstraintComponent</c></summary>
    public static Utf8String LanguageIn { get; } = new(LanguageInComponentBytes);

    /// <summary><c>sh:UniqueLangConstraintComponent</c></summary>
    public static Utf8String UniqueLang { get; } = new(UniqueLangComponentBytes);

    /// <summary><c>sh:ClassConstraintComponent</c></summary>
    public static Utf8String Class { get; } = new(ClassComponentBytes);

    /// <summary><c>sh:DatatypeConstraintComponent</c></summary>
    public static Utf8String Datatype { get; } = new(DatatypeComponentBytes);

    /// <summary><c>sh:NodeKindConstraintComponent</c></summary>
    public static Utf8String NodeKind { get; } = new(NodeKindComponentBytes);

    /// <summary><c>sh:RootClassConstraintComponent</c></summary>
    public static Utf8String RootClass { get; } = new(RootClassComponentBytes);

    /// <summary><c>sh:HasValueConstraintComponent</c></summary>
    public static Utf8String HasValue { get; } = new(HasValueComponentBytes);

    /// <summary><c>sh:InConstraintComponent</c></summary>
    public static Utf8String In { get; } = new(InComponentBytes);

    /// <summary><c>sh:EqualsConstraintComponent</c></summary>
    public static Utf8String EqualsTo { get; } = new(EqualsComponentBytes);

    /// <summary><c>sh:DisjointConstraintComponent</c></summary>
    public static Utf8String Disjoint { get; } = new(DisjointComponentBytes);

    /// <summary><c>sh:LessThanConstraintComponent</c></summary>
    public static Utf8String LessThan { get; } = new(LessThanComponentBytes);

    /// <summary><c>sh:LessThanOrEqualsConstraintComponent</c></summary>
    public static Utf8String LessThanOrEquals { get; } = new(LessThanOrEqualsComponentBytes);

    /// <summary><c>sh:NotConstraintComponent</c></summary>
    public static Utf8String Not { get; } = new(NotComponentBytes);

    /// <summary><c>sh:AndConstraintComponent</c></summary>
    public static Utf8String And { get; } = new(AndComponentBytes);

    /// <summary><c>sh:OrConstraintComponent</c></summary>
    public static Utf8String Or { get; } = new(OrComponentBytes);

    /// <summary><c>sh:XoneConstraintComponent</c></summary>
    public static Utf8String Xone { get; } = new(XoneComponentBytes);

    /// <summary><c>sh:NodeConstraintComponent</c></summary>
    public static Utf8String Node { get; } = new(NodeComponentBytes);

    /// <summary><c>sh:PropertyConstraintComponent</c></summary>
    public static Utf8String Property { get; } = new(PropertyComponentBytes);

    /// <summary><c>sh:QualifiedMinCountConstraintComponent</c></summary>
    public static Utf8String QualifiedMinCount { get; } = new(QualifiedMinCountComponentBytes);

    /// <summary><c>sh:QualifiedMaxCountConstraintComponent</c></summary>
    public static Utf8String QualifiedMaxCount { get; } = new(QualifiedMaxCountComponentBytes);

    /// <summary><c>sh:ClosedConstraintComponent</c></summary>
    public static Utf8String Closed { get; } = new(ClosedComponentBytes);

    /// <summary><c>sh:UniqueValuesForConstraintComponent</c></summary>
    public static Utf8String UniqueValuesFor { get; } = new(UniqueValuesForComponentBytes);

    /// <summary><c>sh:SubsetOfConstraintComponent</c></summary>
    public static Utf8String SubsetOf { get; } = new(SubsetOfComponentBytes);

    /// <summary><c>sh:ReifierShapeConstraintComponent</c> (SHACL 1.2).</summary>
    public static Utf8String ReifierShape { get; } = new(ReifierShapeComponentBytes);

    /// <summary><c>sh:MemberShapeConstraintComponent</c> (SHACL 1.2).</summary>
    public static Utf8String MemberShape { get; } = new(MemberShapeComponentBytes);

    /// <summary><c>sh:MinListLengthConstraintComponent</c> (SHACL 1.2).</summary>
    public static Utf8String MinListLength { get; } = new(MinListLengthComponentBytes);

    /// <summary><c>sh:MaxListLengthConstraintComponent</c> (SHACL 1.2).</summary>
    public static Utf8String MaxListLength { get; } = new(MaxListLengthComponentBytes);

    /// <summary><c>sh:UniqueMembersConstraintComponent</c> (SHACL 1.2).</summary>
    public static Utf8String UniqueMembers { get; } = new(UniqueMembersComponentBytes);

    /// <summary><c>sh:SPARQLConstraintComponent</c> — the source constraint component of a violation produced by a <c>sh:sparql</c> constraint (SHACL-SPARQL §5.1).</summary>
    public static Utf8String SparqlConstraint { get; } = new("http://www.w3.org/ns/shacl#SPARQLConstraintComponent"u8.ToArray());
}
