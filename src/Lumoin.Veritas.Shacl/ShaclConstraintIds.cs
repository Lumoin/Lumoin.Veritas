using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for every SHACL constraint-component
/// parameter defined by SHACL 1.2 Core §6 (including SHACL 1.2 additions
/// for reification, lists, and <c>sh:rootClass</c>).
/// </summary>
/// <remarks>
/// <para>
/// The shape loader matches the predicate of each incoming shape-graph
/// triple against these handles to dispatch into the correct constraint
/// constructor. Evaluators then carry parsed values (counts, IRIs,
/// literals) rather than predicate handles.
/// </para>
/// <para>
/// This struct is large — one field per parameter — because doing so
/// collapses the shape-loader's predicate dispatch to pure integer
/// comparisons with no per-constraint dictionary lookups on the hot path.
/// </para>
/// </remarks>
/// <param name="MinCount"><c>sh:minCount</c></param>
/// <param name="MaxCount"><c>sh:maxCount</c></param>
/// <param name="MinExclusive"><c>sh:minExclusive</c></param>
/// <param name="MaxExclusive"><c>sh:maxExclusive</c></param>
/// <param name="MinInclusive"><c>sh:minInclusive</c></param>
/// <param name="MaxInclusive"><c>sh:maxInclusive</c></param>
/// <param name="MinLength"><c>sh:minLength</c></param>
/// <param name="MaxLength"><c>sh:maxLength</c></param>
/// <param name="Pattern"><c>sh:pattern</c></param>
/// <param name="Flags"><c>sh:flags</c></param>
/// <param name="SingleLine"><c>sh:singleLine</c></param>
/// <param name="LanguageIn"><c>sh:languageIn</c></param>
/// <param name="UniqueLang"><c>sh:uniqueLang</c></param>
/// <param name="Class"><c>sh:class</c></param>
/// <param name="Datatype"><c>sh:datatype</c></param>
/// <param name="NodeKind"><c>sh:nodeKind</c></param>
/// <param name="RootClass"><c>sh:rootClass</c></param>
/// <param name="HasValue"><c>sh:hasValue</c></param>
/// <param name="In"><c>sh:in</c></param>
/// <param name="EqualsTo"><c>sh:equals</c></param>
/// <param name="Disjoint"><c>sh:disjoint</c></param>
/// <param name="LessThan"><c>sh:lessThan</c></param>
/// <param name="LessThanOrEquals"><c>sh:lessThanOrEquals</c></param>
/// <param name="Not"><c>sh:not</c></param>
/// <param name="And"><c>sh:and</c></param>
/// <param name="Or"><c>sh:or</c></param>
/// <param name="Xone"><c>sh:xone</c></param>
/// <param name="Node"><c>sh:node</c></param>
/// <param name="Property"><c>sh:property</c></param>
/// <param name="QualifiedValueShape"><c>sh:qualifiedValueShape</c></param>
/// <param name="QualifiedMinCount"><c>sh:qualifiedMinCount</c></param>
/// <param name="QualifiedMaxCount"><c>sh:qualifiedMaxCount</c></param>
/// <param name="QualifiedValueShapesDisjoint"><c>sh:qualifiedValueShapesDisjoint</c></param>
/// <param name="SubsetOf"><c>sh:subsetOf</c></param>
/// <param name="Closed"><c>sh:closed</c></param>
/// <param name="IgnoredProperties"><c>sh:ignoredProperties</c></param>
/// <param name="UniqueValuesFor"><c>sh:uniqueValuesFor</c></param>
/// <param name="ReifierShape"><c>sh:reifierShape</c> (SHACL 1.2)</param>
/// <param name="ReificationRequired"><c>sh:reificationRequired</c> (SHACL 1.2)</param>
/// <param name="MemberShape"><c>sh:memberShape</c> (SHACL 1.2)</param>
/// <param name="MinListLength"><c>sh:minListLength</c> (SHACL 1.2)</param>
/// <param name="MaxListLength"><c>sh:maxListLength</c> (SHACL 1.2)</param>
/// <param name="UniqueMembers"><c>sh:uniqueMembers</c> (SHACL 1.2)</param>
public readonly record struct ShaclConstraintIds(
    IriId MinCount,
    IriId MaxCount,
    IriId MinExclusive,
    IriId MaxExclusive,
    IriId MinInclusive,
    IriId MaxInclusive,
    IriId MinLength,
    IriId MaxLength,
    IriId Pattern,
    IriId Flags,
    IriId SingleLine,
    IriId LanguageIn,
    IriId UniqueLang,
    IriId Class,
    IriId Datatype,
    IriId NodeKind,
    IriId RootClass,
    IriId HasValue,
    IriId In,
    IriId EqualsTo,
    IriId Disjoint,
    IriId LessThan,
    IriId LessThanOrEquals,
    IriId Not,
    IriId And,
    IriId Or,
    IriId Xone,
    IriId Node,
    IriId Property,
    IriId QualifiedValueShape,
    IriId QualifiedMinCount,
    IriId QualifiedMaxCount,
    IriId QualifiedValueShapesDisjoint,
    IriId SubsetOf,
    IriId Closed,
    IriId IgnoredProperties,
    IriId UniqueValuesFor,
    IriId ReifierShape,
    IriId ReificationRequired,
    IriId MemberShape,
    IriId MinListLength,
    IriId MaxListLength,
    IriId UniqueMembers)
{
    /// <summary>
    /// Interns every constraint-parameter SHACL IRI into
    /// <paramref name="dictionary"/> and returns their narrowed
    /// <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved constraint-parameter IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclConstraintIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclConstraintIds(
            MinCount: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MinCount)),
            MaxCount: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MaxCount)),
            MinExclusive: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MinExclusive)),
            MaxExclusive: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MaxExclusive)),
            MinInclusive: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MinInclusive)),
            MaxInclusive: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MaxInclusive)),
            MinLength: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MinLength)),
            MaxLength: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MaxLength)),
            Pattern: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Pattern)),
            Flags: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Flags)),
            SingleLine: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.SingleLine)),
            LanguageIn: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.LanguageIn)),
            UniqueLang: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.UniqueLang)),
            Class: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Class)),
            Datatype: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Datatype)),
            NodeKind: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.NodeKind)),
            RootClass: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.RootClass)),
            HasValue: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.HasValue)),
            In: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.In)),
            EqualsTo: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.EqualsTo)),
            Disjoint: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Disjoint)),
            LessThan: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.LessThan)),
            LessThanOrEquals: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.LessThanOrEquals)),
            Not: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Not)),
            And: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.And)),
            Or: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Or)),
            Xone: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Xone)),
            Node: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Node)),
            Property: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Property)),
            QualifiedValueShape: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.QualifiedValueShape)),
            QualifiedMinCount: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.QualifiedMinCount)),
            QualifiedMaxCount: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.QualifiedMaxCount)),
            QualifiedValueShapesDisjoint: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.QualifiedValueShapesDisjoint)),
            SubsetOf: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.SubsetOf)),
            Closed: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Closed)),
            IgnoredProperties: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.IgnoredProperties)),
            UniqueValuesFor: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.UniqueValuesFor)),
            ReifierShape: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.ReifierShape)),
            ReificationRequired: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.ReificationRequired)),
            MemberShape: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MemberShape)),
            MinListLength: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MinListLength)),
            MaxListLength: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MaxListLength)),
            UniqueMembers: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.UniqueMembers)));
    }
}
