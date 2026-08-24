namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The alphabet predicates of the XML subset — the single home for the
/// character-class questions the scanner's structural pass and its decode
/// paths both ask, so the two can never drift apart. Byte predicates answer
/// questions a single UTF-8 byte decides; scalar predicates answer questions
/// that need the decoded code point.
/// </summary>
internal static class XmlLexicon
{
    /// <summary>True for the XML whitespace set: space, tab, carriage return, line feed.</summary>
    public static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    /// <summary>True for an ASCII decimal digit.</summary>
    public static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    /// <summary>True for an ASCII hexadecimal digit, either case.</summary>
    public static bool IsHexDigit(byte value) =>
        value is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'A' and <= (byte)'F') or (>= (byte)'a' and <= (byte)'f');

    /// <summary>
    /// The numeric value of an ASCII hexadecimal digit byte. The caller
    /// guards with <see cref="IsHexDigit"/>; an unguarded byte yields
    /// garbage, not an exception, because the scan path never presents one.
    /// </summary>
    public static int HexDigitValue(byte value)
    {
        if(value <= (byte)'9')
        {
            return value - (byte)'0';
        }

        if(value <= (byte)'F')
        {
            return value - (byte)'A' + 10;
        }

        return value - (byte)'a' + 10;
    }

    /// <summary>
    /// True when the decoded code point is an XML character: tab, line feed,
    /// carriage return, and the three planes the character production admits.
    /// The two permanent non-characters at the end of the basic plane are
    /// excluded; surrogate code points cannot arrive here because valid UTF-8
    /// cannot encode them.
    /// </summary>
    public static bool IsCharacter(int scalar) =>
        scalar is 0x9 or 0xA or 0xD
            or (>= 0x20 and <= 0xD7FF)
            or (>= 0xE000 and <= 0xFFFD)
            or (>= 0x10000 and <= 0x10FFFF);

    /// <summary>
    /// True when the decoded code point may START a namespace-constrained
    /// name: the fifth-edition name-start ranges with the colon excluded,
    /// because the colon is admitted only as the single prefix separator and
    /// the scanner consumes it structurally.
    /// </summary>
    public static bool IsNameStart(int scalar) =>
        scalar is (>= 'A' and <= 'Z') or '_' or (>= 'a' and <= 'z')
            or (>= 0xC0 and <= 0xD6)
            or (>= 0xD8 and <= 0xF6)
            or (>= 0xF8 and <= 0x2FF)
            or (>= 0x370 and <= 0x37D)
            or (>= 0x37F and <= 0x1FFF)
            or (>= 0x200C and <= 0x200D)
            or (>= 0x2070 and <= 0x218F)
            or (>= 0x2C00 and <= 0x2FEF)
            or (>= 0x3001 and <= 0xD7FF)
            or (>= 0xF900 and <= 0xFDCF)
            or (>= 0xFDF0 and <= 0xFFFD)
            or (>= 0x10000 and <= 0xEFFFF);

    /// <summary>
    /// True when the decoded code point may appear INSIDE a
    /// namespace-constrained name: the start set plus the hyphen, the dot,
    /// the decimal digits, the middle dot, the combining range, and the two
    /// undertie characters.
    /// </summary>
    public static bool IsNameCharacter(int scalar) =>
        IsNameStart(scalar)
            || scalar is '-' or '.' or (>= '0' and <= '9') or 0xB7
                or (>= 0x300 and <= 0x36F)
                or (>= 0x203F and <= 0x2040);
}
