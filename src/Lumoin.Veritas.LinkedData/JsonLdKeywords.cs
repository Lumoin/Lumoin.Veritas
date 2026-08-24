using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// The canonical home for the JSON-LD 1.1 / CBOR-LD keyword tokens. Every
/// algorithm that dispatches on or compares against a keyword references these
/// members rather than re-typing the raw <c>"@…"</c> literal, so the token set
/// has one source of truth.
/// </summary>
/// <remarks>
/// Each token is a <see langword="static"/> <see langword="readonly"/>
/// <see cref="string"/> — one stable instance living in the assembly's data
/// section, exactly as <c>Microsoft.AspNetCore.Http.HttpMethods</c> holds the
/// HTTP verbs and <see cref="Lumoin.Veritas.Core.TextDirections"/> holds the
/// base directions. The <c>IsX</c> helpers and <see cref="Equals"/> take a
/// <see cref="object.ReferenceEquals"/> fast-path before an ordinal compare, so
/// once a token has been canonicalized through
/// <see cref="GetCanonicalizedValue"/> all later matching is a pointer compare.
/// (They are deliberately not <see langword="const"/>: a <c>const</c> is copied
/// into every use site, can never be reference-matched, and would force a
/// value compare everywhere.)
/// </remarks>
/// <seealso href="https://www.w3.org/TR/json-ld11/#keywords"/>
public static class JsonLdKeywords
{
    /// <summary>The <c>@base</c> keyword — the document base IRI directive.</summary>
    public static readonly string Base = "@base";

    /// <summary>The <c>@container</c> keyword — a term's container mapping.</summary>
    public static readonly string Container = "@container";

    /// <summary>The <c>@context</c> keyword — an inline, referenced, or scoped context.</summary>
    public static readonly string Context = "@context";

    /// <summary>The <c>@direction</c> keyword — a base text direction.</summary>
    public static readonly string Direction = "@direction";

    /// <summary>The <c>@graph</c> keyword — a named or default graph object.</summary>
    public static readonly string Graph = "@graph";

    /// <summary>The <c>@id</c> keyword — a node identifier.</summary>
    public static readonly string Id = "@id";

    /// <summary>The <c>@import</c> keyword — a context merged into the containing context.</summary>
    public static readonly string Import = "@import";

    /// <summary>The <c>@included</c> keyword — additional node objects included in a document.</summary>
    public static readonly string Included = "@included";

    /// <summary>The <c>@index</c> keyword — an index value or index-map container.</summary>
    public static readonly string Index = "@index";

    /// <summary>The <c>@json</c> keyword — the JSON literal datatype/coercion.</summary>
    public static readonly string Json = "@json";

    /// <summary>The <c>@language</c> keyword — a language tag or language-map container.</summary>
    public static readonly string Language = "@language";

    /// <summary>The <c>@list</c> keyword — an ordered list object.</summary>
    public static readonly string List = "@list";

    /// <summary>The <c>@nest</c> keyword — a property-nesting alias.</summary>
    public static readonly string Nest = "@nest";

    /// <summary>The <c>@none</c> keyword — the no-discriminator map key.</summary>
    public static readonly string None = "@none";

    /// <summary>The <c>@prefix</c> keyword — whether a term may head a compact IRI.</summary>
    public static readonly string Prefix = "@prefix";

    /// <summary>The <c>@propagate</c> keyword — whether a context propagates into nested nodes.</summary>
    public static readonly string Propagate = "@propagate";

    /// <summary>The <c>@protected</c> keyword — whether term definitions are protected.</summary>
    public static readonly string Protected = "@protected";

    /// <summary>The <c>@reverse</c> keyword — a reverse property or reverse map.</summary>
    public static readonly string Reverse = "@reverse";

    /// <summary>The <c>@set</c> keyword — an unordered set object.</summary>
    public static readonly string Set = "@set";

    /// <summary>The <c>@type</c> keyword — a node type or type-map container.</summary>
    public static readonly string Type = "@type";

    /// <summary>The <c>@value</c> keyword — a value object's literal value.</summary>
    public static readonly string Value = "@value";

    /// <summary>The <c>@version</c> keyword — the processing-mode version directive.</summary>
    public static readonly string Version = "@version";

    /// <summary>The <c>@vocab</c> keyword — the vocabulary mapping directive.</summary>
    public static readonly string Vocab = "@vocab";

    /// <summary>The <c>@default</c> framing keyword — a property's default value when absent from a matched node.</summary>
    public static readonly string Default = "@default";

    /// <summary>The <c>@embed</c> framing keyword — the node-embedding mode (<c>@once</c>/<c>@always</c>/<c>@never</c>/<c>@link</c>).</summary>
    public static readonly string Embed = "@embed";

    /// <summary>The <c>@explicit</c> framing keyword — whether only frame-named properties are kept.</summary>
    public static readonly string Explicit = "@explicit";

    /// <summary>The <c>@omitDefault</c> framing keyword — whether absent properties are omitted instead of defaulted.</summary>
    public static readonly string OmitDefault = "@omitDefault";

    /// <summary>The <c>@preserve</c> framing keyword — wraps a value carried verbatim through framing's compaction.</summary>
    public static readonly string Preserve = "@preserve";

    /// <summary>The <c>@requireAll</c> framing keyword — whether all frame properties must match.</summary>
    public static readonly string RequireAll = "@requireAll";

    /// <summary>Every JSON-LD keyword token, holding the canonical instances above (used for membership tests and canonicalization).</summary>
    public static FrozenSet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Base, Container, Context, Direction, Graph, Id, Import, Included,
            Index, Json, Language, List, Nest, None, Prefix, Propagate,
            Protected, Reverse, Set, Type, Value, Version, Vocab,
            Default, Embed, Explicit, OmitDefault, Preserve, RequireAll
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns whether two keyword tokens are the same, taking a <see cref="object.ReferenceEquals"/> fast-path before an <b>ordinal</b> (case-sensitive) compare — JSON-LD keywords are lower-case <c>"@…"</c> tokens, so the compare is case-sensitive (unlike case-insensitive RFC media types).</summary>
    /// <param name="tokenA">The first token, or <see langword="null"/>.</param>
    /// <param name="tokenB">The second token, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the tokens are equal.</returns>
    public static bool Equals(string? tokenA, string? tokenB)
    {
        return ReferenceEquals(tokenA, tokenB) || StringComparer.Ordinal.Equals(tokenA, tokenB);
    }

    /// <summary>Returns the canonical static instance equal to <paramref name="token"/>, or <paramref name="token"/> unchanged when it is not a keyword. Canonicalizing parsed tokens lets later <c>IsX</c>/<see cref="Equals(string?, string?)"/> calls take the <see cref="object.ReferenceEquals"/> fast-path.</summary>
    /// <param name="token">The token to canonicalize.</param>
    /// <returns>The canonical instance, or <paramref name="token"/> unchanged.</returns>
    public static string GetCanonicalizedValue(string token) => token switch
    {
        _ when IsBase(token) => Base,
        _ when IsContainer(token) => Container,
        _ when IsContext(token) => Context,
        _ when IsDirection(token) => Direction,
        _ when IsGraph(token) => Graph,
        _ when IsId(token) => Id,
        _ when IsImport(token) => Import,
        _ when IsIncluded(token) => Included,
        _ when IsIndex(token) => Index,
        _ when IsJson(token) => Json,
        _ when IsLanguage(token) => Language,
        _ when IsList(token) => List,
        _ when IsNest(token) => Nest,
        _ when IsNone(token) => None,
        _ when IsPrefix(token) => Prefix,
        _ when IsPropagate(token) => Propagate,
        _ when IsProtected(token) => Protected,
        _ when IsReverse(token) => Reverse,
        _ when IsSet(token) => Set,
        _ when IsType(token) => Type,
        _ when IsValue(token) => Value,
        _ when IsVersion(token) => Version,
        _ when IsVocab(token) => Vocab,
        _ => token
    };

    /// <summary>Determines whether a string is a JSON-LD keyword.</summary>
    /// <param name="value">The candidate token, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is one of the keywords in <see cref="All"/>.</returns>
    public static bool IsKeyword(string? value)
    {
        return value is not null && All.Contains(value);
    }

    /// <summary>
    /// Determines whether a key is a context-level directive — one of
    /// <see cref="Base"/>, <see cref="Vocab"/>, <see cref="Language"/>,
    /// <see cref="Direction"/>, <see cref="Propagate"/>, <see cref="Protected"/>,
    /// <see cref="Import"/>, or <see cref="Version"/> — rather than a term
    /// definition. Every other key (including keyword-shaped keys such as
    /// <see cref="Type"/> or <see cref="Context"/>) is processed as a term
    /// definition, which is where their validation/rejection happens.
    /// </summary>
    /// <param name="key">The context-map key, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="key"/> is a context-level directive.</returns>
    public static bool IsContextDirective(string? key)
    {
        return IsBase(key) || IsVocab(key) || IsLanguage(key) || IsDirection(key)
            || IsPropagate(key) || IsProtected(key) || IsImport(key) || IsVersion(key);
    }

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Base"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Base"/>.</returns>
    public static bool IsBase(string? token) => Equals(token, Base);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Container"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Container"/>.</returns>
    public static bool IsContainer(string? token) => Equals(token, Container);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Context"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Context"/>.</returns>
    public static bool IsContext(string? token) => Equals(token, Context);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Direction"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Direction"/>.</returns>
    public static bool IsDirection(string? token) => Equals(token, Direction);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Graph"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Graph"/>.</returns>
    public static bool IsGraph(string? token) => Equals(token, Graph);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Id"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Id"/>.</returns>
    public static bool IsId(string? token) => Equals(token, Id);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Import"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Import"/>.</returns>
    public static bool IsImport(string? token) => Equals(token, Import);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Included"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Included"/>.</returns>
    public static bool IsIncluded(string? token) => Equals(token, Included);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Index"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Index"/>.</returns>
    public static bool IsIndex(string? token) => Equals(token, Index);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Json"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Json"/>.</returns>
    public static bool IsJson(string? token) => Equals(token, Json);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Language"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Language"/>.</returns>
    public static bool IsLanguage(string? token) => Equals(token, Language);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="List"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="List"/>.</returns>
    public static bool IsList(string? token) => Equals(token, List);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Nest"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Nest"/>.</returns>
    public static bool IsNest(string? token) => Equals(token, Nest);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="None"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="None"/>.</returns>
    public static bool IsNone(string? token) => Equals(token, None);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Prefix"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Prefix"/>.</returns>
    public static bool IsPrefix(string? token) => Equals(token, Prefix);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Propagate"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Propagate"/>.</returns>
    public static bool IsPropagate(string? token) => Equals(token, Propagate);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Protected"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Protected"/>.</returns>
    public static bool IsProtected(string? token) => Equals(token, Protected);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Reverse"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Reverse"/>.</returns>
    public static bool IsReverse(string? token) => Equals(token, Reverse);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Set"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Set"/>.</returns>
    public static bool IsSet(string? token) => Equals(token, Set);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Type"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Type"/>.</returns>
    public static bool IsType(string? token) => Equals(token, Type);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Value"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Value"/>.</returns>
    public static bool IsValue(string? token) => Equals(token, Value);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Version"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Version"/>.</returns>
    public static bool IsVersion(string? token) => Equals(token, Version);

    /// <summary>Returns whether <paramref name="token"/> is the <see cref="Vocab"/> keyword.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when equal to <see cref="Vocab"/>.</returns>
    public static bool IsVocab(string? token) => Equals(token, Vocab);
}
