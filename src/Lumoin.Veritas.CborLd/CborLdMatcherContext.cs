using System;
using System.Collections.Frozen;
using System.Diagnostics;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Routing context carried alongside a type identifier when a matcher
/// delegate resolves an encoder or decoder. Holds additional parameters
/// — registry metadata, profile flags, consumer-supplied extension keys
/// — that a matcher branch may consult to refine its choice. Most
/// matcher branches do not consult the context; it is available for
/// the cases that genuinely need extra routing dimensions.
/// </summary>
[DebuggerDisplay("Parameters = {Parameters.Count}")]
public sealed class CborLdMatcherContext
{
    /// <summary>
    /// Initialises a new context backed by the supplied frozen
    /// parameter dictionary. Use <see cref="Empty"/> when no parameters
    /// are required.
    /// </summary>
    /// <param name="parameters">The frozen parameter dictionary.</param>
    public CborLdMatcherContext(FrozenDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = parameters;
    }

    /// <summary>A context with no parameters.</summary>
    public static CborLdMatcherContext Empty { get; } =
        new(FrozenDictionary<string, object>.Empty);

    /// <summary>Gets the parameter dictionary.</summary>
    public FrozenDictionary<string, object> Parameters { get; }

    /// <summary>
    /// Reads a parameter as a value of type <typeparamref name="T"/>,
    /// returning <c>null</c> if the key is absent or the value is of a
    /// different type.
    /// </summary>
    /// <typeparam name="T">The expected reference type of the value.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <returns>The parameter value, or <c>null</c>.</returns>
    public T? Get<T>(string key) where T: class
    {
        ArgumentNullException.ThrowIfNull(key);
        return Parameters.TryGetValue(key, out object? value) ? value as T : null;
    }

    /// <summary>
    /// Reads a parameter as a value of type <typeparamref name="T"/>,
    /// throwing if the key is absent or the value is of a different type.
    /// </summary>
    /// <typeparam name="T">The expected reference type of the value.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <returns>The parameter value.</returns>
    /// <exception cref="ArgumentException">The key is missing or the value is of a different type.</exception>
    public T GetRequired<T>(string key) where T: class
    {
        return Get<T>(key)
            ?? throw new ArgumentException(
                $"Required CBOR-LD matcher parameter '{key}' missing or wrong type.");
    }
}
