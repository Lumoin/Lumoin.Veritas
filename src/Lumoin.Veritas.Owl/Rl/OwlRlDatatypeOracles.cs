using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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
/// numeric interval map, range-intersection coverage for the
/// <c>dt-range-intersection</c> extension, the alias-and-target admission
/// and literal retype for the <c>datatype-alias-retype</c> completion,
/// same-space value equality for the entailment surface's <c>dt-eq</c>
/// value-identity bridge, and the exact-tower integral bound read and
/// count-literal mint for the <c>fibre-cardinality-certificate</c>
/// completion.
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

        return new OwlRlDatatypeOracle(oracle.KnownDistinct, oracle.OutsideDatatype, oracle.Supersets, oracle.KnownDisjoint, oracle.AliasRecognized, oracle.AliasRetype, oracle.KnownEqual, oracle.ReadNonNegativeInteger, oracle.MintNonNegativeInteger);
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
        public List<TermId> Supersets(TermId first, TermId second)
        {
            return RangeIntersectionSupersets(Dictionary, first, second);
        }

        /// <summary>Whether two datatype term ids denote datatypes with known-disjoint value spaces.</summary>
        /// <param name="first">The first datatype term id.</param>
        /// <param name="second">The second datatype term id.</param>
        /// <returns><see langword="true"/> when the value spaces are known disjoint.</returns>
        public bool KnownDisjoint(TermId first, TermId second)
        {
            return DatatypesKnownDisjoint(Dictionary, first, second);
        }

        /// <summary>Whether an ordered pair is an alias-and-target retype admission.</summary>
        /// <param name="alias">The alias side's term id.</param>
        /// <param name="target">The target side's term id.</param>
        /// <returns><see langword="true"/> when the pair is admitted.</returns>
        public bool AliasRecognized(TermId alias, TermId target)
        {
            return DatatypeAliasRecognized(Dictionary, alias, target);
        }

        /// <summary>The minted retype of an alias-typed literal onto an admitted pair's target, or <see cref="TermId.None"/> when refused.</summary>
        /// <param name="literal">The candidate literal term id.</param>
        /// <param name="alias">The alias side's term id.</param>
        /// <param name="target">The target side's term id.</param>
        /// <returns>The minted retyped literal's id, or <see cref="TermId.None"/>.</returns>
        public TermId AliasRetype(TermId literal, TermId alias, TermId target)
        {
            return DatatypeAliasLiteralRetype(Dictionary, literal, alias, target);
        }

        /// <summary>Whether two literal term ids denote the same data value within one value space.</summary>
        /// <param name="first">The first literal term id.</param>
        /// <param name="second">The second literal term id.</param>
        /// <returns><see langword="true"/> when the literals are known equal.</returns>
        public bool KnownEqual(TermId first, TermId second)
        {
            return LiteralsKnownEqual(Dictionary, first, second);
        }

        /// <summary>The exact-tower integral read of a literal term id's value and datatype term.</summary>
        /// <param name="literal">The candidate literal term id.</param>
        /// <param name="value">The nonnegative integer value, when the read succeeds.</param>
        /// <param name="datatype">The literal's datatype term id, when the read succeeds.</param>
        /// <returns><see langword="true"/> when the literal reads.</returns>
        public bool ReadNonNegativeInteger(TermId literal, out long value, out TermId datatype)
        {
            return LiteralNonNegativeInteger(Dictionary, literal, out value, out datatype);
        }

        /// <summary>The minted digit literal of a nonnegative integer typed by a datatype, or <see cref="TermId.None"/> when refused.</summary>
        /// <param name="value">The nonnegative integer value.</param>
        /// <param name="datatype">The datatype term id typing the minted literal.</param>
        /// <returns>The minted literal's id, or <see cref="TermId.None"/>.</returns>
        public TermId MintNonNegativeInteger(long value, TermId datatype)
        {
            return NonNegativeIntegerLiteral(Dictionary, value, datatype);
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

    /// <summary>
    /// Whether two term ids denote datatypes with disjoint value spaces:
    /// both must resolve to named datatypes of known, different families.
    /// The families' value spaces are pairwise disjoint by the datatype
    /// map's own definitions — the boolean values, the character-sequence
    /// values, the structured date and time values, and the numbers are
    /// different kinds of value object, and <c>owl:real</c> and
    /// <c>owl:rational</c> are number sets by definition. Same-family
    /// pairs answer unknown; within-family refinement belongs to the
    /// numeric interval map.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through.</param>
    /// <param name="first">The first datatype term id.</param>
    /// <param name="second">The second datatype term id.</param>
    /// <returns><see langword="true"/> when the value spaces are known disjoint.</returns>
    private static bool DatatypesKnownDisjoint(TermDictionary dictionary, TermId first, TermId second)
    {
        if(dictionary.Resolve(first) is not NamedNode firstDatatype || dictionary.Resolve(second) is not NamedNode secondDatatype)
        {
            return false;
        }

        DatatypeFamily firstFamily = FamilyOf(firstDatatype.Iri);
        DatatypeFamily secondFamily = FamilyOf(secondDatatype.Iri);

        return firstFamily != DatatypeFamily.Unknown && secondFamily != DatatypeFamily.Unknown && firstFamily != secondFamily;
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

    /// <summary>
    /// Whether an ordered pair is an alias-and-target retype admission: the
    /// alias resolves to an IRI outside BOTH the value-space-family grouping
    /// and the value-space classifier's modelled set — the strictest
    /// known-datatype net the oracle has, refusing every recognized pair
    /// outright — and the target resolves to an IRI the classifier MODELS,
    /// which is exactly the set whose lexical validity is parser-genuine or
    /// true by the datatype's own definition. A family-known but
    /// classifier-unmodelled datatype such as <c>xsd:token</c> is refused on
    /// either side: as a target its validity read would be the facade's
    /// default acceptance, not a check.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through.</param>
    /// <param name="alias">The alias side's term id.</param>
    /// <param name="target">The target side's term id.</param>
    /// <returns><see langword="true"/> when the pair is admitted.</returns>
    private static bool DatatypeAliasRecognized(TermDictionary dictionary, TermId alias, TermId target)
    {
        if(dictionary.Resolve(alias) is not NamedNode aliasNode || dictionary.Resolve(target) is not NamedNode targetNode)
        {
            return false;
        }

        return FamilyOf(aliasNode.Iri) == DatatypeFamily.Unknown
            && !XsdLexicalValidity.ModelsDatatype(aliasNode.Iri)
            && XsdLexicalValidity.ModelsDatatype(targetNode.Iri);
    }

    /// <summary>
    /// Mints the retype of an alias-typed literal onto an admitted pair's
    /// target: the literal must carry no language tag, be typed by exactly
    /// the alias IRI, and its lexical form must be valid for the target —
    /// an invalid lexical leaves both denotations arbitrary non-values that
    /// no model forces equal, so refusal there is a soundness fence, not
    /// hygiene. The literal-shape checks run first as the cheap reject on
    /// the closure's object scan.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through; the retyped literal is added to it.</param>
    /// <param name="literalId">The candidate literal term id.</param>
    /// <param name="alias">The alias side's term id.</param>
    /// <param name="target">The target side's term id.</param>
    /// <returns>The minted retyped literal's id, or <see cref="TermId.None"/> when refused.</returns>
    private static TermId DatatypeAliasLiteralRetype(TermDictionary dictionary, TermId literalId, TermId alias, TermId target)
    {
        if(dictionary.Resolve(literalId) is not Literal literal
            || literal.Language is not null
            || dictionary.Resolve(alias) is not NamedNode aliasNode
            || !literal.Datatype.Iri.Equals(aliasNode.Iri)
            || !DatatypeAliasRecognized(dictionary, alias, target)
            || dictionary.Resolve(target) is not NamedNode targetNode
            || !XsdLexicalValidity.IsValidLexicalForm(literal.Value, targetNode.Iri, ValueDatatypeRegistry.Empty))
        {
            return TermId.None;
        }

        return dictionary.GetOrAdd(new Literal(literal.Value, new NamedNode(targetNode.Iri)));
    }

    /// <summary>
    /// Whether two literal term ids denote the same data value within ONE
    /// value space. The exact tower — the integer and decimal kinds, where
    /// the decimal, derived-integer, and <c>owl:rational</c> lexicals land —
    /// affirms across datatypes because those value spaces genuinely nest;
    /// <c>xsd:float</c> and <c>xsd:double</c> affirm only within their own
    /// kind, because the datatype map keeps the float, double, and real
    /// spaces pairwise disjoint and the numeric comparison alone is the
    /// promotion lattice, not value-space identity. Signed floating zeros
    /// of differing sign refuse; booleans affirm on equal readings; every
    /// other shape answers unknown.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through.</param>
    /// <param name="first">The first literal term id.</param>
    /// <param name="second">The second literal term id.</param>
    /// <returns><see langword="true"/> when the literals are known to denote one value.</returns>
    private static bool LiteralsKnownEqual(TermDictionary dictionary, TermId first, TermId second)
    {
        if(dictionary.Resolve(first) is not Literal firstLiteral
            || dictionary.Resolve(second) is not Literal secondLiteral
            || firstLiteral.Language is not null
            || secondLiteral.Language is not null)
        {
            return false;
        }

        DatatypeFamily firstFamily = FamilyOf(firstLiteral.Datatype.Iri);
        DatatypeFamily secondFamily = FamilyOf(secondLiteral.Datatype.Iri);

        if(firstFamily == DatatypeFamily.Boolean && secondFamily == DatatypeFamily.Boolean)
        {
            return BooleanValue(firstLiteral) is bool left && BooleanValue(secondLiteral) is bool right && left == right;
        }

        if(firstFamily != DatatypeFamily.Numeric || secondFamily != DatatypeFamily.Numeric)
        {
            return false;
        }

        if(!OwlNumericLexicals.TryGetValue(firstLiteral.Value.ToString(), firstLiteral.Datatype.Iri, out NumericValue firstValue)
            || !OwlNumericLexicals.TryGetValue(secondLiteral.Value.ToString(), secondLiteral.Datatype.Iri, out NumericValue secondValue)
            || !NumericValueSpacesMatch(firstValue, secondValue)
            || NumericValue.Compare(firstValue, secondValue) != ComparisonResult.Equal)
        {
            return false;
        }

        return !IsFloatingZero(firstValue, out bool firstNegative)
            || !IsFloatingZero(secondValue, out bool secondNegative)
            || firstNegative == secondNegative;
    }

    /// <summary>
    /// Whether two parsed numeric values inhabit one value space: both in
    /// the exact tower (the integer and decimal kinds), both float, or both
    /// double. Cross-space numeric coincidence never makes one value.
    /// </summary>
    /// <param name="first">The first parsed value.</param>
    /// <param name="second">The second parsed value.</param>
    /// <returns><see langword="true"/> when the kinds share a value space.</returns>
    private static bool NumericValueSpacesMatch(NumericValue first, NumericValue second)
    {
        bool firstExact = first.Kind is NumericKind.Integer or NumericKind.Decimal;
        bool secondExact = second.Kind is NumericKind.Integer or NumericKind.Decimal;

        if(firstExact || secondExact)
        {
            return firstExact && secondExact;
        }

        return first.Kind == second.Kind;
    }

    /// <summary>
    /// Reads the nonnegative-integer value and the datatype term of an
    /// exact-tower integral literal: a plain literal whose lexical parses
    /// into the integer or decimal kind with an integral, nonnegative value
    /// that fits a <see cref="long"/>. Float and double literals refuse —
    /// the float, double, and exact value spaces are pairwise disjoint, so
    /// a float-typed bound never denotes a member of the exact tower's
    /// nonnegative integers. The surfaced datatype term is the literal's
    /// own type IRI, interned through the dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through; the datatype term is added to it.</param>
    /// <param name="literalId">The candidate literal term id.</param>
    /// <param name="value">The nonnegative integer value, when the read succeeds.</param>
    /// <param name="datatype">The literal's datatype term id, when the read succeeds.</param>
    /// <returns><see langword="true"/> when the literal reads.</returns>
    private static bool LiteralNonNegativeInteger(TermDictionary dictionary, TermId literalId, out long value, out TermId datatype)
    {
        value = 0;
        datatype = TermId.None;

        if(dictionary.Resolve(literalId) is not Literal literal
            || literal.Language is not null
            || !OwlNumericLexicals.TryGetValue(literal.Value.ToString(), literal.Datatype.Iri, out NumericValue parsed))
        {
            return false;
        }

        if(parsed.Kind == NumericKind.Integer)
        {
            BigInteger integer = parsed.AsInteger();
            if(integer.Sign < 0 || integer > long.MaxValue)
            {
                return false;
            }

            value = (long)integer;
        }
        else if(parsed.Kind == NumericKind.Decimal)
        {
            decimal number = parsed.AsDecimal();
            if(number < 0m || decimal.Truncate(number) != number || number > long.MaxValue)
            {
                return false;
            }

            value = (long)number;
        }
        else
        {
            return false;
        }

        datatype = dictionary.GetOrAdd(new NamedNode(literal.Datatype.Iri));

        return true;
    }

    /// <summary>
    /// Mints the invariant digit literal of a nonnegative integer typed by
    /// a datatype: the datatype must be an integer datatype of the numeric
    /// interval map and the value must lie in its value space, so a minted
    /// literal always denotes the value it spells. Continuum datatypes
    /// refuse — the mint types counts, and a count's canonical spelling is
    /// its digit string under an integer datatype.
    /// </summary>
    /// <param name="dictionary">The dictionary the term ids resolve through; the minted literal is added to it.</param>
    /// <param name="value">The nonnegative integer value.</param>
    /// <param name="datatypeId">The datatype term id typing the minted literal.</param>
    /// <returns>The minted literal's id, or <see cref="TermId.None"/> when refused.</returns>
    private static TermId NonNegativeIntegerLiteral(TermDictionary dictionary, long value, TermId datatypeId)
    {
        if(value < 0
            || dictionary.Resolve(datatypeId) is not NamedNode datatypeNode
            || !OwlNumericRanges.TryGetRange(datatypeNode.Iri, out OwlNumericRange range)
            || !range.IntegersOnly
            || !range.ContainsValue(new NumericValue(new BigInteger(value))))
        {
            return TermId.None;
        }

        return dictionary.GetOrAdd(new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(datatypeNode.Iri)));
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
