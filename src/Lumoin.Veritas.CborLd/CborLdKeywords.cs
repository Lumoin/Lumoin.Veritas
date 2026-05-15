using System.Collections.Generic;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// The fixed map from JSON-LD keywords to compressed CBOR integer
/// identifiers, per W3C CBOR-LD 1.0 §5.4.1 step 3. Each keyword occupies
/// an even integer; the odd identifier <c>id + 1</c> denotes the plural
/// (array-valued) form of the same keyword.
/// </summary>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#convert-map"/>
public static class CborLdKeywords
{
    private static Dictionary<string, int> ForwardMap { get; } = new()
    {
        ["@context"] = 0,
        ["@type"] = 2,
        ["@id"] = 4,
        ["@value"] = 6,
        ["@direction"] = 8,
        ["@graph"] = 10,
        ["@included"] = 12,
        ["@index"] = 14,
        ["@json"] = 16,
        ["@language"] = 18,
        ["@list"] = 20,
        ["@nest"] = 22,
        ["@reverse"] = 24,
        ["@base"] = 26,
        ["@container"] = 28,
        ["@default"] = 30,
        ["@embed"] = 32,
        ["@explicit"] = 34,
        ["@none"] = 36,
        ["@omitDefault"] = 38,
        ["@prefix"] = 40,
        ["@preserve"] = 42,
        ["@protected"] = 44,
        ["@requireAll"] = 46,
        ["@set"] = 48,
        ["@version"] = 50,
        ["@vocab"] = 52,
        ["@propagate"] = 54,
    };

    private static Dictionary<int, string> ReverseMap { get; } = BuildReverse(ForwardMap);

    /// <summary>Gets the mapping from JSON-LD keyword to compressed integer id.</summary>
    public static IReadOnlyDictionary<string, int> KeywordsToIds => ForwardMap;

    /// <summary>Gets the mapping from compressed integer id back to JSON-LD keyword.</summary>
    public static IReadOnlyDictionary<int, string> IdsToKeywords => ReverseMap;

    private static Dictionary<int, string> BuildReverse(Dictionary<string, int> forward)
    {
        Dictionary<int, string> reverse = new(forward.Count);
        foreach(KeyValuePair<string, int> entry in forward)
        {
            reverse[entry.Value] = entry.Key;
        }
        return reverse;
    }
}
