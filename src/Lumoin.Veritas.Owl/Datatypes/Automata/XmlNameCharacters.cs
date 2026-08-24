namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// The XML 1.0 (Fifth Edition) <c>NameStartChar</c> and <c>NameChar</c> code-point
/// sets, the value spaces of the XSD-dialect <c>\i</c> and <c>\c</c> escapes. The
/// range lists are hand-encoded from the published grammar productions; <c>\I</c> and
/// <c>\c</c> complements are taken within the XML Char universe.
/// </summary>
internal static class XmlNameCharacters
{
    /// <summary>The <c>NameStartChar</c> set (the initial name characters, <c>\i</c>), bounded to the universe.</summary>
    public static CodePointSet NameStart { get; } = XmlCharAlphabet.Bound(CodePointSet.Of(
    [
        new CodePointRange(0x3A, 0x3A),
        new CodePointRange(0x41, 0x5A),
        new CodePointRange(0x5F, 0x5F),
        new CodePointRange(0x61, 0x7A),
        new CodePointRange(0xC0, 0xD6),
        new CodePointRange(0xD8, 0xF6),
        new CodePointRange(0xF8, 0x2FF),
        new CodePointRange(0x370, 0x37D),
        new CodePointRange(0x37F, 0x1FFF),
        new CodePointRange(0x200C, 0x200D),
        new CodePointRange(0x2070, 0x218F),
        new CodePointRange(0x2C00, 0x2FEF),
        new CodePointRange(0x3001, 0xD7FF),
        new CodePointRange(0xF900, 0xFDCF),
        new CodePointRange(0xFDF0, 0xFFFD),
        new CodePointRange(0x10000, 0xEFFFF),
    ]));

    /// <summary>The <c>NameChar</c> set (any name character, <c>\c</c>), bounded to the universe.</summary>
    public static CodePointSet NameChar { get; } = XmlCharAlphabet.Bound(CodePointSet.Union(NameStart, CodePointSet.Of(
    [
        new CodePointRange(0x2D, 0x2D),
        new CodePointRange(0x2E, 0x2E),
        new CodePointRange(0x30, 0x39),
        new CodePointRange(0xB7, 0xB7),
        new CodePointRange(0x300, 0x36F),
        new CodePointRange(0x203F, 0x2040),
    ])));
}
