using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>A frame or document section keyword, as a closed dispatch discriminant.</summary>
internal enum OwlManchesterSection
{
    /// <summary>The text names no section keyword.</summary>
    Unknown = 0,

    /// <summary>The <c>Annotations:</c> section.</summary>
    Annotations,

    /// <summary>The <c>SubClassOf:</c> section.</summary>
    SubClassOf,

    /// <summary>The <c>EquivalentTo:</c> section.</summary>
    EquivalentTo,

    /// <summary>The <c>DisjointWith:</c> section.</summary>
    DisjointWith,

    /// <summary>The <c>DisjointUnionOf:</c> section.</summary>
    DisjointUnionOf,

    /// <summary>The <c>HasKey:</c> section.</summary>
    HasKey,

    /// <summary>The <c>Domain:</c> section.</summary>
    Domain,

    /// <summary>The <c>Range:</c> section.</summary>
    Range,

    /// <summary>The <c>Characteristics:</c> section.</summary>
    Characteristics,

    /// <summary>The <c>SubPropertyOf:</c> section.</summary>
    SubPropertyOf,

    /// <summary>The <c>InverseOf:</c> section.</summary>
    InverseOf,

    /// <summary>The <c>SubPropertyChain:</c> section.</summary>
    SubPropertyChain,

    /// <summary>The <c>Types:</c> section.</summary>
    Types,

    /// <summary>The <c>Facts:</c> section.</summary>
    Facts,

    /// <summary>The <c>SameAs:</c> section.</summary>
    SameAs,

    /// <summary>The <c>DifferentFrom:</c> section.</summary>
    DifferentFrom
}

/// <summary>An expression operator word, as a closed dispatch discriminant.</summary>
internal enum OwlManchesterOperator
{
    /// <summary>The text names no operator word.</summary>
    None = 0,

    /// <summary>The <c>and</c> conjunction.</summary>
    And,

    /// <summary>The <c>or</c> disjunction.</summary>
    Or,

    /// <summary>The <c>not</c> negation.</summary>
    Not,

    /// <summary>The <c>that</c> conjunction separator.</summary>
    That,

    /// <summary>The <c>some</c> existential restriction.</summary>
    Some,

    /// <summary>The <c>only</c> universal restriction.</summary>
    Only,

    /// <summary>The <c>value</c> has-value restriction.</summary>
    Value,

    /// <summary>The <c>inverse</c> property modifier.</summary>
    Inverse,

    /// <summary>The <c>o</c> property-chain composition.</summary>
    Chain,

    /// <summary>The <c>min</c> minimum cardinality.</summary>
    Min,

    /// <summary>The <c>max</c> maximum cardinality.</summary>
    Max,

    /// <summary>The <c>exactly</c> exact cardinality.</summary>
    Exactly,

    /// <summary>The <c>Self</c> self restriction.</summary>
    Self
}

/// <summary>An entity-free n-ary frame keyword, as a closed dispatch discriminant.</summary>
internal enum OwlManchesterMiscFrame
{
    /// <summary>The text names no misc frame keyword.</summary>
    Unknown = 0,

    /// <summary>The <c>EquivalentClasses:</c> frame.</summary>
    EquivalentClasses,

    /// <summary>The <c>DisjointClasses:</c> frame.</summary>
    DisjointClasses,

    /// <summary>The <c>EquivalentProperties:</c> frame.</summary>
    EquivalentProperties,

    /// <summary>The <c>DisjointProperties:</c> frame.</summary>
    DisjointProperties,

    /// <summary>The <c>SameIndividual:</c> frame.</summary>
    SameIndividual,

    /// <summary>The <c>DifferentIndividuals:</c> frame.</summary>
    DifferentIndividuals
}

/// <summary>
/// The reserved word sets of the Manchester syntax — frame keywords, section
/// keywords, expression operators, property characteristics, facet names, and
/// built-in datatype names — as UTF-8 byte sequences, in one canonical home for
/// the lexer, the converter, and the writer.
/// </summary>
/// <remarks>
/// The keyword bytes are <c>u8</c> literals (the source of truth the writer
/// emits and the reader/converter match with
/// <see cref="System.MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>);
/// the word→kind and word→IRI tables are byte-keyed frozen dictionaries. Frame
/// and section keywords carry a trailing <c>:</c> baked into the literal; the
/// expression operators and the property characteristics do not.
/// </remarks>
internal static class OwlManchesterWords
{
    /// <summary>The <c>Prefix:</c> document keyword.</summary>
    public static ReadOnlySpan<byte> PrefixKeyword => "Prefix:"u8;

    /// <summary>The <c>Ontology:</c> document keyword.</summary>
    public static ReadOnlySpan<byte> OntologyKeyword => "Ontology:"u8;

    /// <summary>The <c>Import:</c> document keyword.</summary>
    public static ReadOnlySpan<byte> ImportKeyword => "Import:"u8;

    /// <summary>The <c>Annotations:</c> keyword, both the ontology-level block and the frame section.</summary>
    public static ReadOnlySpan<byte> AnnotationsKeyword => "Annotations:"u8;

    /// <summary>The <c>Class:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> ClassFrame => "Class:"u8;

    /// <summary>The <c>Datatype:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> DatatypeFrame => "Datatype:"u8;

    /// <summary>The <c>ObjectProperty:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyFrame => "ObjectProperty:"u8;

    /// <summary>The <c>DataProperty:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> DataPropertyFrame => "DataProperty:"u8;

    /// <summary>The <c>AnnotationProperty:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> AnnotationPropertyFrame => "AnnotationProperty:"u8;

    /// <summary>The <c>Individual:</c> entity frame keyword.</summary>
    public static ReadOnlySpan<byte> IndividualFrame => "Individual:"u8;

    /// <summary>The <c>EquivalentClasses:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> EquivalentClassesFrame => "EquivalentClasses:"u8;

    /// <summary>The <c>DisjointClasses:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> DisjointClassesFrame => "DisjointClasses:"u8;

    /// <summary>The <c>EquivalentProperties:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> EquivalentPropertiesFrame => "EquivalentProperties:"u8;

    /// <summary>The <c>DisjointProperties:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> DisjointPropertiesFrame => "DisjointProperties:"u8;

    /// <summary>The <c>SameIndividual:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> SameIndividualFrame => "SameIndividual:"u8;

    /// <summary>The <c>DifferentIndividuals:</c> misc frame keyword.</summary>
    public static ReadOnlySpan<byte> DifferentIndividualsFrame => "DifferentIndividuals:"u8;

    /// <summary>The <c>SubClassOf:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> SubClassOfSection => "SubClassOf:"u8;

    /// <summary>The <c>EquivalentTo:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> EquivalentToSection => "EquivalentTo:"u8;

    /// <summary>The <c>DisjointWith:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> DisjointWithSection => "DisjointWith:"u8;

    /// <summary>The <c>DisjointUnionOf:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> DisjointUnionOfSection => "DisjointUnionOf:"u8;

    /// <summary>The <c>HasKey:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> HasKeySection => "HasKey:"u8;

    /// <summary>The <c>Domain:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> DomainSection => "Domain:"u8;

    /// <summary>The <c>Range:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> RangeSection => "Range:"u8;

    /// <summary>The <c>Characteristics:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> CharacteristicsSection => "Characteristics:"u8;

    /// <summary>The <c>SubPropertyOf:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> SubPropertyOfSection => "SubPropertyOf:"u8;

    /// <summary>The <c>InverseOf:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> InverseOfSection => "InverseOf:"u8;

    /// <summary>The <c>SubPropertyChain:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> SubPropertyChainSection => "SubPropertyChain:"u8;

    /// <summary>The <c>Types:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> TypesSection => "Types:"u8;

    /// <summary>The <c>Facts:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> FactsSection => "Facts:"u8;

    /// <summary>The <c>SameAs:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> SameAsSection => "SameAs:"u8;

    /// <summary>The <c>DifferentFrom:</c> section keyword.</summary>
    public static ReadOnlySpan<byte> DifferentFromSection => "DifferentFrom:"u8;

    /// <summary>The <c>and</c> operator word.</summary>
    public static ReadOnlySpan<byte> AndWord => "and"u8;

    /// <summary>The <c>or</c> operator word.</summary>
    public static ReadOnlySpan<byte> OrWord => "or"u8;

    /// <summary>The <c>not</c> operator word.</summary>
    public static ReadOnlySpan<byte> NotWord => "not"u8;

    /// <summary>The <c>that</c> operator word.</summary>
    public static ReadOnlySpan<byte> ThatWord => "that"u8;

    /// <summary>The <c>some</c> operator word.</summary>
    public static ReadOnlySpan<byte> SomeWord => "some"u8;

    /// <summary>The <c>only</c> operator word.</summary>
    public static ReadOnlySpan<byte> OnlyWord => "only"u8;

    /// <summary>The <c>value</c> operator word.</summary>
    public static ReadOnlySpan<byte> ValueWord => "value"u8;

    /// <summary>The <c>inverse</c> operator word.</summary>
    public static ReadOnlySpan<byte> InverseWord => "inverse"u8;

    /// <summary>The <c>o</c> property-chain composition word.</summary>
    public static ReadOnlySpan<byte> ChainWord => "o"u8;

    /// <summary>The <c>min</c> operator word.</summary>
    public static ReadOnlySpan<byte> MinWord => "min"u8;

    /// <summary>The <c>max</c> operator word.</summary>
    public static ReadOnlySpan<byte> MaxWord => "max"u8;

    /// <summary>The <c>exactly</c> operator word.</summary>
    public static ReadOnlySpan<byte> ExactlyWord => "exactly"u8;

    /// <summary>The <c>Self</c> restriction word.</summary>
    public static ReadOnlySpan<byte> SelfWord => "Self"u8;

    /// <summary>The <c>Functional</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> FunctionalWord => "Functional"u8;

    /// <summary>The <c>InverseFunctional</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> InverseFunctionalWord => "InverseFunctional"u8;

    /// <summary>The <c>Reflexive</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> ReflexiveWord => "Reflexive"u8;

    /// <summary>The <c>Irreflexive</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> IrreflexiveWord => "Irreflexive"u8;

    /// <summary>The <c>Symmetric</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> SymmetricWord => "Symmetric"u8;

    /// <summary>The <c>Asymmetric</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> AsymmetricWord => "Asymmetric"u8;

    /// <summary>The <c>Transitive</c> characteristic word.</summary>
    public static ReadOnlySpan<byte> TransitiveWord => "Transitive"u8;

    /// <summary>The entity-frame keywords, keyed to the entity kind each declares.</summary>
    public static FrozenDictionary<Utf8String, OwlEntityKind> EntityFrames { get; } = new Dictionary<Utf8String, OwlEntityKind>
    {
        [new Utf8String(ClassFrame.ToArray())] = OwlEntityKind.Class,
        [new Utf8String(DatatypeFrame.ToArray())] = OwlEntityKind.Datatype,
        [new Utf8String(ObjectPropertyFrame.ToArray())] = OwlEntityKind.ObjectProperty,
        [new Utf8String(DataPropertyFrame.ToArray())] = OwlEntityKind.DataProperty,
        [new Utf8String(AnnotationPropertyFrame.ToArray())] = OwlEntityKind.AnnotationProperty,
        [new Utf8String(IndividualFrame.ToArray())] = OwlEntityKind.NamedIndividual,
    }.ToFrozenDictionary();

    /// <summary>The keywords of the entity-free n-ary frames, keyed to their discriminant.</summary>
    public static FrozenDictionary<Utf8String, OwlManchesterMiscFrame> MiscFrameKeywords { get; } = new Dictionary<Utf8String, OwlManchesterMiscFrame>
    {
        [new Utf8String(EquivalentClassesFrame.ToArray())] = OwlManchesterMiscFrame.EquivalentClasses,
        [new Utf8String(DisjointClassesFrame.ToArray())] = OwlManchesterMiscFrame.DisjointClasses,
        [new Utf8String(EquivalentPropertiesFrame.ToArray())] = OwlManchesterMiscFrame.EquivalentProperties,
        [new Utf8String(DisjointPropertiesFrame.ToArray())] = OwlManchesterMiscFrame.DisjointProperties,
        [new Utf8String(SameIndividualFrame.ToArray())] = OwlManchesterMiscFrame.SameIndividual,
        [new Utf8String(DifferentIndividualsFrame.ToArray())] = OwlManchesterMiscFrame.DifferentIndividuals,
    }.ToFrozenDictionary();

    /// <summary>The section keywords that may appear inside a frame, keyed to their discriminant.</summary>
    private static FrozenDictionary<Utf8String, OwlManchesterSection> SectionKeywords { get; } = new Dictionary<Utf8String, OwlManchesterSection>
    {
        [new Utf8String(AnnotationsKeyword.ToArray())] = OwlManchesterSection.Annotations,
        [new Utf8String(SubClassOfSection.ToArray())] = OwlManchesterSection.SubClassOf,
        [new Utf8String(EquivalentToSection.ToArray())] = OwlManchesterSection.EquivalentTo,
        [new Utf8String(DisjointWithSection.ToArray())] = OwlManchesterSection.DisjointWith,
        [new Utf8String(DisjointUnionOfSection.ToArray())] = OwlManchesterSection.DisjointUnionOf,
        [new Utf8String(HasKeySection.ToArray())] = OwlManchesterSection.HasKey,
        [new Utf8String(DomainSection.ToArray())] = OwlManchesterSection.Domain,
        [new Utf8String(RangeSection.ToArray())] = OwlManchesterSection.Range,
        [new Utf8String(CharacteristicsSection.ToArray())] = OwlManchesterSection.Characteristics,
        [new Utf8String(SubPropertyOfSection.ToArray())] = OwlManchesterSection.SubPropertyOf,
        [new Utf8String(InverseOfSection.ToArray())] = OwlManchesterSection.InverseOf,
        [new Utf8String(SubPropertyChainSection.ToArray())] = OwlManchesterSection.SubPropertyChain,
        [new Utf8String(TypesSection.ToArray())] = OwlManchesterSection.Types,
        [new Utf8String(FactsSection.ToArray())] = OwlManchesterSection.Facts,
        [new Utf8String(SameAsSection.ToArray())] = OwlManchesterSection.SameAs,
        [new Utf8String(DifferentFromSection.ToArray())] = OwlManchesterSection.DifferentFrom,
    }.ToFrozenDictionary();

    /// <summary>The expression operator words, keyed to their discriminant.</summary>
    private static FrozenDictionary<Utf8String, OwlManchesterOperator> OperatorWords { get; } = new Dictionary<Utf8String, OwlManchesterOperator>
    {
        [new Utf8String(AndWord.ToArray())] = OwlManchesterOperator.And,
        [new Utf8String(OrWord.ToArray())] = OwlManchesterOperator.Or,
        [new Utf8String(NotWord.ToArray())] = OwlManchesterOperator.Not,
        [new Utf8String(ThatWord.ToArray())] = OwlManchesterOperator.That,
        [new Utf8String(SomeWord.ToArray())] = OwlManchesterOperator.Some,
        [new Utf8String(OnlyWord.ToArray())] = OwlManchesterOperator.Only,
        [new Utf8String(ValueWord.ToArray())] = OwlManchesterOperator.Value,
        [new Utf8String(InverseWord.ToArray())] = OwlManchesterOperator.Inverse,
        [new Utf8String(ChainWord.ToArray())] = OwlManchesterOperator.Chain,
        [new Utf8String(MinWord.ToArray())] = OwlManchesterOperator.Min,
        [new Utf8String(MaxWord.ToArray())] = OwlManchesterOperator.Max,
        [new Utf8String(ExactlyWord.ToArray())] = OwlManchesterOperator.Exactly,
        [new Utf8String(SelfWord.ToArray())] = OwlManchesterOperator.Self,
    }.ToFrozenDictionary();

    /// <summary>The continuation operator words (those of the <c>Operators</c> set, <c>Self</c> excluded).</summary>
    private static FrozenSet<Utf8String> ContinuationOperators { get; } = new HashSet<Utf8String>
    {
        new(AndWord.ToArray()), new(OrWord.ToArray()), new(NotWord.ToArray()), new(ThatWord.ToArray()),
        new(SomeWord.ToArray()), new(OnlyWord.ToArray()), new(ValueWord.ToArray()), new(InverseWord.ToArray()),
        new(ChainWord.ToArray()), new(MinWord.ToArray()), new(MaxWord.ToArray()), new(ExactlyWord.ToArray()),
    }.ToFrozenSet();

    /// <summary>The property characteristic words, keyed to the characteristic each names.</summary>
    public static FrozenDictionary<Utf8String, OwlPropertyCharacteristic> Characteristics { get; } = new Dictionary<Utf8String, OwlPropertyCharacteristic>
    {
        [new Utf8String(FunctionalWord.ToArray())] = OwlPropertyCharacteristic.Functional,
        [new Utf8String(InverseFunctionalWord.ToArray())] = OwlPropertyCharacteristic.InverseFunctional,
        [new Utf8String(ReflexiveWord.ToArray())] = OwlPropertyCharacteristic.Reflexive,
        [new Utf8String(IrreflexiveWord.ToArray())] = OwlPropertyCharacteristic.Irreflexive,
        [new Utf8String(SymmetricWord.ToArray())] = OwlPropertyCharacteristic.Symmetric,
        [new Utf8String(AsymmetricWord.ToArray())] = OwlPropertyCharacteristic.Asymmetric,
        [new Utf8String(TransitiveWord.ToArray())] = OwlPropertyCharacteristic.Transitive,
    }.ToFrozenDictionary();

    /// <summary>The named facet words, keyed to the constraining-facet IRI each abbreviates.</summary>
    public static FrozenDictionary<Utf8String, Utf8String> NamedFacets { get; } = new Dictionary<Utf8String, Utf8String>
    {
        [new Utf8String("length"u8.ToArray())] = Vocabulary.XsdFacets.Length,
        [new Utf8String("minLength"u8.ToArray())] = Vocabulary.XsdFacets.MinLength,
        [new Utf8String("maxLength"u8.ToArray())] = Vocabulary.XsdFacets.MaxLength,
        [new Utf8String("pattern"u8.ToArray())] = Vocabulary.XsdFacets.Pattern,
        [new Utf8String("langRange"u8.ToArray())] = new("http://www.w3.org/1999/02/22-rdf-syntax-ns#langRange"u8.ToArray()),
    }.ToFrozenDictionary();

    /// <summary>The comparison facet operators, keyed to the constraining-facet IRI each abbreviates.</summary>
    public static FrozenDictionary<Utf8String, Utf8String> ComparisonFacets { get; } = new Dictionary<Utf8String, Utf8String>
    {
        [new Utf8String("<="u8.ToArray())] = Vocabulary.XsdFacets.MaxInclusive,
        [new Utf8String("<"u8.ToArray())] = Vocabulary.XsdFacets.MaxExclusive,
        [new Utf8String(">="u8.ToArray())] = Vocabulary.XsdFacets.MinInclusive,
        [new Utf8String(">"u8.ToArray())] = Vocabulary.XsdFacets.MinExclusive,
    }.ToFrozenDictionary();

    /// <summary>The built-in datatype abbreviations, keyed to the datatype IRI each names.</summary>
    public static FrozenDictionary<Utf8String, Utf8String> BuiltinDatatypes { get; } = new Dictionary<Utf8String, Utf8String>
    {
        [new Utf8String("integer"u8.ToArray())] = new("http://www.w3.org/2001/XMLSchema#integer"u8.ToArray()),
        [new Utf8String("decimal"u8.ToArray())] = new("http://www.w3.org/2001/XMLSchema#decimal"u8.ToArray()),
        [new Utf8String("float"u8.ToArray())] = new("http://www.w3.org/2001/XMLSchema#float"u8.ToArray()),
        [new Utf8String("string"u8.ToArray())] = new("http://www.w3.org/2001/XMLSchema#string"u8.ToArray()),
    }.ToFrozenDictionary();

    /// <summary>The prefixes that are always available without declaration, per the Manchester syntax note.</summary>
    public static FrozenDictionary<Utf8String, Utf8String> BuiltinPrefixes { get; } = new Dictionary<Utf8String, Utf8String>
    {
        [new Utf8String("rdf"u8.ToArray())] = new("http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8.ToArray()),
        [new Utf8String("rdfs"u8.ToArray())] = new("http://www.w3.org/2000/01/rdf-schema#"u8.ToArray()),
        [new Utf8String("xsd"u8.ToArray())] = new("http://www.w3.org/2001/XMLSchema#"u8.ToArray()),
        [new Utf8String("owl"u8.ToArray())] = new("http://www.w3.org/2002/07/owl#"u8.ToArray()),
    }.ToFrozenDictionary();

    /// <summary>Resolves a word to its section discriminant.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns>The section, or <see cref="OwlManchesterSection.Unknown"/>.</returns>
    public static OwlManchesterSection ResolveSection(Utf8String word)
    {
        return SectionKeywords.GetValueOrDefault(word, OwlManchesterSection.Unknown);
    }

    /// <summary>Resolves a word to its operator discriminant.</summary>
    /// <param name="word">The candidate operator text.</param>
    /// <returns>The operator, or <see cref="OwlManchesterOperator.None"/>.</returns>
    public static OwlManchesterOperator ResolveOperator(Utf8String word)
    {
        return OperatorWords.GetValueOrDefault(word, OwlManchesterOperator.None);
    }

    /// <summary>Resolves a word to its misc-frame discriminant.</summary>
    /// <param name="word">The candidate misc-frame keyword text.</param>
    /// <returns>The misc frame, or <see cref="OwlManchesterMiscFrame.Unknown"/>.</returns>
    public static OwlManchesterMiscFrame ResolveMiscFrame(Utf8String word)
    {
        return MiscFrameKeywords.GetValueOrDefault(word, OwlManchesterMiscFrame.Unknown);
    }

    /// <summary>Whether a word is a section keyword.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for section keywords.</returns>
    public static bool IsSection(Utf8String word)
    {
        return SectionKeywords.ContainsKey(word);
    }

    /// <summary>Whether a word is one of the continuation operator words (<c>Self</c> excluded).</summary>
    /// <param name="word">The candidate operator text.</param>
    /// <returns><see langword="true"/> for the continuation operators.</returns>
    public static bool IsOperator(Utf8String word)
    {
        return ContinuationOperators.Contains(word);
    }

    /// <summary>Whether a word is the <c>Prefix:</c> document keyword.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for the <c>Prefix:</c> keyword.</returns>
    public static bool IsPrefixKeyword(Utf8String word)
    {
        return word.SequenceEqual(PrefixKeyword);
    }

    /// <summary>Whether a word is the <c>Ontology:</c> document keyword.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for the <c>Ontology:</c> keyword.</returns>
    public static bool IsOntologyKeyword(Utf8String word)
    {
        return word.SequenceEqual(OntologyKeyword);
    }

    /// <summary>Whether a word is the <c>Import:</c> document keyword.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for the <c>Import:</c> keyword.</returns>
    public static bool IsImportKeyword(Utf8String word)
    {
        return word.SequenceEqual(ImportKeyword);
    }

    /// <summary>Whether a word is the <c>Annotations:</c> keyword, the ontology block or a frame section.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for the <c>Annotations:</c> keyword.</returns>
    public static bool IsAnnotationsKeyword(Utf8String word)
    {
        return word.SequenceEqual(AnnotationsKeyword);
    }

    /// <summary>Whether a word is one of the entity-free n-ary frame keywords.</summary>
    /// <param name="word">The candidate keyword text.</param>
    /// <returns><see langword="true"/> for misc frame keywords.</returns>
    public static bool IsMiscFrame(Utf8String word)
    {
        return MiscFrameKeywords.ContainsKey(word);
    }

    /// <summary>Whether a word is a frame keyword: an entity frame, a misc frame, or a document-level keyword.</summary>
    /// <param name="word">The raw token text.</param>
    /// <returns><see langword="true"/> for frame keywords.</returns>
    public static bool IsFrameKeyword(Utf8String word)
    {
        return IsPrefixKeyword(word) || IsOntologyKeyword(word)
            || IsImportKeyword(word) || IsAnnotationsKeyword(word)
            || EntityFrames.ContainsKey(word)
            || IsMiscFrame(word);
    }

    /// <summary>
    /// Whether a trailing word means the document cannot validly end here: a
    /// frame keyword awaiting its subject, a section keyword awaiting its
    /// list, or an operator awaiting its operand. <c>Ontology:</c> is the
    /// exception — an anonymous ontology header may end a document.
    /// </summary>
    /// <param name="word">The raw token text.</param>
    /// <returns><see langword="true"/> when a continuation is required.</returns>
    public static bool IsContinuationKeyword(Utf8String word)
    {
        if(IsOntologyKeyword(word))
        {
            return false;
        }

        return IsPrefixKeyword(word) || IsImportKeyword(word) || IsAnnotationsKeyword(word)
            || EntityFrames.ContainsKey(word)
            || IsMiscFrame(word)
            || IsSection(word)
            || IsOperator(word);
    }
}
