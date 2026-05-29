using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Static <see cref="ConstraintComponentInfo"/> instances for the SHACL 1.2
/// Core built-in constraint components plus the SHACL 1.2 additions
/// (lists, reification, <c>sh:rootClass</c>).
/// </summary>
/// <remarks>
/// <para>
/// Exposes one static property per component. The <see cref="All"/>
/// enumeration returns every built-in for bulk registration into a
/// <see cref="ShaclComponentRegistry"/>.
/// </para>
/// <para>
/// <b>Factories are co-located.</b> Each component's metadata and its
/// construction logic are declared together. This is the Option-A
/// extension pattern applied to the built-ins themselves: a single
/// registration is everything the shape loader needs to produce the
/// correct AST record. Every factory is a <see langword="static"/>
/// lambda — no captures, no per-invocation allocation — that pulls
/// parsed values from a <see cref="ParameterBag"/> using the
/// <see cref="Utf8String"/>-keyed companion accessors and constructs
/// the corresponding record.
/// </para>
/// <para>
/// <b>Naming.</b> Property names match the SHACL Core section headings
/// rather than any single parameter. <see cref="Pattern"/> is
/// <c>sh:PatternConstraintComponent</c> with primary <c>sh:pattern</c>
/// and optional companions <c>sh:flags</c> and <c>sh:singleLine</c>.
/// Qualified-value-shape splits into two sibling components
/// (<see cref="QualifiedMinCount"/> and <see cref="QualifiedMaxCount"/>),
/// each producing its own constraint record
/// (<see cref="QualifiedMinCountConstraint"/> and
/// <see cref="QualifiedMaxCountConstraint"/> respectively) carrying
/// the inner-shape reference and the disjoint flag alongside its own
/// count. The loader emits one record per primary occurrence; a
/// property shape declaring both counts produces both records.
/// </para>
/// </remarks>
public static class ShaclBuiltInComponents
{
    //Cardinality (§6.5)

    /// <summary><c>sh:MinCountConstraintComponent</c> — primary <c>sh:minCount</c>.</summary>
    public static ConstraintComponentInfo MinCount { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MinCount,
        ShaclConstraintVocabulary.MinCount,
        static bag => new MinCountConstraint(bag.RequirePrimaryInt()));

    /// <summary><c>sh:MaxCountConstraintComponent</c> — primary <c>sh:maxCount</c>.</summary>
    public static ConstraintComponentInfo MaxCount { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MaxCount,
        ShaclConstraintVocabulary.MaxCount,
        static bag => new MaxCountConstraint(bag.RequirePrimaryInt()));

    //Value range (§6.2)

    /// <summary><c>sh:MinExclusiveConstraintComponent</c> — primary <c>sh:minExclusive</c>.</summary>
    public static ConstraintComponentInfo MinExclusive { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MinExclusive,
        ShaclConstraintVocabulary.MinExclusive,
        static bag => new MinExclusiveConstraint(bag.RequirePrimaryTerm()));

    /// <summary><c>sh:MaxExclusiveConstraintComponent</c> — primary <c>sh:maxExclusive</c>.</summary>
    public static ConstraintComponentInfo MaxExclusive { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MaxExclusive,
        ShaclConstraintVocabulary.MaxExclusive,
        static bag => new MaxExclusiveConstraint(bag.RequirePrimaryTerm()));

    /// <summary><c>sh:MinInclusiveConstraintComponent</c> — primary <c>sh:minInclusive</c>.</summary>
    public static ConstraintComponentInfo MinInclusive { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MinInclusive,
        ShaclConstraintVocabulary.MinInclusive,
        static bag => new MinInclusiveConstraint(bag.RequirePrimaryTerm()));

    /// <summary><c>sh:MaxInclusiveConstraintComponent</c> — primary <c>sh:maxInclusive</c>.</summary>
    public static ConstraintComponentInfo MaxInclusive { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MaxInclusive,
        ShaclConstraintVocabulary.MaxInclusive,
        static bag => new MaxInclusiveConstraint(bag.RequirePrimaryTerm()));

    //String / literal (§6.3)

    /// <summary><c>sh:MinLengthConstraintComponent</c> — primary <c>sh:minLength</c>.</summary>
    public static ConstraintComponentInfo MinLength { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MinLength,
        ShaclConstraintVocabulary.MinLength,
        static bag => new MinLengthConstraint(bag.RequirePrimaryInt()));

    /// <summary><c>sh:MaxLengthConstraintComponent</c> — primary <c>sh:maxLength</c>.</summary>
    public static ConstraintComponentInfo MaxLength { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MaxLength,
        ShaclConstraintVocabulary.MaxLength,
        static bag => new MaxLengthConstraint(bag.RequirePrimaryInt()));

    /// <summary>
    /// <c>sh:PatternConstraintComponent</c> — primary <c>sh:pattern</c>,
    /// optional companions <c>sh:flags</c> and <c>sh:singleLine</c>.
    /// The regex is compiled via <see cref="ParameterBag.CompilePattern"/>,
    /// which consults the user resolver first, then the session memo, and
    /// finally falls back to <c>RegexOptions.NonBacktracking</c> for ReDoS
    /// safety on untrusted input.
    /// </summary>
    public static ConstraintComponentInfo Pattern { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Pattern,
        ShaclConstraintVocabulary.Pattern,
        static bag =>
        {
            string pattern = bag.RequirePrimaryString();
            string? flags = bag.OptionalString(ShaclConstraintVocabulary.Flags);
            bool singleLine = bag.OptionalBool(ShaclConstraintVocabulary.SingleLine) ?? false;
            Regex compiled = bag.CompilePattern(pattern, flags, singleLine);
            return new PatternConstraint(pattern, flags, singleLine, compiled);
        },
        ShaclConstraintVocabulary.Flags,
        ShaclConstraintVocabulary.SingleLine);

    /// <summary>
    /// <c>sh:SingleLineConstraintComponent</c> — primary <c>sh:singleLine</c>.
    /// Applies when <c>sh:singleLine</c> appears without <c>sh:pattern</c>;
    /// with a pattern, <c>sh:singleLine</c> is folded into
    /// <see cref="Pattern"/>.
    /// </summary>
    public static ConstraintComponentInfo SingleLine { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.SingleLine,
        ShaclConstraintVocabulary.SingleLine,
        static bag => new SingleLineConstraint(bag.RequirePrimaryBool()));

    /// <summary><c>sh:LanguageInConstraintComponent</c> — primary <c>sh:languageIn</c>.</summary>
    public static ConstraintComponentInfo LanguageIn { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.LanguageIn,
        ShaclConstraintVocabulary.LanguageIn,
        static bag => new LanguageInConstraint(bag.RequirePrimaryRdfListOfUtf8Strings()));

    /// <summary><c>sh:UniqueLangConstraintComponent</c> — primary <c>sh:uniqueLang</c>.</summary>
    public static ConstraintComponentInfo UniqueLang { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.UniqueLang,
        ShaclConstraintVocabulary.UniqueLang,
        static bag => new UniqueLanguageConstraint(bag.RequirePrimaryBool()));

    //Term inspection (§6.1)

    /// <summary>
    /// <c>sh:ClassConstraintComponent</c> — primary <c>sh:class</c>.
    /// The constraint captures the class IRI plus the <c>rdf:type</c>
    /// and <c>rdfs:subClassOf</c> predicate ids from the bag's RDFS
    /// vocabulary so the evaluator can walk the class hierarchy without
    /// re-resolving those fixed terms.
    /// </summary>
    public static ConstraintComponentInfo Class { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Class,
        ShaclConstraintVocabulary.Class,
        static bag => new ClassConstraint(
            ClassId: bag.RequirePrimaryIri(),
            RdfTypeId: bag.RdfsVocabulary.RdfType,
            RdfsSubClassOfId: bag.RdfsVocabulary.RdfsSubClassOf));

    /// <summary><c>sh:DatatypeConstraintComponent</c> — primary <c>sh:datatype</c>.</summary>
    public static ConstraintComponentInfo Datatype { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Datatype,
        ShaclConstraintVocabulary.Datatype,
        static bag => new DatatypeConstraint(bag.RequirePrimaryIri()));

    /// <summary><c>sh:NodeKindConstraintComponent</c> — primary <c>sh:nodeKind</c>.</summary>
    public static ConstraintComponentInfo NodeKind { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.NodeKind,
        ShaclConstraintVocabulary.NodeKind,
        static bag => new NodeKindConstraint(bag.RequirePrimaryNodeKind()));

    /// <summary>
    /// <c>sh:RootClassConstraintComponent</c> — primary <c>sh:rootClass</c>
    /// (SHACL 1.2). Like <see cref="Class"/> but semantically narrower.
    /// </summary>
    public static ConstraintComponentInfo RootClass { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.RootClass,
        ShaclConstraintVocabulary.RootClass,
        static bag => new RootClassConstraint(
            ClassId: bag.RequirePrimaryIri(),
            RdfTypeId: bag.RdfsVocabulary.RdfType,
            RdfsSubClassOfId: bag.RdfsVocabulary.RdfsSubClassOf));

    //Value set (§6.4)

    /// <summary><c>sh:HasValueConstraintComponent</c> — primary <c>sh:hasValue</c>.</summary>
    public static ConstraintComponentInfo HasValue { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.HasValue,
        ShaclConstraintVocabulary.HasValue,
        static bag => new HasValueConstraint(bag.RequirePrimaryTerm()));

    /// <summary><c>sh:InConstraintComponent</c> — primary <c>sh:in</c>.</summary>
    public static ConstraintComponentInfo In { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.In,
        ShaclConstraintVocabulary.In,
        static bag => new InConstraint(bag.RequirePrimaryRdfListOfTerms()));

    //Pair comparison (§6.7)

    /// <summary><c>sh:EqualsConstraintComponent</c> — primary <c>sh:equals</c>.</summary>
    public static ConstraintComponentInfo EqualsTo { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.EqualsTo,
        ShaclConstraintVocabulary.EqualsTo,
        static bag => new EqualsToConstraint(bag.RequirePrimaryIri()));

    /// <summary><c>sh:DisjointConstraintComponent</c> — primary <c>sh:disjoint</c>.</summary>
    public static ConstraintComponentInfo Disjoint { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Disjoint,
        ShaclConstraintVocabulary.Disjoint,
        static bag => new DisjointConstraint(bag.RequirePrimaryIri()));

    /// <summary><c>sh:LessThanConstraintComponent</c> — primary <c>sh:lessThan</c>.</summary>
    public static ConstraintComponentInfo LessThan { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.LessThan,
        ShaclConstraintVocabulary.LessThan,
        static bag => new LessThanConstraint(bag.RequirePrimaryIri()));

    /// <summary><c>sh:LessThanOrEqualsConstraintComponent</c> — primary <c>sh:lessThanOrEquals</c>.</summary>
    public static ConstraintComponentInfo LessThanOrEquals { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.LessThanOrEquals,
        ShaclConstraintVocabulary.LessThanOrEquals,
        static bag => new LessThanOrEqualsConstraint(bag.RequirePrimaryIri()));

    //Boolean combinators (§6.8)

    /// <summary><c>sh:NotConstraintComponent</c> — primary <c>sh:not</c>.</summary>
    public static ConstraintComponentInfo Not { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Not,
        ShaclConstraintVocabulary.Not,
        static bag => new NotConstraint(bag.RequirePrimaryShapeId()));

    /// <summary><c>sh:AndConstraintComponent</c> — primary <c>sh:and</c>.</summary>
    public static ConstraintComponentInfo And { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.And,
        ShaclConstraintVocabulary.And,
        static bag => new AndConstraint(bag.RequirePrimaryRdfListOfShapeIds()));

    /// <summary><c>sh:OrConstraintComponent</c> — primary <c>sh:or</c>.</summary>
    public static ConstraintComponentInfo Or { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Or,
        ShaclConstraintVocabulary.Or,
        static bag => new OrConstraint(bag.RequirePrimaryRdfListOfShapeIds()));

    /// <summary><c>sh:XoneConstraintComponent</c> — primary <c>sh:xone</c>.</summary>
    public static ConstraintComponentInfo Xone { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Xone,
        ShaclConstraintVocabulary.Xone,
        static bag => new XoneConstraint(bag.RequirePrimaryRdfListOfShapeIds()));

    //Shape references (§6.9)

    /// <summary><c>sh:NodeConstraintComponent</c> — primary <c>sh:node</c>.</summary>
    public static ConstraintComponentInfo Node { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Node,
        ShaclConstraintVocabulary.Node,
        static bag => new NodeConstraint(bag.RequirePrimaryShapeId()));

    /// <summary>
    /// <c>sh:PropertyConstraintComponent</c> — primary <c>sh:property</c>.
    /// The referenced shape is expected to be a
    /// <see cref="PropertyShape"/>; the factory captures its term id
    /// without dereferencing, and the evaluator verifies the kind at
    /// validation time.
    /// </summary>
    public static ConstraintComponentInfo Property { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Property,
        ShaclConstraintVocabulary.Property,
        static bag => new PropertyConstraint(bag.RequirePrimaryShapeId()));

    /// <summary>
    /// <c>sh:QualifiedMinCountConstraintComponent</c> — primary
    /// <c>sh:qualifiedMinCount</c>, companions
    /// <c>sh:qualifiedValueShape</c> and
    /// <c>sh:qualifiedValueShapesDisjoint</c>.
    /// </summary>
    public static ConstraintComponentInfo QualifiedMinCount { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.QualifiedMinCount,
        ShaclConstraintVocabulary.QualifiedMinCount,
        static bag => bag.OptionalShapeId(ShaclConstraintVocabulary.QualifiedValueShape) is TermId minValueShape
            ? new QualifiedMinCountConstraint(
                ValueShapeId: minValueShape,
                MinCount: bag.RequirePrimaryInt(),
                Disjoint: bag.OptionalBool(ShaclConstraintVocabulary.QualifiedValueShapesDisjoint) ?? false)
            : null,
        ShaclConstraintVocabulary.QualifiedValueShape,
        ShaclConstraintVocabulary.QualifiedValueShapesDisjoint);

    /// <summary>
    /// <c>sh:QualifiedMaxCountConstraintComponent</c> — primary
    /// <c>sh:qualifiedMaxCount</c>, companions
    /// <c>sh:qualifiedValueShape</c> and
    /// <c>sh:qualifiedValueShapesDisjoint</c>.
    /// </summary>
    public static ConstraintComponentInfo QualifiedMaxCount { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.QualifiedMaxCount,
        ShaclConstraintVocabulary.QualifiedMaxCount,
        static bag => bag.OptionalShapeId(ShaclConstraintVocabulary.QualifiedValueShape) is TermId maxValueShape
            ? new QualifiedMaxCountConstraint(
                ValueShapeId: maxValueShape,
                MaxCount: bag.RequirePrimaryInt(),
                Disjoint: bag.OptionalBool(ShaclConstraintVocabulary.QualifiedValueShapesDisjoint) ?? false)
            : null,
        ShaclConstraintVocabulary.QualifiedValueShape,
        ShaclConstraintVocabulary.QualifiedValueShapesDisjoint);

    //Closed world (§6.10)

    /// <summary>
    /// <c>sh:ClosedConstraintComponent</c> — primary <c>sh:closed</c>,
    /// optional companion <c>sh:ignoredProperties</c>.
    /// </summary>
    public static ConstraintComponentInfo Closed { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.Closed,
        ShaclConstraintVocabulary.Closed,
        static bag => new ClosedConstraint(
            Closed: bag.RequirePrimaryBool(),
            IgnoredPredicateIds: bag.OptionalRdfListOfIris(ShaclConstraintVocabulary.IgnoredProperties)
                ?? ImmutableArray<IriId>.Empty),
        ShaclConstraintVocabulary.IgnoredProperties);

    //Uniqueness / other

    /// <summary><c>sh:UniqueValuesForConstraintComponent</c> — primary <c>sh:uniqueValuesFor</c>.</summary>
    public static ConstraintComponentInfo UniqueValuesFor { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.UniqueValuesFor,
        ShaclConstraintVocabulary.UniqueValuesFor,
        static bag => new UniqueValuesForConstraint(bag.RequirePrimaryRdfListOfIris()));

    /// <summary><c>sh:SubsetOfConstraintComponent</c> — primary <c>sh:subsetOf</c>.</summary>
    public static ConstraintComponentInfo SubsetOf { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.SubsetOf,
        ShaclConstraintVocabulary.SubsetOf,
        static bag => new SubsetOfConstraint(bag.RequirePrimaryIri()));

    //Reification (SHACL 1.2)

    /// <summary>
    /// <c>sh:ReifierShapeConstraintComponent</c> — primary
    /// <c>sh:reifierShape</c>, optional companion
    /// <c>sh:reificationRequired</c> (SHACL 1.2).
    /// </summary>
    public static ConstraintComponentInfo ReifierShape { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.ReifierShape,
        ShaclConstraintVocabulary.ReifierShape,
        static bag => new ReifierShapeConstraint(
            ReifierShapeId: bag.RequirePrimaryShapeId(),
            ReificationRequired: bag.OptionalBool(ShaclConstraintVocabulary.ReificationRequired) ?? false),
        ShaclConstraintVocabulary.ReificationRequired);

    //Lists (SHACL 1.2)

    /// <summary><c>sh:MemberShapeConstraintComponent</c> — primary <c>sh:memberShape</c> (SHACL 1.2).</summary>
    public static ConstraintComponentInfo MemberShape { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MemberShape,
        ShaclConstraintVocabulary.MemberShape,
        static bag => new MemberShapeConstraint(bag.RequirePrimaryShapeId()));

    /// <summary><c>sh:MinListLengthConstraintComponent</c> — primary <c>sh:minListLength</c> (SHACL 1.2).</summary>
    public static ConstraintComponentInfo MinListLength { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MinListLength,
        ShaclConstraintVocabulary.MinListLength,
        static bag => new MinListLengthConstraint(bag.RequirePrimaryInt()));

    /// <summary><c>sh:MaxListLengthConstraintComponent</c> — primary <c>sh:maxListLength</c> (SHACL 1.2).</summary>
    public static ConstraintComponentInfo MaxListLength { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.MaxListLength,
        ShaclConstraintVocabulary.MaxListLength,
        static bag => new MaxListLengthConstraint(bag.RequirePrimaryInt()));

    /// <summary><c>sh:UniqueMembersConstraintComponent</c> — primary <c>sh:uniqueMembers</c> (SHACL 1.2).</summary>
    public static ConstraintComponentInfo UniqueMembers { get; } = ConstraintComponentInfo.Create(
        ShaclComponentVocabulary.UniqueMembers,
        ShaclConstraintVocabulary.UniqueMembers,
        static bag => new UniqueMembersConstraint(bag.RequirePrimaryBool()));

    //Aggregate enumeration

    private static ImmutableArray<ConstraintComponentInfo> AllBuiltIns { get; } = ImmutableArray.Create(
        //Cardinality
        MinCount, MaxCount,
        //Value range
        MinExclusive, MaxExclusive, MinInclusive, MaxInclusive,
        //String / literal
        MinLength, MaxLength, Pattern, SingleLine, LanguageIn, UniqueLang,
        //Term inspection
        Class, Datatype, NodeKind, RootClass,
        //Value set
        HasValue, In,
        //Pair comparison
        EqualsTo, Disjoint, LessThan, LessThanOrEquals,
        //Boolean combinators
        Not, And, Or, Xone,
        //Shape references
        Node, Property, QualifiedMinCount, QualifiedMaxCount,
        //Closed world
        Closed,
        //Uniqueness / other
        UniqueValuesFor, SubsetOf,
        //SHACL 1.2 reification / lists
        ReifierShape, MemberShape, MinListLength, MaxListLength, UniqueMembers);

    /// <summary>
    /// Every built-in constraint-component info, suitable for bulk
    /// registration into a <see cref="ShaclComponentRegistry"/>.
    /// </summary>
    public static IReadOnlyList<ConstraintComponentInfo> All => AllBuiltIns;
}
