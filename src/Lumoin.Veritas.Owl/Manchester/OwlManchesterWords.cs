using System.Collections.Generic;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// The reserved word sets of the Manchester syntax — frame keywords, section
/// keywords, expression operators, property characteristics, facet names, and
/// built-in datatype names — in one canonical home for the lexer, the
/// converter, and the writer.
/// </summary>
internal static class OwlManchesterWords
{
    /// <summary>The <c>Prefix:</c> keyword.</summary>
    public const string PrefixKeyword = "Prefix:";

    /// <summary>The <c>Ontology:</c> keyword.</summary>
    public const string OntologyKeyword = "Ontology:";

    /// <summary>The <c>Import:</c> keyword.</summary>
    public const string ImportKeyword = "Import:";

    /// <summary>The <c>Annotations:</c> keyword, both the ontology-level block and the frame section.</summary>
    public const string AnnotationsKeyword = "Annotations:";

    /// <summary>The entity-frame keywords, keyed to the entity kind each declares.</summary>
    public static IReadOnlyDictionary<string, OwlEntityKind> EntityFrames { get; } = new Dictionary<string, OwlEntityKind>
    {
        ["Class:"] = OwlEntityKind.Class,
        ["Datatype:"] = OwlEntityKind.Datatype,
        ["ObjectProperty:"] = OwlEntityKind.ObjectProperty,
        ["DataProperty:"] = OwlEntityKind.DataProperty,
        ["AnnotationProperty:"] = OwlEntityKind.AnnotationProperty,
        ["Individual:"] = OwlEntityKind.NamedIndividual
    };

    /// <summary>The keywords of the entity-free n-ary frames.</summary>
    public static IReadOnlySet<string> MiscFrames { get; } = new HashSet<string>
    {
        "EquivalentClasses:",
        "DisjointClasses:",
        "EquivalentProperties:",
        "DisjointProperties:",
        "SameIndividual:",
        "DifferentIndividuals:"
    };

    /// <summary>The section keywords that may appear inside a frame.</summary>
    public static IReadOnlySet<string> Sections { get; } = new HashSet<string>
    {
        AnnotationsKeyword,
        "SubClassOf:",
        "EquivalentTo:",
        "DisjointWith:",
        "DisjointUnionOf:",
        "HasKey:",
        "Domain:",
        "Range:",
        "Characteristics:",
        "SubPropertyOf:",
        "InverseOf:",
        "SubPropertyChain:",
        "Types:",
        "Facts:",
        "SameAs:",
        "DifferentFrom:"
    };

    /// <summary>The expression operator words that always require a continuation.</summary>
    public static IReadOnlySet<string> Operators { get; } = new HashSet<string>
    {
        "and",
        "or",
        "not",
        "that",
        "some",
        "only",
        "value",
        "inverse",
        "o",
        "min",
        "max",
        "exactly"
    };

    /// <summary>The property characteristic words, keyed to the characteristic each names.</summary>
    public static IReadOnlyDictionary<string, OwlPropertyCharacteristic> Characteristics { get; } = new Dictionary<string, OwlPropertyCharacteristic>
    {
        ["Functional"] = OwlPropertyCharacteristic.Functional,
        ["InverseFunctional"] = OwlPropertyCharacteristic.InverseFunctional,
        ["Reflexive"] = OwlPropertyCharacteristic.Reflexive,
        ["Irreflexive"] = OwlPropertyCharacteristic.Irreflexive,
        ["Symmetric"] = OwlPropertyCharacteristic.Symmetric,
        ["Asymmetric"] = OwlPropertyCharacteristic.Asymmetric,
        ["Transitive"] = OwlPropertyCharacteristic.Transitive
    };

    /// <summary>The named facet words, keyed to the constraining-facet IRI each abbreviates.</summary>
    public static IReadOnlyDictionary<string, string> NamedFacets { get; } = new Dictionary<string, string>
    {
        ["length"] = "http://www.w3.org/2001/XMLSchema#length",
        ["minLength"] = "http://www.w3.org/2001/XMLSchema#minLength",
        ["maxLength"] = "http://www.w3.org/2001/XMLSchema#maxLength",
        ["pattern"] = "http://www.w3.org/2001/XMLSchema#pattern",
        ["langRange"] = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langRange"
    };

    /// <summary>The comparison facet operators, keyed to the constraining-facet IRI each abbreviates.</summary>
    public static IReadOnlyDictionary<string, string> ComparisonFacets { get; } = new Dictionary<string, string>
    {
        ["<="] = "http://www.w3.org/2001/XMLSchema#maxInclusive",
        ["<"] = "http://www.w3.org/2001/XMLSchema#maxExclusive",
        [">="] = "http://www.w3.org/2001/XMLSchema#minInclusive",
        [">"] = "http://www.w3.org/2001/XMLSchema#minExclusive"
    };

    /// <summary>The built-in datatype abbreviations, keyed to the datatype IRI each names.</summary>
    public static IReadOnlyDictionary<string, string> BuiltinDatatypes { get; } = new Dictionary<string, string>
    {
        ["integer"] = "http://www.w3.org/2001/XMLSchema#integer",
        ["decimal"] = "http://www.w3.org/2001/XMLSchema#decimal",
        ["float"] = "http://www.w3.org/2001/XMLSchema#float",
        ["string"] = "http://www.w3.org/2001/XMLSchema#string"
    };

    /// <summary>The prefixes that are always available without declaration, per the Manchester syntax note.</summary>
    public static IReadOnlyDictionary<string, string> BuiltinPrefixes { get; } = new Dictionary<string, string>
    {
        ["rdf"] = "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
        ["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#",
        ["xsd"] = "http://www.w3.org/2001/XMLSchema#",
        ["owl"] = "http://www.w3.org/2002/07/owl#"
    };

    /// <summary>Whether a word is a frame keyword: an entity frame, a misc frame, or a document-level keyword.</summary>
    /// <param name="word">The raw token text.</param>
    /// <returns><see langword="true"/> for frame keywords.</returns>
    public static bool IsFrameKeyword(string word)
    {
        return word is PrefixKeyword or OntologyKeyword or ImportKeyword or AnnotationsKeyword
            || EntityFrames.ContainsKey(word)
            || MiscFrames.Contains(word);
    }

    /// <summary>
    /// Whether a trailing word means the document cannot validly end here: a
    /// frame keyword awaiting its subject, a section keyword awaiting its
    /// list, or an operator awaiting its operand. <c>Ontology:</c> is the
    /// exception — an anonymous ontology header may end a document.
    /// </summary>
    /// <param name="word">The raw token text.</param>
    /// <returns><see langword="true"/> when a continuation is required.</returns>
    public static bool IsContinuationKeyword(string word)
    {
        if(word == OntologyKeyword)
        {
            return false;
        }

        return word is PrefixKeyword or ImportKeyword or AnnotationsKeyword
            || EntityFrames.ContainsKey(word)
            || MiscFrames.Contains(word)
            || Sections.Contains(word)
            || Operators.Contains(word);
    }
}
