using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The shared regular-expression match walk for the matcher-accepting string functions (<c>$match</c>,
/// <c>$split</c>, <c>$contains</c>, <c>$replace</c>) and the evaluator's function-replacement cursor. It
/// iterates a compiled regular expression's non-overlapping matches over a string by stepping the start index
/// past each match — the explicit, closure-free equivalent of the reference's resumable matcher closure — and
/// raises D1004 when a continuation match is zero-length (the scan would not progress).
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/regex">the JSONata regular-expressions reference</see>.</remarks>
internal static class JsonataRegexEngine
{
    /// <summary>
    /// Iterates the non-overlapping matches of a regular expression over a string, left to right, stepping the
    /// start index to the end of each match. The first match is yielded unconditionally (it may be
    /// zero-length); a subsequent continuation match of length zero throws D1004 because the scan would not
    /// progress. Iteration calls <see cref="JsonataRegex.MatchAt"/> directly, never
    /// <see cref="System.Text.RegularExpressions.Match.NextMatch"/>, so the zero-length guard is observed.
    /// </summary>
    /// <param name="regex">The regular expression to match with.</param>
    /// <param name="subject">The string to scan.</param>
    /// <returns>The matches in left-to-right order.</returns>
    /// <exception cref="JsonataErrorException">A continuation match is zero-length (code D1004).</exception>
    public static IEnumerable<JsonataRegexMatch> IterateMatches(JsonataRegex regex, string subject)
    {
        JsonataRegexMatch? current = regex.MatchAt(subject, 0);
        bool isFirst = true;
        while(current is JsonataRegexMatch match)
        {
            if(!isFirst && match.Match.Length == 0)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.RegexZeroLengthMatch, null, "The regular expression matched a zero-length string; the scan would not progress.");
            }

            yield return match;
            isFirst = false;

            //The walk stops once a match reaches the end of the subject, mirroring the reference matcher's
            //`lastIndex >= length` guard (lastIndex is the match end); continuing would re-match an empty string
            //at the end and raise a spurious zero-length fault.
            if(match.End >= subject.Length)
            {
                yield break;
            }

            current = regex.MatchAt(subject, match.End);
        }
    }

    /// <summary>
    /// Builds a <c>$match</c>-shaped result object <c>{ "match", "index", "groups" }</c> from one match: the
    /// matched substring, its start index, and the captured groups (an unmatched optional group is the
    /// undefined value, which omits from the serialised array, matching the reference).
    /// </summary>
    /// <param name="match">The match to render.</param>
    /// <returns>The match object value.</returns>
    public static JsonataValue BuildMatchObject(JsonataRegexMatch match)
    {
        List<JsonataValue> groups = [];
        foreach(string? group in match.Groups)
        {
            groups.Add(group is null ? JsonataValue.Undefined : JsonataValue.String(group));
        }

        return JsonataValue.Object(
        [
            new KeyValuePair<string, JsonataValue>("match", JsonataValue.String(match.Match)),
            new KeyValuePair<string, JsonataValue>("index", JsonataValue.Number(match.Start)),
            new KeyValuePair<string, JsonataValue>("groups", JsonataValue.Array(groups))
        ]);
    }
}
