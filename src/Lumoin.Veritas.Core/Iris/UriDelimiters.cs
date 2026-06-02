namespace Lumoin.Veritas.Core.Iris;

/// <summary>
/// The canonical home for the RFC 3986 / RFC 3987 structural delimiters that
/// separate an IRI's components, so parsers, resolvers, and recomposers
/// reference these names rather than re-typing the magic character.
/// </summary>
/// <remarks>
/// These are <see langword="const"/> (unlike the <c>static readonly</c> token
/// sets such as <see cref="Lumoin.Veritas.Core.TextDirections"/>): a delimiter
/// is a single character (or the two-character authority prefix) that is only
/// ever scanned for or appended — value comparisons throughout — so there is no
/// canonicalization or <see cref="object.ReferenceEquals"/> fast-path to gain
/// from a shared instance.
/// </remarks>
/// <seealso href="https://www.rfc-editor.org/rfc/rfc3986#section-3"/>
public static class UriDelimiters
{
    /// <summary>The <c>':'</c> that terminates the scheme component.</summary>
    public const char SchemeSeparator = ':';

    /// <summary>The <c>'/'</c> that separates path segments (and introduces the path after an authority).</summary>
    public const char PathSeparator = '/';

    /// <summary>The <c>'?'</c> that introduces the query component.</summary>
    public const char QueryPrefix = '?';

    /// <summary>The <c>'#'</c> that introduces the fragment component.</summary>
    public const char FragmentPrefix = '#';

    /// <summary>The <c>"//"</c> that introduces the authority component.</summary>
    public const string AuthorityPrefix = "//";
}
