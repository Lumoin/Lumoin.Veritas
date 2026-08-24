using System;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A span recognizer for the lexical shape of a DGGS geometry literal: an IRI identifying the specific
/// discrete global grid system, enclosed in angle brackets, followed by at least one whitespace character
/// as a separator, and then the DGGS geometry data. The grammar is flat, so recognition is one forward
/// pass with no frame stack, no recursion, and no runtime regular expressions.
/// </summary>
/// <remarks>
/// <para>
/// The certified region is the prefix for every grid, plus the whole body for the house A5 flavour: the
/// empty lexical form is well-formed and denotes the empty geometry; a non-empty form must open with
/// <c>&lt;</c> at offset zero, carry a non-empty IRI region terminated by <c>&gt;</c>, and separate the
/// geometry data with at least one whitespace character. The IRI region rejects a raw <c>&lt;</c>, the
/// separator whitespace characters, every other control character, and the delete character, because an
/// IRI cannot contain them raw; anything subtler passes, since full IRI-grammar certification is not this
/// recognizer's jurisdiction. When the IRI is exactly <see cref="A5DggsVocabulary.GridIri"/>, the
/// geometry data is certified against the house A5 body grammar (<see cref="A5DggsBody"/>) and answers
/// well-formed or malformed.
/// </para>
/// <para>
/// The abstention set is every non-empty geometry-data body after a valid prefix whose IRI names a
/// FOREIGN grid: that data is formulated according to the DGGS the IRI identifies, which the
/// specification expressly does not delve into, so the recognizer answers
/// <see cref="GeometryLexicalRecognition.Unrecognized"/> for it without any claim in either direction.
/// An empty body after a valid prefix is a prefix without geometry data and is malformed. A
/// whitespace-only lexical form is not the empty form and carries no angle-bracket prefix, so it is
/// malformed. <see cref="GeometryLexicalRecognition.DepthExceeded"/> is structurally unreachable in a
/// flat grammar and is never answered.
/// </para>
/// </remarks>
public static class DggsLexical
{
    /// <summary>The prefix opener, a <c>&lt;</c>.</summary>
    private const byte AngleOpen = (byte)'<';

    /// <summary>The prefix terminator, a <c>&gt;</c>.</summary>
    private const byte AngleClose = (byte)'>';

    /// <summary>The exclusive upper bound of the C0 control characters, all rejected inside the IRI region.</summary>
    private const byte ControlUpperBound = 0x20;

    /// <summary>The delete character, rejected inside the IRI region.</summary>
    private const byte Delete = 0x7F;

    /// <summary>The offset a recognition reports when no byte of the form offends.</summary>
    private const int NoOffendingByte = -1;

    /// <summary>Lexically recognizes one DGGS geometry literal form.</summary>
    /// <param name="body">The candidate lexical form as UTF-8 bytes.</param>
    /// <param name="offendingOffset">
    /// The offset into <paramref name="body"/> of the first offending byte when the answer is
    /// <see cref="GeometryLexicalRecognition.Malformed"/> — for an offense of absence, the byte at which
    /// the violation became inevitable, which is the form's length when the grammar ran out of input.
    /// Minus one for every other answer: a well-formed form and the foreign-grid abstention name no
    /// offending byte.
    /// </param>
    /// <returns>The recognition outcome; the empty form is well-formed (an empty geometry).</returns>
    public static GeometryLexicalRecognition Recognize(ReadOnlySpan<byte> body, out int offendingOffset)
    {
        offendingOffset = NoOffendingByte;
        if(body.Length == 0)
        {
            return GeometryLexicalRecognition.WellFormed;
        }

        if(!TryDecompose(body, out Range iriRegion, out Range dataRegion, out offendingOffset))
        {
            return GeometryLexicalRecognition.Malformed;
        }

        if(body[iriRegion].SequenceEqual(A5DggsVocabulary.GridIri.Span))
        {
            if(A5DggsBody.Certify(body[dataRegion], out int dataOffset))
            {
                return GeometryLexicalRecognition.WellFormed;
            }

            offendingOffset = dataRegion.Start.Value + dataOffset;

            return GeometryLexicalRecognition.Malformed;
        }

        return GeometryLexicalRecognition.Unrecognized;
    }

    /// <summary>
    /// Decomposes a non-empty DGGS literal form into its grid-IRI region and its geometry-data region,
    /// enforcing the prefix grammar: <c>&lt;</c> at offset zero, a non-empty IRI region free of raw
    /// whitespace, control, delete, and <c>&lt;</c> characters, a <c>&gt;</c> terminator, at least one
    /// separator whitespace character, and a non-empty data region.
    /// </summary>
    /// <param name="body">The non-empty candidate lexical form.</param>
    /// <param name="iriRegion">The grid-IRI region, between the angle brackets.</param>
    /// <param name="dataRegion">The geometry-data region, after the separator.</param>
    /// <param name="offendingOffset">
    /// The offset into <paramref name="body"/> of the first byte at which the prefix grammar could not be
    /// extended, or minus one when the grammar holds. A missing opener names offset zero, an unterminated
    /// IRI region and a missing data region name the form's length, and an empty IRI region names the
    /// terminator's offset — the byte at which the region had to carry content.
    /// </param>
    /// <returns><see langword="true"/> when the prefix grammar holds.</returns>
    internal static bool TryDecompose(ReadOnlySpan<byte> body, out Range iriRegion, out Range dataRegion, out int offendingOffset)
    {
        iriRegion = default;
        dataRegion = default;
        if(body.Length == 0 || body[0] != AngleOpen)
        {
            offendingOffset = 0;

            return false;
        }

        int index = 1;
        while(index < body.Length && body[index] != AngleClose)
        {
            byte value = body[index];
            if(value == AngleOpen || value == Delete || value < ControlUpperBound || IsSeparatorWhitespace(value))
            {
                offendingOffset = index;

                return false;
            }

            index++;
        }

        if(index == body.Length || index == 1)
        {
            offendingOffset = index;

            return false;
        }

        iriRegion = new Range(1, index);
        index++;
        int separatorStart = index;
        while(index < body.Length && IsSeparatorWhitespace(body[index]))
        {
            index++;
        }

        if(index == separatorStart || index == body.Length)
        {
            offendingOffset = index;

            return false;
        }

        dataRegion = new Range(index, body.Length);
        offendingOffset = NoOffendingByte;

        return true;
    }

    /// <summary>Classifies a separator whitespace character: a space, a horizontal tab, a carriage return, or a line feed.</summary>
    /// <param name="value">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte is separator whitespace.</returns>
    internal static bool IsSeparatorWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }
}
