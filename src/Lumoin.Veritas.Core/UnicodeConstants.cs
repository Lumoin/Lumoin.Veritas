namespace Lumoin.Veritas.Core;

/// <summary>
/// Named Unicode code-point boundaries shared by the parsers, lexers,
/// and the canonical serializer, in place of bare hexadecimal literals.
/// </summary>
/// <remarks>
/// All values are scalar-value or code-point boundaries drawn from the
/// Unicode standard. They are exposed as static get-only properties so
/// callers reference them by name; the values are compile-time facts of
/// the standard and never change.
/// </remarks>
public static class UnicodeConstants
{
    /// <summary>The first code point of the UTF-16 surrogate range (U+D800).</summary>
    public static int SurrogateRangeFirst => 0xD800;

    /// <summary>The last code point of the UTF-16 surrogate range (U+DFFF).</summary>
    public static int SurrogateRangeLast => 0xDFFF;

    /// <summary>The highest valid Unicode code point (U+10FFFF).</summary>
    public static int MaximumCodePoint => 0x10FFFF;

    /// <summary>The DELETE control character (U+007F).</summary>
    public static int Delete => 0x7F;

    /// <summary>The first printable code point; code points below it are C0 controls (U+0020).</summary>
    public static int FirstNonControlCodePoint => 0x20;

    /// <summary>The first code point of the Arabic Presentation Forms-A noncharacter block (U+FDD0).</summary>
    public static int ArabicPresentationFormNoncharacterFirst => 0xFDD0;

    /// <summary>The last code point of the Arabic Presentation Forms-A noncharacter block (U+FDEF).</summary>
    public static int ArabicPresentationFormNoncharacterLast => 0xFDEF;

    /// <summary>The penultimate code-point offset within any plane, a noncharacter (U+xxFFFE).</summary>
    public static int PlaneNoncharacterPenultimateOffset => 0xFFFE;

    /// <summary>The last code-point offset within any plane, a noncharacter (U+xxFFFF).</summary>
    public static int PlaneNoncharacterLastOffset => 0xFFFF;

    /// <summary>The mask selecting a code point's offset within its plane.</summary>
    public static int PlaneMask => 0xFFFF;

    /// <summary>The bit shift that yields a code point's plane number.</summary>
    public static int PlaneShift => 16;

    /// <summary>The highest Unicode plane number (plane 16).</summary>
    public static int HighestPlane => 0x10;

    /// <summary>
    /// Determines whether a code point is one of the 66 Unicode noncharacters:
    /// the Arabic Presentation Forms-A block U+FDD0..U+FDEF, and U+xxFFFE / U+xxFFFF
    /// for every plane from 0 through 16.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to test.</param>
    /// <returns><c>true</c> when <paramref name="codePoint"/> is a noncharacter; otherwise <c>false</c>.</returns>
    public static bool IsNoncharacter(int codePoint)
    {
        if(codePoint >= ArabicPresentationFormNoncharacterFirst && codePoint <= ArabicPresentationFormNoncharacterLast)
        {
            return true;
        }

        int planeOffset = codePoint & PlaneMask;
        if(planeOffset == PlaneNoncharacterPenultimateOffset || planeOffset == PlaneNoncharacterLastOffset)
        {
            return (codePoint >> PlaneShift) <= HighestPlane;
        }

        return false;
    }
}
