using System;

namespace Lumoin.Veritas.Core;

/// <summary>
/// The XSD boolean lexical space (XML Schema Datatypes §3.2.2): the canonical
/// forms <c>true</c>/<c>false</c> and the permitted numeric alternatives
/// <c>1</c>/<c>0</c>, with the lexical-to-value mapping centralized so every
/// RDF/JSON-LD reader and writer agrees on one definition.
/// </summary>
public static class XsdBooleanLexical
{
    /// <summary>The canonical lexical form for <see langword="true"/>.</summary>
    public const string True = "true";

    /// <summary>The canonical lexical form for <see langword="false"/>.</summary>
    public const string False = "false";

    /// <summary>The permitted numeric lexical form for <see langword="true"/>.</summary>
    public const string TrueNumeric = "1";

    /// <summary>The permitted numeric lexical form for <see langword="false"/>.</summary>
    public const string FalseNumeric = "0";

    /// <summary>Returns the canonical lexical form of a boolean value.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns><see cref="True"/> or <see cref="False"/>.</returns>
    public static string Canonical(bool value)
    {
        return value ? True : False;
    }

    /// <summary>
    /// Maps an XSD boolean lexical form to its value (§3.2.2): <c>true</c>/<c>1</c>
    /// yield <see langword="true"/>, <c>false</c>/<c>0</c> yield
    /// <see langword="false"/>, and anything else is not a valid boolean.
    /// </summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="value">The mapped boolean value when valid.</param>
    /// <returns><see langword="true"/> when <paramref name="lexical"/> is a valid XSD boolean.</returns>
    public static bool TryParse(string lexical, out bool value)
    {
        (bool matched, value) = lexical switch
        {
            True or TrueNumeric => (true, true),
            False or FalseNumeric => (true, false),
            _ => (false, default(bool))
        };

        return matched;
    }

    /// <summary>
    /// Maps a UTF-8 XSD boolean lexical form to its value (§3.2.2): <c>true</c>/<c>1</c>
    /// yield <see langword="true"/>, <c>false</c>/<c>0</c> yield
    /// <see langword="false"/>, and anything else is not a valid boolean.
    /// </summary>
    /// <param name="lexical">The UTF-8 lexical form.</param>
    /// <param name="value">The mapped boolean value when valid.</param>
    /// <returns><see langword="true"/> when <paramref name="lexical"/> is a valid XSD boolean.</returns>
    public static bool TryParse(ReadOnlySpan<byte> lexical, out bool value)
    {
        value = lexical.SequenceEqual("true"u8) || lexical.SequenceEqual("1"u8);

        return value || lexical.SequenceEqual("false"u8) || lexical.SequenceEqual("0"u8);
    }
}
