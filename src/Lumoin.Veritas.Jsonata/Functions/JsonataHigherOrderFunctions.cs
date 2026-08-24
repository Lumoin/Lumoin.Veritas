using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The higher-order built-in functions, exposed for the registry: the array functions <c>$map</c>,
/// <c>$filter</c>, <c>$single</c>, <c>$reduce</c>, and <c>$sort</c>, plus the object functions <c>$sift</c> and
/// <c>$each</c>. Each is a <see cref="JsonataHigherOrderFunction"/> value the registry pre-wraps under its bare
/// name, exactly like the synchronous built-in categories.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/higher-order-functions">the JSONata higher-order-functions reference</see>.</remarks>
internal static class JsonataHigherOrderFunctions
{
    /// <summary>The higher-order built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataHigherOrderFunction> All { get; } =
    [
        new JsonataHigherOrderFunction(Utf8Strings.From("map"), HigherOrderKind.Map),
        new JsonataHigherOrderFunction(Utf8Strings.From("filter"), HigherOrderKind.Filter),
        new JsonataHigherOrderFunction(Utf8Strings.From("single"), HigherOrderKind.Single),
        new JsonataHigherOrderFunction(Utf8Strings.From("reduce"), HigherOrderKind.Reduce),
        new JsonataHigherOrderFunction(Utf8Strings.From("sort"), HigherOrderKind.Sort),
        new JsonataHigherOrderFunction(Utf8Strings.From("sift"), HigherOrderKind.Sift),
        new JsonataHigherOrderFunction(Utf8Strings.From("each"), HigherOrderKind.Each)
    ];
}
