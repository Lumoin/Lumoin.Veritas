namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// The XSD-dialect single-letter character-class escapes as universe-bounded code-point
/// sets, pinned to the exact definitions the dialect fixes: <c>\d = \p{Nd}</c>;
/// <c>\s = [#x20 #x9 #xA #xD]</c> (those four only, NOT <c>\p{Z}</c>);
/// <c>\w = complement of [\p{P}\p{Z}\p{C}]</c> (so the connector-punctuation underscore
/// is EXCLUDED); <c>.</c> = <c>[^#xA#xD]</c>; <c>\i</c>/<c>\c</c> the XML name-start and
/// name-character sets. Upper-case forms are the universe complements.
/// </summary>
internal static class XsdCharacterClasses
{
    /// <summary>The <c>\d</c> set — the decimal-digit category.</summary>
    public static CodePointSet Digit { get; } = UnicodeCategoryTables.DecimalNumber;

    /// <summary>The <c>\D</c> set — the universe complement of <c>\d</c>.</summary>
    public static CodePointSet NonDigit { get; } = XmlCharAlphabet.Complement(Digit);

    /// <summary>The <c>\s</c> set — exactly space, tab, line feed, and carriage return.</summary>
    public static CodePointSet Space { get; } = XmlCharAlphabet.Bound(CodePointSet.Of(
    [
        new CodePointRange(0x9, 0x9),
        new CodePointRange(0xA, 0xA),
        new CodePointRange(0xD, 0xD),
        new CodePointRange(0x20, 0x20),
    ]));

    /// <summary>The <c>\S</c> set — the universe complement of <c>\s</c>.</summary>
    public static CodePointSet NonSpace { get; } = XmlCharAlphabet.Complement(Space);

    /// <summary>The <c>\w</c> set — the universe complement of the punctuation, separator, and other groups.</summary>
    public static CodePointSet Word { get; } = XmlCharAlphabet.Complement(
        CodePointSet.Union(
            CodePointSet.Union(UnicodeCategoryTables.PunctuationGroup, UnicodeCategoryTables.SeparatorGroup),
            UnicodeCategoryTables.OtherGroup));

    /// <summary>The <c>\W</c> set — the universe complement of <c>\w</c>.</summary>
    public static CodePointSet NonWord { get; } = XmlCharAlphabet.Complement(Word);

    /// <summary>The <c>\i</c> set — the XML name-start characters.</summary>
    public static CodePointSet InitialName { get; } = XmlNameCharacters.NameStart;

    /// <summary>The <c>\I</c> set — the universe complement of <c>\i</c>.</summary>
    public static CodePointSet NonInitialName { get; } = XmlCharAlphabet.Complement(InitialName);

    /// <summary>The <c>\c</c> set — the XML name characters.</summary>
    public static CodePointSet Name { get; } = XmlNameCharacters.NameChar;

    /// <summary>The <c>\C</c> set — the universe complement of <c>\c</c>.</summary>
    public static CodePointSet NonName { get; } = XmlCharAlphabet.Complement(Name);

    /// <summary>The <c>.</c> set — every universe code point except line feed and carriage return.</summary>
    public static CodePointSet Dot { get; } = XmlCharAlphabet.Complement(CodePointSet.Of(
    [
        new CodePointRange(0xA, 0xA),
        new CodePointRange(0xD, 0xD),
    ]));
}
