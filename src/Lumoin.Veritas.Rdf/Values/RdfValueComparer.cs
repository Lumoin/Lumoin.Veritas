using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Compares two RDF terms under SPARQL 1.1 §17.4.1 ordering
/// semantics.
/// </summary>
/// <remarks>
/// <para>
/// This is the canonical RDF value-space comparator used by SHACL's
/// numeric-range and pair-property constraints, by the SPARQL filter
/// and <c>ORDER BY</c> machinery, and by any value index that must
/// agree with them. It dispatches on the operands' XSD value spaces:
/// </para>
/// <list type="bullet">
///   <item><description>Numeric tower (integer / decimal / float / double, plus derived integer types) — compared via promotion lattice.</description></item>
///   <item><description><c>xsd:string</c> — ordinal Unicode codepoint comparison.</description></item>
///   <item><description><c>xsd:boolean</c> — <c>false &lt; true</c>.</description></item>
///   <item><description><c>xsd:dateTime</c>, <c>xsd:date</c>, <c>xsd:time</c> — instant comparison; the two-argument overload keeps XSD §3.2.7.4 indeterminate handling for naive vs aware, while the implicit-timezone overload totalizes the axis per SPARQL §17.3.</description></item>
///   <item><description><c>xsd:duration</c> — partial order via XSD §3.2.6.2 four-test-points; restricted subtypes total order within their kind.</description></item>
/// </list>
/// <para>
/// Operands in different value spaces, ill-formed lexical forms,
/// non-literal terms, and indeterminate cases all produce
/// <see cref="ComparisonResult.Incomparable"/>. Callers decide what
/// that means in their context — SHACL constraint evaluators treat
/// it as non-conformance, the SPARQL filter engine treats it as a
/// type error, etc.
/// </para>
/// <para>
/// <see cref="CompareForSort"/> is the distinct <c>ORDER BY</c>
/// surface: a total preorder over all literals built from a
/// class-rank partition, so a mixed-datatype sort can never observe
/// an intransitive comparison.
/// </para>
/// </remarks>
public static class RdfValueComparer
{
    /// <summary>
    /// Compares <paramref name="left"/> and <paramref name="right"/>
    /// under the XSD partial order: a timezone-naive temporal operand
    /// against a timezone-aware one is decided only outside the ±14h
    /// envelope. Returns <see cref="ComparisonResult.Incomparable"/>
    /// when the operands cannot be ordered under SPARQL semantics.
    /// </summary>
    /// <param name="left">The left term.</param>
    /// <param name="right">The right term.</param>
    /// <returns>The comparison verdict.</returns>
    public static ComparisonResult Compare(RdfTerm left, RdfTerm right)
    {
        return CompareCore(left, right, implicitTimezone: null);
    }

    /// <summary>
    /// Compares <paramref name="left"/> and <paramref name="right"/>
    /// with the temporal families totalized: a timezone-naive
    /// <c>xsd:dateTime</c>/<c>xsd:date</c>/<c>xsd:time</c> operand is
    /// normalized with <paramref name="implicitTimezone"/> (the
    /// SPARQL §17.3 / XPath F&amp;O implicit-timezone reading), so a
    /// well-formed same-family temporal pair always yields an order
    /// verdict. Every non-temporal value space behaves exactly as the
    /// two-argument overload.
    /// </summary>
    /// <param name="left">The left term.</param>
    /// <param name="right">The right term.</param>
    /// <param name="implicitTimezone">The implicit timezone applied to naive temporal operands.</param>
    /// <returns>The comparison verdict.</returns>
    public static ComparisonResult Compare(RdfTerm left, RdfTerm right, TimeSpan implicitTimezone)
    {
        return CompareCore(left, right, implicitTimezone);
    }

    /// <summary>
    /// Attempts the temporal-family comparison SPARQL's ordering
    /// operators (<c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>,
    /// <c>&gt;=</c>) delegate to: returns <see langword="true"/>
    /// exactly when BOTH operands are literals of a temporal family
    /// (<c>xsd:dateTime</c>/<c>xsd:dateTimeStamp</c>, <c>xsd:date</c>,
    /// <c>xsd:time</c>), in which case the pair is governed by this
    /// comparison and <paramref name="result"/> carries the verdict —
    /// an order verdict for a well-formed same-family pair on the
    /// implicit-timezone-totalized axis, or
    /// <see cref="ComparisonResult.Incomparable"/> (a type error at
    /// the caller) for a cross-family pair or an operand whose
    /// lexical form is invalid for its declared datatype.
    /// </summary>
    /// <param name="left">The left term.</param>
    /// <param name="right">The right term.</param>
    /// <param name="implicitTimezone">The implicit timezone applied to naive operands.</param>
    /// <param name="result">Receives the verdict when the pair is temporal-governed.</param>
    /// <returns><see langword="true"/> when both operands are temporal-family literals.</returns>
    public static bool TryCompareTemporal(RdfTerm left, RdfTerm right, TimeSpan implicitTimezone, out ComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        result = ComparisonResult.Incomparable;
        if(left is not Literal leftLiteral || right is not Literal rightLiteral)
        {
            return false;
        }

        ValueSpace leftSpace = ValueSpaceClassifier.Classify(leftLiteral.Datatype.Iri);
        ValueSpace rightSpace = ValueSpaceClassifier.Classify(rightLiteral.Datatype.Iri);
        if(!IsTemporal(leftSpace) || !IsTemporal(rightSpace))
        {
            return false;
        }

        if(leftSpace == rightSpace)
        {
            result = leftSpace switch
            {
                ValueSpace.DateTime => CompareDateTime(leftLiteral, rightLiteral, implicitTimezone),
                ValueSpace.Date => CompareDate(leftLiteral, rightLiteral, implicitTimezone),
                _ => CompareTime(leftLiteral, rightLiteral, implicitTimezone),
            };
        }

        return true;
    }

    /// <summary>
    /// Compares two literals for <c>ORDER BY</c>/<c>MIN</c>/<c>MAX</c>:
    /// a TOTAL preorder over all literals. Literals are partitioned
    /// into classes — every numeric datatype forms ONE class ordered
    /// by numeric value, each temporal family forms one class ordered
    /// on the implicit-timezone-totalized instant axis, and every
    /// other datatype forms its own class ordered by lexical form —
    /// and the classes are ranked by a static key (the least datatype
    /// IRI among the class's members), so no cross-class comparison
    /// can ever disagree with a within-class one. Within a class,
    /// value-equal-but-distinct literals order deterministically by
    /// datatype IRI then lexical form, an ill-formed member orders
    /// after every well-formed one, and a <c>NaN</c> orders before
    /// every other numeric value.
    /// </summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <param name="implicitTimezone">The implicit timezone applied to naive temporal operands.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    public static int CompareForSort(Literal left, Literal right, TimeSpan implicitTimezone)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Utf8String leftKey = OrderingClassKey(left, out ValueSpace leftSpace);
        Utf8String rightKey = OrderingClassKey(right, out ValueSpace rightSpace);
        int byClass = leftKey.CompareTo(rightKey);
        if(byClass != 0)
        {
            return byClass;
        }

        //Equal class keys imply one shared class, so the value spaces agree and one within-class rule applies.
        return leftSpace switch
        {
            ValueSpace.Numeric => CompareNumericForSort(left, right),
            ValueSpace.Boolean => CompareBooleanForSort(left, right),
            ValueSpace.String => left.Value.CompareTo(right.Value),
            ValueSpace.DateTime or ValueSpace.Date or ValueSpace.Time => CompareTemporalForSort(left, right, leftSpace, implicitTimezone),
            _ => CompareTieBreak(left, right),
        };
    }

    /// <summary>Routes the comparison by value space, with the temporal families consulting the optional implicit timezone.</summary>
    /// <param name="left">The left term.</param>
    /// <param name="right">The right term.</param>
    /// <param name="implicitTimezone">The implicit timezone for naive temporal operands, or <see langword="null"/> for the XSD partial order.</param>
    /// <returns>The comparison verdict.</returns>
    private static ComparisonResult CompareCore(RdfTerm left, RdfTerm right, TimeSpan? implicitTimezone)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        //Both operands must be literals to participate in ordered
        //comparison. SPARQL §17.4.1 explicitly rejects ordering of
        //IRIs and blank nodes.
        if(left is not Literal leftLiteral || right is not Literal rightLiteral)
        {
            return ComparisonResult.Incomparable;
        }

        ValueSpace leftSpace = ValueSpaceClassifier.Classify(leftLiteral.Datatype.Iri);
        ValueSpace rightSpace = ValueSpaceClassifier.Classify(rightLiteral.Datatype.Iri);

        //String space: language-tagged literals share string space
        //with xsd:string, but only when both have the same language
        //tag (or both have none). Mixed-tag comparison is incomparable
        //per SPARQL.
        bool bothString = (leftSpace == ValueSpace.String || leftLiteral.Language is not null)
            && (rightSpace == ValueSpace.String || rightLiteral.Language is not null);
        if(bothString)
        {
            return CompareStrings(leftLiteral, rightLiteral);
        }

        if(leftSpace == ValueSpace.Unknown || rightSpace == ValueSpace.Unknown)
        {
            return ComparisonResult.Incomparable;
        }

        //Numeric tower: members of the tower compare with each other,
        //even across different XSD numeric types, via the promotion
        //lattice in NumericValue.
        if(leftSpace == ValueSpace.Numeric && rightSpace == ValueSpace.Numeric)
        {
            return CompareNumeric(leftLiteral, rightLiteral);
        }

        //Date/time family: only same-kind comparison is defined per
        //XSD. dateTime vs date is incomparable (different value
        //spaces); date vs time is incomparable.
        if(leftSpace != rightSpace
            && !IsDurationPair(leftSpace, rightSpace))
        {
            return ComparisonResult.Incomparable;
        }

        //Same-space dispatch.
        return leftSpace switch
        {
            ValueSpace.Boolean => CompareBoolean(leftLiteral, rightLiteral),
            ValueSpace.DateTime => CompareDateTime(leftLiteral, rightLiteral, implicitTimezone),
            ValueSpace.Date => CompareDate(leftLiteral, rightLiteral, implicitTimezone),
            ValueSpace.Time => CompareTime(leftLiteral, rightLiteral, implicitTimezone),
            ValueSpace.Duration or ValueSpace.YearMonthDuration or ValueSpace.DayTimeDuration
                => CompareDuration(leftLiteral, leftSpace, rightLiteral, rightSpace),
            _ => ComparisonResult.Incomparable,
        };
    }

    /// <summary>Whether the value space is one of the temporal families the implicit timezone totalizes.</summary>
    /// <param name="space">The value space.</param>
    /// <returns><see langword="true"/> for the dateTime, date, and time spaces.</returns>
    private static bool IsTemporal(ValueSpace space)
    {
        return space is ValueSpace.DateTime or ValueSpace.Date or ValueSpace.Time;
    }

    /// <summary>
    /// Whether two datatype IRIs name the SAME temporal family (<c>xsd:dateTime</c> and
    /// <c>xsd:dateTimeStamp</c> are one family; <c>xsd:date</c> and <c>xsd:time</c> each their own) — the
    /// family test a value-index probe recognizer applies before pushing a constant onto a declared axis:
    /// a cross-family constant falls through to the scan, which errors it, so cross-family pushdown would
    /// answer what the scan refuses.
    /// </summary>
    /// <param name="leftDatatypeIri">The first datatype IRI.</param>
    /// <param name="rightDatatypeIri">The second datatype IRI.</param>
    /// <returns><see langword="true"/> when both are temporal and share one family.</returns>
    public static bool AreSameTemporalFamily(Utf8String leftDatatypeIri, Utf8String rightDatatypeIri)
    {
        ValueSpace left = ValueSpaceClassifier.Classify(leftDatatypeIri);

        return IsTemporal(left) && left == ValueSpaceClassifier.Classify(rightDatatypeIri);
    }

    /// <summary>
    /// The static rank key of a literal's ordering class: the least
    /// datatype IRI among the class's members — <c>xsd:byte</c> for
    /// the numeric class, <c>xsd:dateTime</c> for the
    /// dateTime/dateTimeStamp family, and the literal's own datatype
    /// IRI for every single-datatype class (including
    /// language-tagged literals, whose class is their own datatype).
    /// A static key keeps the class order independent of the operand
    /// set, which is what makes the cross-class order transitive.
    /// </summary>
    /// <param name="literal">The literal.</param>
    /// <param name="space">Receives the literal's value space for the within-class dispatch.</param>
    /// <returns>The class key IRI.</returns>
    private static Utf8String OrderingClassKey(Literal literal, out ValueSpace space)
    {
        if(literal.Language is not null)
        {
            //A language-tagged literal sorts in its own datatype's class regardless of value-space glosses, the
            //same contour the pre-partition ordering had.
            space = ValueSpace.Unknown;

            return literal.Datatype.Iri;
        }

        space = ValueSpaceClassifier.Classify(literal.Datatype.Iri);

        return space switch
        {
            ValueSpace.Numeric => Vocabulary.Xsd.ByteValue,
            ValueSpace.DateTime => Vocabulary.Xsd.DateTime,
            ValueSpace.String => Vocabulary.Xsd.String,
            ValueSpace.Boolean => Vocabulary.Xsd.Boolean,
            ValueSpace.Date => Vocabulary.Xsd.Date,
            ValueSpace.Time => Vocabulary.Xsd.Time,
            _ => literal.Datatype.Iri,
        };
    }

    /// <summary>Orders two numeric-class literals for sorting: by value; a <c>NaN</c> before every other value (two <c>NaN</c>s tie-break); an ill-formed member after every well-formed one.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <returns>The sort order sign.</returns>
    private static int CompareNumericForSort(Literal left, Literal right)
    {
        bool leftParsed = NumericValue.TryParse(left.Value.ToString(), left.Datatype.Iri, out NumericValue leftValue);
        bool rightParsed = NumericValue.TryParse(right.Value.ToString(), right.Datatype.Iri, out NumericValue rightValue);
        if(leftParsed != rightParsed)
        {
            return leftParsed ? -1 : 1;
        }

        if(!leftParsed)
        {
            return CompareTieBreak(left, right);
        }

        ComparisonResult comparison = NumericValue.Compare(leftValue, rightValue);
        if(comparison == ComparisonResult.Incomparable)
        {
            //At least one operand is NaN (the tower is otherwise total). A NaN self-compares Incomparable,
            //which is the detection; NaN orders before every number, and two NaNs fall to the tiebreak.
            bool leftIsNaN = NumericValue.Compare(leftValue, leftValue) != ComparisonResult.Equal;
            bool rightIsNaN = NumericValue.Compare(rightValue, rightValue) != ComparisonResult.Equal;

            return leftIsNaN == rightIsNaN ? CompareTieBreak(left, right) : leftIsNaN ? -1 : 1;
        }

        return comparison == ComparisonResult.Equal ? CompareTieBreak(left, right) : ComparisonSign(comparison);
    }

    /// <summary>Orders two boolean-class literals for sorting: <c>false</c> before <c>true</c>; equal values and ill-formed pairs tie-break; an ill-formed member after every well-formed one.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <returns>The sort order sign.</returns>
    private static int CompareBooleanForSort(Literal left, Literal right)
    {
        bool leftParsed = XsdBooleanLexical.TryParse(left.Value.Span, out bool leftValue);
        bool rightParsed = XsdBooleanLexical.TryParse(right.Value.Span, out bool rightValue);
        if(leftParsed != rightParsed)
        {
            return leftParsed ? -1 : 1;
        }

        if(!leftParsed || leftValue == rightValue)
        {
            return CompareTieBreak(left, right);
        }

        return leftValue ? 1 : -1;
    }

    /// <summary>Orders two same-family temporal literals for sorting on the totalized instant axis: by normalized instant; two lexical forms of one instant tie-break deterministically (the value-equal-class tiebreak keys on the comparator's Equal verdict, never on field equality); an ill-formed member after every well-formed one.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <param name="space">The shared temporal value space.</param>
    /// <param name="implicitTimezone">The implicit timezone applied to naive operands.</param>
    /// <returns>The sort order sign.</returns>
    private static int CompareTemporalForSort(Literal left, Literal right, ValueSpace space, TimeSpan implicitTimezone)
    {
        bool leftParsed = TryParseTemporal(left, space, out DateTimeValue leftValue);
        bool rightParsed = TryParseTemporal(right, space, out DateTimeValue rightValue);
        if(leftParsed != rightParsed)
        {
            return leftParsed ? -1 : 1;
        }

        if(!leftParsed)
        {
            return CompareTieBreak(left, right);
        }

        ComparisonResult comparison = DateTimeValue.Compare(leftValue, rightValue, implicitTimezone);

        return comparison == ComparisonResult.Equal ? CompareTieBreak(left, right) : ComparisonSign(comparison);
    }

    /// <summary>The deterministic sequence tiebreak for value-equal (or pairwise unparseable) same-class literals: datatype IRI first, then lexical form as bytes.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <returns>The sort order sign.</returns>
    private static int CompareTieBreak(Literal left, Literal right)
    {
        int byDatatype = left.Datatype.Iri.CompareTo(right.Datatype.Iri);

        return byDatatype != 0 ? byDatatype : left.Value.CompareTo(right.Value);
    }

    /// <summary>Maps an ordered <see cref="ComparisonResult"/> to a comparison sign.</summary>
    /// <param name="comparison">The ordered verdict.</param>
    /// <returns>The sign.</returns>
    private static int ComparisonSign(ComparisonResult comparison)
        => comparison switch
        {
            ComparisonResult.Less => -1,
            ComparisonResult.Greater => 1,
            _ => 0,
        };

    /// <summary>Parses a temporal literal of a known value space, enforcing the <c>xsd:dateTimeStamp</c> timezone requirement.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="space">The literal's temporal value space.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <returns><see langword="true"/> when the lexical form is valid.</returns>
    private static bool TryParseTemporal(Literal literal, ValueSpace space, out DateTimeValue value)
    {
        return space switch
        {
            ValueSpace.DateTime => DateTimeValue.TryParseDateTime(literal.Value.Span, literal.Datatype.Iri == Vocabulary.Xsd.DateTimeStamp, out value),
            ValueSpace.Date => DateTimeValue.TryParseDate(literal.Value.Span, out value),
            _ => DateTimeValue.TryParseTime(literal.Value.Span, out value),
        };
    }

    //A pair of duration spaces (general or restricted) is treated by
    //the duration comparator, which then decides whether the two
    //specific subtype combinations are comparable.
    private static bool IsDurationPair(ValueSpace left, ValueSpace right)
    {
        bool leftIsDuration = left is ValueSpace.Duration
            or ValueSpace.YearMonthDuration
            or ValueSpace.DayTimeDuration;
        bool rightIsDuration = right is ValueSpace.Duration
            or ValueSpace.YearMonthDuration
            or ValueSpace.DayTimeDuration;

        return leftIsDuration && rightIsDuration;
    }

    private static ComparisonResult CompareStrings(Literal left, Literal right)
    {
        //SPARQL: language-tagged literals only compare with each
        //other when their tags match (case-insensitive); otherwise
        //incomparable. Plain xsd:string literals compare with each
        //other but not with language-tagged ones.
        Utf8String? leftLang = left.Language;
        Utf8String? rightLang = right.Language;

        if(leftLang.HasValue != rightLang.HasValue)
        {
            return ComparisonResult.Incomparable;
        }

        if(leftLang.HasValue)
        {
            string leftTag = leftLang.Value.ToString();
            string rightTag = rightLang!.Value.ToString();
            if(!string.Equals(leftTag, rightTag, StringComparison.OrdinalIgnoreCase))
            {
                return ComparisonResult.Incomparable;
            }
        }

        int sign = string.CompareOrdinal(left.Value.ToString(), right.Value.ToString());

        return SignToResult(sign);
    }

    private static ComparisonResult CompareNumeric(Literal left, Literal right)
    {
        if(!NumericValue.TryParse(left.Value.ToString(), left.Datatype.Iri, out NumericValue leftValue)
            || !NumericValue.TryParse(right.Value.ToString(), right.Datatype.Iri, out NumericValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return NumericValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareBoolean(Literal left, Literal right)
    {
        if(!TryParseBoolean(left.Value.ToString(), out bool leftValue)
            || !TryParseBoolean(right.Value.ToString(), out bool rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        //false (0) < true (1).
        int leftInt = leftValue ? 1 : 0;
        int rightInt = rightValue ? 1 : 0;

        return SignToResult(leftInt.CompareTo(rightInt));
    }

    private static ComparisonResult CompareDateTime(Literal left, Literal right, TimeSpan? implicitTimezone)
    {
        bool leftRequiresTz = left.Datatype.Iri == Lumoin.Veritas.Core.Vocabulary.Xsd.DateTimeStamp;
        bool rightRequiresTz = right.Datatype.Iri == Lumoin.Veritas.Core.Vocabulary.Xsd.DateTimeStamp;

        if(!DateTimeValue.TryParseDateTime(left.Value.Span, leftRequiresTz, out DateTimeValue leftValue)
            || !DateTimeValue.TryParseDateTime(right.Value.Span, rightRequiresTz, out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return implicitTimezone is TimeSpan timezone
            ? DateTimeValue.Compare(leftValue, rightValue, timezone)
            : DateTimeValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareDate(Literal left, Literal right, TimeSpan? implicitTimezone)
    {
        if(!DateTimeValue.TryParseDate(left.Value.Span, out DateTimeValue leftValue)
            || !DateTimeValue.TryParseDate(right.Value.Span, out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return implicitTimezone is TimeSpan timezone
            ? DateTimeValue.Compare(leftValue, rightValue, timezone)
            : DateTimeValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareTime(Literal left, Literal right, TimeSpan? implicitTimezone)
    {
        if(!DateTimeValue.TryParseTime(left.Value.Span, out DateTimeValue leftValue)
            || !DateTimeValue.TryParseTime(right.Value.Span, out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return implicitTimezone is TimeSpan timezone
            ? DateTimeValue.Compare(leftValue, rightValue, timezone)
            : DateTimeValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareDuration(
        Literal left, ValueSpace leftSpace,
        Literal right, ValueSpace rightSpace)
    {
        if(!XsdDuration.TryParse(left.Value.ToString(), leftSpace, out XsdDuration leftValue)
            || !XsdDuration.TryParse(right.Value.ToString(), rightSpace, out XsdDuration rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return XsdDuration.Compare(leftValue, leftSpace, rightValue, rightSpace);
    }

    //XSD §3.2.2 boolean lexical mapping, centralized in Core.XsdBooleanLexical.
    private static bool TryParseBoolean(string lexical, out bool result)
    {
        return XsdBooleanLexical.TryParse(lexical, out result);
    }

    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };
}
