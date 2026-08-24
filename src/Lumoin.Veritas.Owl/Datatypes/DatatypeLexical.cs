using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The lexical primitives the declarative datatype tier shares: decoding a literal's UTF-8 value into
/// Unicode code points and counting its runes (never through <see cref="string"/>), the white-space
/// discipline that gates a lexical-identity <c>SameValue</c>, and the value identity and enumeration
/// membership an enumerated or bounded value space answers over the exact-real numeric line or lexical
/// codepoint equality.
/// </summary>
internal static class DatatypeLexical
{
    /// <summary>Decodes a UTF-8 value into its Unicode code points.</summary>
    /// <param name="value">The UTF-8 value.</param>
    /// <returns>The code points, in order.</returns>
    public static int[] CodePoints(Utf8String value)
    {
        List<int> codePoints = [];
        System.ReadOnlySpan<byte> bytes = value.Span;
        while(!bytes.IsEmpty)
        {
            System.Buffers.OperationStatus status = Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed);
            if(status != System.Buffers.OperationStatus.Done)
            {
                codePoints.Add(Rune.ReplacementChar.Value);
                bytes = bytes[(consumed > 0 ? consumed : 1)..];

                continue;
            }

            codePoints.Add(rune.Value);
            bytes = bytes[consumed..];
        }

        return [.. codePoints];
    }

    /// <summary>Encodes a code-point sequence into a UTF-8 value, skipping any non-scalar code point.</summary>
    /// <param name="codePoints">The code points.</param>
    /// <returns>The UTF-8 value.</returns>
    public static Utf8String Utf8FromCodePoints(System.ReadOnlySpan<int> codePoints)
    {
        List<byte> bytes = [];
        System.Span<byte> buffer = stackalloc byte[4];
        foreach(int codePoint in codePoints)
        {
            if(Rune.TryCreate(codePoint, out Rune rune))
            {
                int written = rune.EncodeToUtf8(buffer);
                for(int i = 0; i < written; i++)
                {
                    bytes.Add(buffer[i]);
                }
            }
        }

        return new Utf8String(bytes.ToArray());
    }

    /// <summary>Counts the runes of a UTF-8 value — the XSD string length in characters.</summary>
    /// <param name="value">The UTF-8 value.</param>
    /// <returns>The rune count.</returns>
    public static int RuneCount(Utf8String value)
    {
        int count = 0;
        System.ReadOnlySpan<byte> bytes = value.Span;
        while(!bytes.IsEmpty)
        {
            System.Buffers.OperationStatus status = Rune.DecodeFromUtf8(bytes, out _, out int consumed);
            bytes = bytes[(status == System.Buffers.OperationStatus.Done ? consumed : (consumed > 0 ? consumed : 1))..];
            count++;
        }

        return count;
    }

    /// <summary>Whether a base datatype preserves white space, so a lexical-identity value comparison is injective over it.</summary>
    /// <param name="baseIri">The base datatype IRI.</param>
    /// <returns><see langword="true"/> when the base is white-space preserving (<c>xsd:string</c>).</returns>
    public static bool IsPreserveWhiteSpace(Utf8String baseIri)
    {
        return baseIri.Equals(Vocabulary.Xsd.String);
    }

    /// <summary>
    /// The three-valued value identity of two literals over the exact-real numeric line when both are
    /// exact-real, and lexical codepoint identity otherwise — <see cref="DatatypeValueIdentity.Same"/> on
    /// an exact lexical match, <see cref="DatatypeValueIdentity.Distinct"/> for two distinct lexical forms of
    /// one white-space-preserving datatype, and <see cref="DatatypeValueIdentity.Indeterminate"/> when the
    /// value spaces are not comparable this way.
    /// </summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <returns>The identity verdict.</returns>
    public static DatatypeValueIdentity Identity(Literal first, Literal second)
    {
        if(OwlDatatypeFamilies.NumericSpaceOf(first.Datatype.Iri) == OwlNumericSpace.ExactReal
            && OwlDatatypeFamilies.NumericSpaceOf(second.Datatype.Iri) == OwlNumericSpace.ExactReal)
        {
            return NumericIdentity(first, second);
        }

        if(first.Language is not null || second.Language is not null)
        {
            return first.Equals(second) ? DatatypeValueIdentity.Same : DatatypeValueIdentity.Indeterminate;
        }

        if(first.Value.Equals(second.Value) && first.Datatype.Iri.Equals(second.Datatype.Iri))
        {
            return DatatypeValueIdentity.Same;
        }

        if(first.Datatype.Iri.Equals(second.Datatype.Iri))
        {
            //Two distinct lexical forms of one white-space-preserving datatype denote distinct values.
            return DatatypeValueIdentity.Distinct;
        }

        return DatatypeValueIdentity.Indeterminate;
    }

    /// <summary>The three-valued membership of a value in a finite enumeration by value identity.</summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="members">The enumerated members.</param>
    /// <returns>The membership verdict.</returns>
    public static DatatypeMembership EnumerationMembership(Literal value, IReadOnlyList<Literal> members)
    {
        bool allDistinct = true;
        foreach(Literal member in members)
        {
            DatatypeValueIdentity identity = Identity(value, member);
            if(identity == DatatypeValueIdentity.Same)
            {
                return DatatypeMembership.In;
            }

            if(identity != DatatypeValueIdentity.Distinct)
            {
                allDistinct = false;
            }
        }

        return allDistinct ? DatatypeMembership.Out : DatatypeMembership.Indeterminate;
    }

    /// <summary>The value identity of two exact-real numeric literals.</summary>
    /// <param name="first">The first literal.</param>
    /// <param name="second">The second literal.</param>
    /// <returns>The identity verdict.</returns>
    private static DatatypeValueIdentity NumericIdentity(Literal first, Literal second)
    {
        if(!OwlNumericLexicals.TryGetValue(first.Value.ToString(), first.Datatype.Iri, out NumericValue firstValue)
            || !OwlNumericLexicals.TryGetValue(second.Value.ToString(), second.Datatype.Iri, out NumericValue secondValue))
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        return NumericValue.Compare(firstValue, secondValue) switch
        {
            ComparisonResult.Equal => DatatypeValueIdentity.Same,
            ComparisonResult.Less or ComparisonResult.Greater => DatatypeValueIdentity.Distinct,
            _ => DatatypeValueIdentity.Indeterminate
        };
    }
}
