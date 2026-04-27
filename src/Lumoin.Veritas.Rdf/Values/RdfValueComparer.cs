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
/// numeric-range and pair-property constraints, and by any future
/// SPARQL filter or OWL reasoner that needs ordered comparison of
/// literals. It dispatches on the operands' XSD value spaces:
/// </para>
/// <list type="bullet">
///   <item><description>Numeric tower (integer / decimal / float / double, plus derived integer types) — compared via promotion lattice.</description></item>
///   <item><description><c>xsd:string</c> — ordinal Unicode codepoint comparison.</description></item>
///   <item><description><c>xsd:boolean</c> — <c>false &lt; true</c>.</description></item>
///   <item><description><c>xsd:dateTime</c>, <c>xsd:date</c>, <c>xsd:time</c> — instant comparison with XSD §3.2.7.4 indeterminate handling for naive vs aware.</description></item>
///   <item><description><c>xsd:duration</c> — partial order via XSD §3.2.6.2 four-test-points; restricted subtypes total order within their kind.</description></item>
/// </list>
/// <para>
/// Operands in different value spaces, ill-formed lexical forms,
/// non-literal terms, and indeterminate cases all produce
/// <see cref="ComparisonResult.Incomparable"/>. Callers decide what
/// that means in their context — SHACL constraint evaluators treat
/// it as non-conformance, a future filter engine might treat it as
/// filter-rejected, etc.
/// </para>
/// </remarks>
public static class RdfValueComparer
{
    /// <summary>
    /// Compares <paramref name="left"/> and <paramref name="right"/>.
    /// Returns <see cref="ComparisonResult.Incomparable"/> when the
    /// operands cannot be ordered under SPARQL semantics.
    /// </summary>
    public static ComparisonResult Compare(RdfTerm left, RdfTerm right)
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
            ValueSpace.DateTime => CompareDateTime(leftLiteral, rightLiteral),
            ValueSpace.Date => CompareDate(leftLiteral, rightLiteral),
            ValueSpace.Time => CompareTime(leftLiteral, rightLiteral),
            ValueSpace.Duration or ValueSpace.YearMonthDuration or ValueSpace.DayTimeDuration
                => CompareDuration(leftLiteral, leftSpace, rightLiteral, rightSpace),
            _ => ComparisonResult.Incomparable,
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

    private static ComparisonResult CompareDateTime(Literal left, Literal right)
    {
        bool leftRequiresTz = left.Datatype.Iri == Lumoin.Veritas.Core.Vocabulary.Xsd.DateTimeStamp;
        bool rightRequiresTz = right.Datatype.Iri == Lumoin.Veritas.Core.Vocabulary.Xsd.DateTimeStamp;

        if(!DateTimeValue.TryParseDateTime(left.Value.ToString(), leftRequiresTz, out DateTimeValue leftValue)
            || !DateTimeValue.TryParseDateTime(right.Value.ToString(), rightRequiresTz, out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return DateTimeValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareDate(Literal left, Literal right)
    {
        if(!DateTimeValue.TryParseDate(left.Value.ToString(), out DateTimeValue leftValue)
            || !DateTimeValue.TryParseDate(right.Value.ToString(), out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return DateTimeValue.Compare(leftValue, rightValue);
    }

    private static ComparisonResult CompareTime(Literal left, Literal right)
    {
        if(!DateTimeValue.TryParseTime(left.Value.ToString(), out DateTimeValue leftValue)
            || !DateTimeValue.TryParseTime(right.Value.ToString(), out DateTimeValue rightValue))
        {
            return ComparisonResult.Incomparable;
        }

        return DateTimeValue.Compare(leftValue, rightValue);
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

    //XSD §3.2.2: lexical forms "true" / "1" map to true,
    //"false" / "0" map to false. Anything else is invalid. Switch
    //expression with tuple result so the out parameter is assigned
    //once at the call site.
    private static bool TryParseBoolean(string lexical, out bool result)
    {
        (bool matched, result) = lexical switch
        {
            "true" or "1" => (true, true),
            "false" or "0" => (true, false),
            _ => (false, default(bool)),
        };

        return matched;
    }

    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };
}
