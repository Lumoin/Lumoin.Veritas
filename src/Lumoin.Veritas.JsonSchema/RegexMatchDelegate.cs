namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Tests whether a string matches a regular expression, for the <c>pattern</c> and
/// <c>patternProperties</c> keywords. This is the seam through which an application supplies its own
/// regular-expression engine.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation uses <see cref="System.Text.RegularExpressions.Regex"/>. An application
/// can instead route patterns to source-generated regexes (for example a dispatch over the patterns it
/// knows at build time to <c>[GeneratedRegex]</c> matchers, suitable for trimming and ahead-of-time
/// compilation) or to a stricter ECMA-262 engine, since JSON Schema specifies ECMA-262 regular
/// expressions and the BCL engine diverges on a few constructs.
/// </para>
/// <para>
/// JSON Schema matching is unanchored: the pattern matches when it is found anywhere in the input.
/// </para>
/// </remarks>
/// <param name="pattern">The regular-expression pattern from the schema.</param>
/// <param name="input">The string instance (or member name) to test.</param>
/// <returns><see langword="true"/> when the pattern matches the input.</returns>
public delegate bool RegexMatchDelegate(string pattern, string input);
