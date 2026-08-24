using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// A compiled JSONata regular-expression value <c>/pattern/flags</c>. Like a lambda or a built-in it is a
/// first-class function value carried in the <see cref="JsonataValue.Function(object)"/> slot, so it is
/// usable everywhere a function value is — bound to a variable, passed as an argument (the matcher argument of
/// <c>$match</c>, <c>$split</c>, <c>$contains</c>, and <c>$replace</c>), or chained through <c>~&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The reference engine models a regex as a closure whose <c>next()</c> resumes matching from a stored
/// <c>lastIndex</c>. This engine forbids closures, so the value is an explicit record carrying the compiled
/// <see cref="System.Text.RegularExpressions.Regex"/> and the consumer drives iteration directly through
/// <see cref="MatchAt"/>, stepping the start index past each match — the same <c>lastIndex</c> walk without a
/// resumable closure.
/// </para>
/// <para>
/// The pattern is a user-supplied value compiled at evaluation, so the <see cref="System.Text.RegularExpressions.Regex"/>
/// is a runtime instance rather than a source-generated one. The JS flags <c>i</c>/<c>m</c>/<c>s</c> are
/// translated to the matching <see cref="RegexOptions"/> at compilation; the JS global flag is implicit in the
/// match-all iteration and carries no option here. The JS and .NET regex flavours diverge on exotic
/// constructs; the corpus patterns are basic, so this is a fragment-relative divergence from the reference.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/regex">the JSONata regular-expressions reference</see>.</para>
/// </remarks>
/// <param name="Source">The pattern text as written between the slashes, without the flags.</param>
/// <param name="Flags">The flag letters as written after the closing slash (a subset of <c>i</c>/<c>m</c>/<c>s</c>); empty when none.</param>
/// <param name="Compiled">The compiled .NET regular expression the flags were translated into.</param>
internal sealed record JsonataRegex(string Source, string Flags, Regex Compiled)
{
    /// <summary>
    /// Runs the regular expression against <paramref name="input"/> starting the scan at
    /// <paramref name="startAt"/>, returning the first match at or after that index. This is the explicit,
    /// closure-free equivalent of the reference's <c>re.lastIndex = startAt; re.exec(str)</c>: the consumer
    /// advances <paramref name="startAt"/> to the end of each match and calls again, never using
    /// <see cref="Match.NextMatch"/> (which would auto-advance past an empty match and hide the zero-length
    /// guard the consumers enforce).
    /// </summary>
    /// <param name="input">The string to scan.</param>
    /// <param name="startAt">The index to begin the scan at; clamped to the string length so an at-end call yields no match.</param>
    /// <returns>The first match result at or after <paramref name="startAt"/>, or <see langword="null"/> when none matches.</returns>
    public JsonataRegexMatch? MatchAt(string input, int startAt)
    {
        ArgumentNullException.ThrowIfNull(input);

        if(startAt < 0)
        {
            startAt = 0;
        }

        if(startAt > input.Length)
        {
            return null;
        }

        Match match = Compiled.Match(input, startAt);
        if(!match.Success)
        {
            return null;
        }

        //The captured groups are groups 1..n in order; an unmatched optional group contributes the undefined
        //value (a null group string) so the consumer can distinguish it from an empty matched group.
        List<string?> groups = [];
        for(int i = 1; i < match.Groups.Count; i++)
        {
            Group group = match.Groups[i];
            groups.Add(group.Success ? group.Value : null);
        }

        return new JsonataRegexMatch(match.Value, match.Index, match.Index + match.Length, groups);
    }
}

/// <summary>
/// One match produced by <see cref="JsonataRegex.MatchAt"/>: the matched substring, its half-open span, and
/// the captured groups. It is the explicit value the reference exposes as <c>{ match, start, end, groups }</c>;
/// <c>$match</c> renames <see cref="Start"/> to <c>index</c> when it builds the result object.
/// </summary>
/// <param name="Match">The matched substring (the whole match, group 0).</param>
/// <param name="Start">The zero-based index of the match within the scanned string.</param>
/// <param name="End">The index one past the end of the match (<see cref="Start"/> plus the match length).</param>
/// <param name="Groups">The captured groups 1..n in order; an unmatched optional group is <see langword="null"/>.</param>
internal readonly record struct JsonataRegexMatch(string Match, int Start, int End, IReadOnlyList<string?> Groups);
