using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// The XSD-dialect <c>\p{...}</c> / <c>\P{...}</c> general-category name resolver over
/// the checked-in <see cref="UnicodeCategoryData"/> range tables. It exposes the
/// nameable single categories (Lu, Ll, ..., Cn) and the category groups (L, M, N, P,
/// Z, S, C); block names (<c>\p{Is...}</c>) and the surrogate category (Cs) are not
/// resolvable and surface upstream as value-based parse errors.
/// </summary>
internal static class UnicodeCategoryTables
{
    /// <summary>The category and group sets keyed by their one- or two-byte XSD name.</summary>
    private static FrozenDictionary<int, CodePointSet> SetsByName { get; } = BuildSets();

    /// <summary>The decimal-digit category (Nd), the value space of <c>\d</c>.</summary>
    public static CodePointSet DecimalNumber { get; } = Lookup('N', 'd');

    /// <summary>The punctuation group (P), a component of the <c>\w</c> complement base.</summary>
    public static CodePointSet PunctuationGroup { get; } = Lookup('P');

    /// <summary>The separator group (Z), a component of the <c>\w</c> complement base.</summary>
    public static CodePointSet SeparatorGroup { get; } = Lookup('Z');

    /// <summary>The other group (C), a component of the <c>\w</c> complement base.</summary>
    public static CodePointSet OtherGroup { get; } = Lookup('C');

    /// <summary>Resolves a general-category or group name to its universe-bounded code-point set.</summary>
    /// <param name="name">The category or group name (for example <c>Lu</c> or <c>L</c>) as ASCII bytes.</param>
    /// <param name="set">The resolved set, or the empty set when the name is not a nameable category or group.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    public static bool TryGetCategorySet(ReadOnlySpan<byte> name, out CodePointSet set)
    {
        int key = EncodeName(name);
        if(key >= 0 && SetsByName.TryGetValue(key, out CodePointSet? found))
        {
            set = found;

            return true;
        }

        set = CodePointSet.Empty;

        return false;
    }

    /// <summary>Encodes a one- or two-byte category name into the dictionary key, or a negative sentinel for other lengths.</summary>
    /// <param name="name">The name bytes.</param>
    /// <returns>The key, or a negative value when the name cannot be a category name.</returns>
    private static int EncodeName(ReadOnlySpan<byte> name)
    {
        return name.Length switch
        {
            1 => name[0],
            2 => (name[0] << 8) | name[1],
            _ => -1
        };
    }

    /// <summary>Reads a known single-character group name from the built table.</summary>
    /// <param name="group">The group letter.</param>
    /// <returns>The group set.</returns>
    private static CodePointSet Lookup(char group)
    {
        return SetsByName[group];
    }

    /// <summary>Reads a known two-character category name from the built table.</summary>
    /// <param name="first">The first name character.</param>
    /// <param name="second">The second name character.</param>
    /// <returns>The category set.</returns>
    private static CodePointSet Lookup(char first, char second)
    {
        return SetsByName[(first << 8) | second];
    }

    /// <summary>Builds the single-category and group sets once from the generated tables.</summary>
    /// <returns>The name-keyed frozen table.</returns>
    private static FrozenDictionary<int, CodePointSet> BuildSets()
    {
        Dictionary<int, CodePointSet> singles = new()
        {
            [Key('L', 'u')] = FromCategory(UnicodeCategory.UppercaseLetter),
            [Key('L', 'l')] = FromCategory(UnicodeCategory.LowercaseLetter),
            [Key('L', 't')] = FromCategory(UnicodeCategory.TitlecaseLetter),
            [Key('L', 'm')] = FromCategory(UnicodeCategory.ModifierLetter),
            [Key('L', 'o')] = FromCategory(UnicodeCategory.OtherLetter),
            [Key('M', 'n')] = FromCategory(UnicodeCategory.NonSpacingMark),
            [Key('M', 'c')] = FromCategory(UnicodeCategory.SpacingCombiningMark),
            [Key('M', 'e')] = FromCategory(UnicodeCategory.EnclosingMark),
            [Key('N', 'd')] = FromCategory(UnicodeCategory.DecimalDigitNumber),
            [Key('N', 'l')] = FromCategory(UnicodeCategory.LetterNumber),
            [Key('N', 'o')] = FromCategory(UnicodeCategory.OtherNumber),
            [Key('P', 'c')] = FromCategory(UnicodeCategory.ConnectorPunctuation),
            [Key('P', 'd')] = FromCategory(UnicodeCategory.DashPunctuation),
            [Key('P', 's')] = FromCategory(UnicodeCategory.OpenPunctuation),
            [Key('P', 'e')] = FromCategory(UnicodeCategory.ClosePunctuation),
            [Key('P', 'i')] = FromCategory(UnicodeCategory.InitialQuotePunctuation),
            [Key('P', 'f')] = FromCategory(UnicodeCategory.FinalQuotePunctuation),
            [Key('P', 'o')] = FromCategory(UnicodeCategory.OtherPunctuation),
            [Key('Z', 's')] = FromCategory(UnicodeCategory.SpaceSeparator),
            [Key('Z', 'l')] = FromCategory(UnicodeCategory.LineSeparator),
            [Key('Z', 'p')] = FromCategory(UnicodeCategory.ParagraphSeparator),
            [Key('S', 'm')] = FromCategory(UnicodeCategory.MathSymbol),
            [Key('S', 'c')] = FromCategory(UnicodeCategory.CurrencySymbol),
            [Key('S', 'k')] = FromCategory(UnicodeCategory.ModifierSymbol),
            [Key('S', 'o')] = FromCategory(UnicodeCategory.OtherSymbol),
            [Key('C', 'c')] = FromCategory(UnicodeCategory.Control),
            [Key('C', 'f')] = FromCategory(UnicodeCategory.Format),
            [Key('C', 'o')] = FromCategory(UnicodeCategory.PrivateUse),
            [Key('C', 'n')] = FromCategory(UnicodeCategory.OtherNotAssigned),
        };

        Dictionary<int, CodePointSet> all = new(singles)
        {
            [Key('L')] = UnionOf(singles, [Key('L', 'u'), Key('L', 'l'), Key('L', 't'), Key('L', 'm'), Key('L', 'o')]),
            [Key('M')] = UnionOf(singles, [Key('M', 'n'), Key('M', 'c'), Key('M', 'e')]),
            [Key('N')] = UnionOf(singles, [Key('N', 'd'), Key('N', 'l'), Key('N', 'o')]),
            [Key('P')] = UnionOf(singles, [Key('P', 'c'), Key('P', 'd'), Key('P', 's'), Key('P', 'e'), Key('P', 'i'), Key('P', 'f'), Key('P', 'o')]),
            [Key('Z')] = UnionOf(singles, [Key('Z', 's'), Key('Z', 'l'), Key('Z', 'p')]),
            [Key('S')] = UnionOf(singles, [Key('S', 'm'), Key('S', 'c'), Key('S', 'k'), Key('S', 'o')]),
            [Key('C')] = UnionOf(singles, [Key('C', 'c'), Key('C', 'f'), Key('C', 'o'), Key('C', 'n')]),
        };

        return all.ToFrozenDictionary();
    }

    /// <summary>The universe-bounded set of a single general category.</summary>
    /// <param name="category">The general category.</param>
    /// <returns>The category set.</returns>
    private static CodePointSet FromCategory(UnicodeCategory category)
    {
        return XmlCharAlphabet.Bound(CodePointSet.FromPairs(UnicodeCategoryData.Pairs(category)));
    }

    /// <summary>The union of several already-built single-category sets.</summary>
    /// <param name="singles">The single-category table.</param>
    /// <param name="keys">The member keys to union.</param>
    /// <returns>The group set.</returns>
    private static CodePointSet UnionOf(Dictionary<int, CodePointSet> singles, ReadOnlySpan<int> keys)
    {
        CodePointSet result = CodePointSet.Empty;
        foreach(int key in keys)
        {
            result = CodePointSet.Union(result, singles[key]);
        }

        return result;
    }

    /// <summary>The dictionary key of a single-character group name.</summary>
    /// <param name="group">The group letter.</param>
    /// <returns>The key.</returns>
    private static int Key(char group)
    {
        return group;
    }

    /// <summary>The dictionary key of a two-character category name.</summary>
    /// <param name="first">The first name character.</param>
    /// <param name="second">The second name character.</param>
    /// <returns>The key.</returns>
    private static int Key(char first, char second)
    {
        return (first << 8) | second;
    }
}
