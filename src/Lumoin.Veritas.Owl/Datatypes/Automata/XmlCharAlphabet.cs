namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// The alphabet the XSD-dialect pattern automata range over: the XML 1.0 <c>Char</c>
/// production, not the full <c>Sigma*</c> of scalar values. It admits
/// <c>#x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]</c> and
/// excludes <c>#x0</c>, most C0 controls, the surrogate block, and <c>#xFFFE/#xFFFF</c>.
/// Every class complement, negated class, <c>.</c>, and negated escape is bounded to
/// this universe, so a pattern satisfiable only through XML-invalid code points has an
/// empty language rather than an over-approximated one.
/// </summary>
internal static class XmlCharAlphabet
{
    /// <summary>The XML <c>Char</c> code-point universe.</summary>
    public static CodePointSet Universe { get; } = CodePointSet.Of(
    [
        new CodePointRange(0x9, 0x9),
        new CodePointRange(0xA, 0xA),
        new CodePointRange(0xD, 0xD),
        new CodePointRange(0x20, 0xD7FF),
        new CodePointRange(0xE000, 0xFFFD),
        new CodePointRange(0x10000, 0x10FFFF),
    ]);

    /// <summary>Intersects a set with the universe, so every produced atom set is a subset of the alphabet.</summary>
    /// <param name="set">The set to bound.</param>
    /// <returns>The set restricted to the universe.</returns>
    public static CodePointSet Bound(CodePointSet set)
    {
        return CodePointSet.Intersect(set, Universe);
    }

    /// <summary>The complement of a set within the universe — every universe code point not in the set.</summary>
    /// <param name="set">The set to complement.</param>
    /// <returns>The universe-bounded complement.</returns>
    public static CodePointSet Complement(CodePointSet set)
    {
        return CodePointSet.Subtract(Universe, set);
    }
}
