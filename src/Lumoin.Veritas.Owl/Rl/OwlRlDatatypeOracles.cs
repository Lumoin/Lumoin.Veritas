using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The standard <see cref="OwlRlDatatypeOracle"/> over a term dictionary:
/// exact numeric value identity (so non-canonical lexical forms and
/// cross-datatype values like <c>"1"^^xsd:integer</c> /
/// <c>"01"^^xsd:integer</c> / <c>"1.0"^^xsd:decimal</c> /
/// <c>"1/1"^^owl:rational</c> compare as the values they denote), disjoint
/// value-space families for the rest, value-space membership against the
/// numeric interval map, and range-intersection coverage for the
/// <c>dt-range-intersection</c> extension.
/// </summary>
/// <remarks>
/// Everything the oracle cannot decide exactly answers unknown: float and
/// double values stay out of the interval algebra, temporal values compare
/// only when their lexical forms agree, and an unparseable lexical form
/// decides nothing. Unknown never fires a falsity, so the oracle is sound
/// over any input.
/// </remarks>
public static class OwlRlDatatypeOracles
{
    private enum DatatypeFamily
    {
        Unknown = 0,
        Text = 1,
        Numeric = 2,
        Boolean = 3,
        Temporal = 4,
    }

    /// <summary>
    /// Builds the oracle resolving term ids through <paramref name="dictionary"/>.
    /// </summary>
    /// <param name="dictionary">The dictionary the closure's term ids resolve through; datatypes the intersection rule derives are added to it.</param>
    /// <returns>The oracle.</returns>
    public static OwlRlDatatypeOracle FromDictionary(TermDictionary dictionary)
    {
        System.ArgumentNullException.ThrowIfNull(dictionary);

        DictionaryDatatypeOracle oracle = new(dictionary);

        return new OwlRlDatatypeOracle(oracle.KnownDistinct, oracle.OutsideDatatype, oracle.Supersets);
    }

    /// <summary>
    /// The standard datatype oracle bound to a term dictionary, carrying the dictionary as explicit
    /// state so the oracle's delegate members are bound method groups rather than lambdas closing over
    /// the enclosing dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through.</param>
    private sealed class DictionaryDatatypeOracle(TermDictionary dictionary)
    {
        /// <summary>The dictionary the term ids resolve through.</summary>
        private TermDictionary Dictionary { get; } = dictionary;

        /// <summary>Whether two literal term ids denote known-distinct data values.</summary>
        /// <param name="first">The first literal term id.</param>
        /// <param name="second">The second literal term id.</param>
        /// <returns><see langword="true"/> when the literals are known distinct.</returns>
        public bool KnownDistinct(TermId first, TermId second)
        {
            return LiteralsKnownDistinct(Dictionary, first, second);
        }

        /// <summary>Whether a literal term id lies outside a datatype's value space.</summary>
        /// <param name="literal">The literal term id.</param>
        /// <param name="datatype">The datatype term id.</param>
        /// <returns><see langword="true"/> when the literal is known to lie outside the datatype.</returns>
        public bool OutsideDatatype(TermId literal, TermId datatype)
        {
            return LiteralOutsideDatatype(Dictionary, literal, datatype);
        }

        /// <summary>The datatype-map members whose value space contains the intersection of two datatypes' spaces.</summary>
        /// <param name="first">The first datatype term id.</param>
        /// <param name="second">The second datatype term id.</param>
        /// <returns>The superset datatype ids; empty when unknown.</returns>
        public IReadOnlyCollection<TermId> Supersets(TermId first, TermId second)
        {
            return RangeIntersectionSupersets(Dictionary, first, second);
        }
    }

    private static bool LiteralsKnownDistinct(TermDictionary dictionary, TermId first, TermId second)
    {
        if(dictionary.Resolve(first) is not Literal firstLiteral || dictionary.Resolve(second) is not Literal secondLiteral)
        {
            return false;
        }

        //Language-tagged values are (tag, string) pairs: distinct exactly
        //when either component differs; a language-tagged value is always
        //distinct from a plain datatyped value.
        if(firstLiteral.Language is not null || secondLiteral.Language is not null)
        {
            return firstLiteral.Language is null
                || secondLiteral.Language is null
                || !firstLiteral.Language.Value.Equals(secondLiteral.Language.Value)
                || !firstLiteral.Value.Equals(secondLiteral.Value);
        }

        DatatypeFamily firstFamily = FamilyOf(firstLiteral.Datatype.Iri);
        DatatypeFamily secondFamily = FamilyOf(secondLiteral.Datatype.Iri);

        if(firstFamily == DatatypeFamily.Unknown || secondFamily == DatatypeFamily.Unknown)
        {
            return false;
        }

        if(firstFamily != secondFamily)
        {
            return true;
        }

        return firstFamily switch
        {
            DatatypeFamily.Numeric => NumericsKnownDistinct(firstLiteral, secondLiteral),
            DatatypeFamily.Boolean => BooleanValue(firstLiteral) is bool left && BooleanValue(secondLiteral) is bool right && left != right,
            DatatypeFamily.Text => firstLiteral.Datatype.Equals(secondLiteral.Datatype) && !firstLiteral.Value.Equals(secondLiteral.Value),
            _ => firstLiteral.Datatype.Equals(secondLiteral.Datatype) && !firstLiteral.Value.Equals(secondLiteral.Value)
        };
    }

    private static bool NumericsKnownDistinct(Literal first, Literal second)
    {
        if(!OwlNumericLexicals.TryGetValue(first.Value.ToString(), first.Datatype.Iri, out NumericValue firstValue)
            || !OwlNumericLexicals.TryGetValue(second.Value.ToString(), second.Datatype.Iri, out NumericValue secondValue))
        {
            return false;
        }

        if(NumericValue.Compare(firstValue, secondValue) is ComparisonResult.Less or ComparisonResult.Greater)
        {
            return true;
        }

        //The XSD float and double value spaces distinguish positive and
        //negative zero, which numeric comparison does not.
        return IsFloatingZero(firstValue, out bool firstNegative)
            && IsFloatingZero(secondValue, out bool secondNegative)
            && firstNegative != secondNegative;
    }

    private static bool IsFloatingZero(NumericValue value, out bool negative)
    {
        if(value.Kind == NumericKind.Float && value.AsFloat() == 0f)
        {
            negative = float.IsNegative(value.AsFloat());

            return true;
        }

        if(value.Kind == NumericKind.Double && value.AsDouble() == 0d)
        {
            negative = double.IsNegative(value.AsDouble());

            return true;
        }

        negative = false;

        return false;
    }

    private static bool LiteralOutsideDatatype(TermDictionary dictionary, TermId literalId, TermId datatypeId)
    {
        if(dictionary.Resolve(literalId) is not Literal literal || dictionary.Resolve(datatypeId) is not NamedNode datatype)
        {
            return false;
        }

        if(literal.Language is not null)
        {
            //A language-tagged value lies outside every plain datatype the
            //families cover.
            return FamilyOf(datatype.Iri) != DatatypeFamily.Unknown;
        }

        DatatypeFamily literalFamily = FamilyOf(literal.Datatype.Iri);
        DatatypeFamily targetFamily = FamilyOf(datatype.Iri);

        if(literalFamily == DatatypeFamily.Unknown || targetFamily == DatatypeFamily.Unknown)
        {
            return false;
        }

        if(literalFamily != targetFamily)
        {
            return true;
        }

        //Within the numeric family the interval map refines membership:
        //the value must lie in the target's space.
        if(literalFamily == DatatypeFamily.Numeric
            && OwlNumericRanges.TryGetRange(datatype.Iri, out OwlNumericRange range)
            && OwlNumericLexicals.TryGetValue(literal.Value.ToString(), literal.Datatype.Iri, out NumericValue value))
        {
            return !range.ContainsValue(value) && value.Kind is NumericKind.Integer or NumericKind.Decimal;
        }

        return false;
    }

    private static List<TermId> RangeIntersectionSupersets(TermDictionary dictionary, TermId first, TermId second)
    {
        if(dictionary.Resolve(first) is not NamedNode firstDatatype
            || dictionary.Resolve(second) is not NamedNode secondDatatype
            || !OwlNumericRanges.TryGetRange(firstDatatype.Iri, out OwlNumericRange firstRange)
            || !OwlNumericRanges.TryGetRange(secondDatatype.Iri, out OwlNumericRange secondRange))
        {
            return [];
        }

        if(OwlNumericRanges.Intersect(firstRange, secondRange) is not OwlNumericRange intersection)
        {
            return [];
        }

        List<TermId> supersets = [];
        foreach(Utf8String iri in OwlNumericRanges.SupersetsOf(intersection))
        {
            supersets.Add(dictionary.GetOrAdd(new NamedNode(iri)));
        }

        return supersets;
    }

    private static bool? BooleanValue(Literal literal)
    {
        string lexical = literal.Value.ToString();

        return lexical switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => null
        };
    }

    private static DatatypeFamily FamilyOf(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(OwlVocabulary.Real) || datatypeIri.Equals(OwlVocabulary.Rational))
        {
            return DatatypeFamily.Numeric;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Boolean))
        {
            return DatatypeFamily.Boolean;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.String) || datatypeIri.Equals(Vocabulary.Xsd.NormalizedString)
            || datatypeIri.Equals(Vocabulary.Xsd.Token))
        {
            return DatatypeFamily.Text;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.DateTime) || datatypeIri.Equals(Vocabulary.Xsd.DateTimeStamp)
            || datatypeIri.Equals(Vocabulary.Xsd.Date) || datatypeIri.Equals(Vocabulary.Xsd.Time))
        {
            return DatatypeFamily.Temporal;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Decimal) || datatypeIri.Equals(Vocabulary.Xsd.Integer)
            || datatypeIri.Equals(Vocabulary.Xsd.NonNegativeInteger) || datatypeIri.Equals(Vocabulary.Xsd.NonPositiveInteger)
            || datatypeIri.Equals(Vocabulary.Xsd.PositiveInteger) || datatypeIri.Equals(Vocabulary.Xsd.NegativeInteger)
            || datatypeIri.Equals(Vocabulary.Xsd.Long) || datatypeIri.Equals(Vocabulary.Xsd.Int)
            || datatypeIri.Equals(Vocabulary.Xsd.Short) || datatypeIri.Equals(Vocabulary.Xsd.ByteValue)
            || datatypeIri.Equals(Vocabulary.Xsd.UnsignedLong) || datatypeIri.Equals(Vocabulary.Xsd.UnsignedInt)
            || datatypeIri.Equals(Vocabulary.Xsd.UnsignedShort) || datatypeIri.Equals(Vocabulary.Xsd.UnsignedByte)
            || datatypeIri.Equals(Vocabulary.Xsd.Double) || datatypeIri.Equals(Vocabulary.Xsd.Float))
        {
            return DatatypeFamily.Numeric;
        }

        return DatatypeFamily.Unknown;
    }
}
