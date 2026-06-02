using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Parses W3C CBOR-LD 1.0 registry JSON into <see cref="CborLdRegistryEntry"/>
/// instances. The loader reads through the backend-agnostic <see cref="JsonNode"/>
/// model; the concrete JSON parser is supplied as a <see cref="ParseJsonDelegate"/>,
/// so this project carries no dependency on a specific JSON library.
/// </summary>
/// <remarks>
/// <para>
/// Supported entry fields:
/// </para>
/// <list type="bullet">
/// <item><description><c>id</c> (required, integer) — the registry entry identifier.</description></item>
/// <item><description><c>keywords</c> (optional, object) — keyword-to-id codec table.</description></item>
/// <item><description><c>terms</c> (optional, object) — term-to-id codec table. Values may be a
/// bare integer (untyped term) or an object <c>{ "id": int, "type": string|null }</c>.</description></item>
/// <item><description><c>typeTables</c> (optional, object) — type-table sources keyed by type name.
/// Values may be the spec sentinel string <c>"callerProvidedTable"</c> (caller-provided marker)
/// or an object containing string-to-integer mappings (registry-provided table).</description></item>
/// <item><description><c>processingModel</c> (optional, string, defaults to <c>"default"</c>).</description></item>
/// <item><description><c>provisional</c> (optional, boolean, defaults to <c>false</c>).</description></item>
/// </list>
/// <para>
/// See <see href="https://www.w3.org/TR/cbor-ld-10/#registry">W3C CBOR-LD 1.0 — Registry</see>.
/// </para>
/// </remarks>
public static class CborLdRegistryJsonLoader
{
    /// <summary>
    /// Parses a single registry-entry JSON document.
    /// </summary>
    /// <param name="utf8Json">UTF-8 encoded JSON describing one registry entry.</param>
    /// <param name="parse">The JSON parser that turns the bytes into a <see cref="JsonNode"/> tree.</param>
    /// <returns>The parsed registry entry.</returns>
    /// <exception cref="CborLdProcessingException">The JSON is not a registry entry, or a required field is missing or malformed.</exception>
    public static CborLdRegistryEntry LoadEntry(Utf8String utf8Json, ParseJsonDelegate parse)
    {
        JsonNode root = ParseRoot(utf8Json, parse);

        return LoadEntryFromNode(root);
    }

    /// <summary>
    /// Parses a JSON document containing an array of registry entries.
    /// </summary>
    /// <param name="utf8Json">UTF-8 encoded JSON containing a top-level array of entries.</param>
    /// <param name="parse">The JSON parser that turns the bytes into a <see cref="JsonNode"/> tree.</param>
    /// <returns>The parsed entries in document order.</returns>
    /// <exception cref="CborLdProcessingException">The JSON is not an array, or any entry is malformed.</exception>
    public static IReadOnlyList<CborLdRegistryEntry> LoadEntries(Utf8String utf8Json, ParseJsonDelegate parse)
    {
        JsonNode root = ParseRoot(utf8Json, parse);
        if(root.Kind != JsonNodeKind.Array)
        {
            throw new CborLdProcessingException(
                "invalid registry document",
                $"Expected a top-level JSON array of registry entries; got {root.Kind}.");
        }

        List<CborLdRegistryEntry> entries = [];
        int index = 0;
        foreach(JsonNode element in root.EnumerateArray())
        {
            try
            {
                entries.Add(LoadEntryFromNode(element));
            }
            catch(CborLdProcessingException ex)
            {
                throw new CborLdProcessingException(
                    ex.ErrorCode ?? "invalid registry entry",
                    string.Create(CultureInfo.InvariantCulture, $"Failed to load registry entry at array index {index}: {ex.Message}"),
                    ex);
            }

            index++;
        }

        return entries;
    }

    private static JsonNode ParseRoot(Utf8String utf8Json, ParseJsonDelegate parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        try
        {
            return parse(utf8Json);
        }
        catch(Exception ex) when(ex is not CborLdProcessingException)
        {
            throw new CborLdProcessingException(
                "invalid registry json",
                $"Failed to parse registry JSON: {ex.Message}",
                ex);
        }
    }

    private static CborLdRegistryEntry LoadEntryFromNode(JsonNode entry)
    {
        if(entry.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Registry entry must be a JSON object; got {entry.Kind}.");
        }

        int id = RequiredInt(entry, "id");
        Dictionary<string, CborLdKeywordCodec> keywords = LoadKeywords(entry);
        Dictionary<string, CborLdTermCodec> terms = LoadTerms(entry);
        Dictionary<string, CborLdTypeTableSource> typeTables = LoadTypeTables(entry);
        string processingModel = OptionalString(entry, "processingModel") ?? "default";
        bool provisional = OptionalBoolean(entry, "provisional") ?? false;

        return new CborLdRegistryEntry(
            registryEntryId: id,
            keywords: keywords,
            terms: terms,
            processingModel: processingModel,
            provisional: provisional,
            typeTables: typeTables.Count > 0 ? typeTables : null);
    }

    private static Dictionary<string, CborLdKeywordCodec> LoadKeywords(JsonNode entry)
    {
        Dictionary<string, CborLdKeywordCodec> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("keywords", out JsonNode keywords))
        {
            return result;
        }

        if(keywords.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'keywords' must be a JSON object; got {keywords.Kind}.");
        }

        foreach(KeyValuePair<string, JsonNode> property in keywords.EnumerateObject())
        {
            if(!TryGetInt32(property.Value, out int codecId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Keyword codec for '{property.Key}' must be an integer; got {property.Value.Kind}.");
            }

            result[property.Key] = new CborLdKeywordCodec(property.Key, codecId);
        }

        return result;
    }

    private static Dictionary<string, CborLdTermCodec> LoadTerms(JsonNode entry)
    {
        Dictionary<string, CborLdTermCodec> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("terms", out JsonNode terms))
        {
            return result;
        }

        if(terms.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'terms' must be a JSON object; got {terms.Kind}.");
        }

        foreach(KeyValuePair<string, JsonNode> property in terms.EnumerateObject())
        {
            result[property.Key] = LoadTermCodec(property.Key, property.Value);
        }

        return result;
    }

    private static CborLdTermCodec LoadTermCodec(string termName, JsonNode value)
    {
        //Compact form: "termName": <int>
        if(value.Kind == JsonNodeKind.Number)
        {
            if(!TryGetInt32(value, out int codecId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Term codec id for '{termName}' must be a 32-bit integer.");
            }

            return new CborLdTermCodec(termName, codecId);
        }

        //Full form: "termName": { "id": <int>, "type": <string|null> }
        if(value.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Term codec for '{termName}' must be an integer or an object; got {value.Kind}.");
        }

        int id = RequiredInt(value, "id");
        string? type = OptionalString(value, "type");

        return new CborLdTermCodec(termName, id, type);
    }

    private static Dictionary<string, CborLdTypeTableSource> LoadTypeTables(JsonNode entry)
    {
        Dictionary<string, CborLdTypeTableSource> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("typeTables", out JsonNode typeTables))
        {
            return result;
        }

        if(typeTables.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'typeTables' must be a JSON object; got {typeTables.Kind}.");
        }

        foreach(KeyValuePair<string, JsonNode> property in typeTables.EnumerateObject())
        {
            result[property.Key] = LoadTypeTableSource(property.Key, property.Value);
        }

        return result;
    }

    private static CborLdTypeTableSource LoadTypeTableSource(string typeName, JsonNode value)
    {
        //Caller-provided sentinel per W3C CBOR-LD 1.0: the typeTables array
        //may contain the literal string "callerProvidedTable" to indicate
        //the table is supplied at encode/decode time.
        if(value.Kind == JsonNodeKind.String)
        {
            string text = value.GetString() ?? string.Empty;
            if(text == CborLdCallerProvidedTypeTableMarker.SentinelValue)
            {
                return CborLdTypeTableSource.CallerProvided();
            }

            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Type table '{typeName}' has unknown string sentinel '{text}'. The only spec-defined string sentinel is '{CborLdCallerProvidedTypeTableMarker.SentinelValue}'.");
        }

        //Registry-provided table: object mapping string keys to integer values.
        if(value.Kind != JsonNodeKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Type table '{typeName}' must be an object of string-to-integer mappings or the sentinel string '{CborLdCallerProvidedTypeTableMarker.SentinelValue}'; got {value.Kind}.");
        }

        Dictionary<string, int> mappings = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> mapping in value.EnumerateObject())
        {
            if(!TryGetInt32(mapping.Value, out int mappedId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Type table '{typeName}' value for key '{mapping.Key}' must be a 32-bit integer.");
            }

            mappings[mapping.Key] = mappedId;
        }

        return CborLdTypeTableSource.FromRegistry(mappings);
    }

    private static int RequiredInt(JsonNode parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonNode value))
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Required field '{fieldName}' is missing.");
        }

        if(!TryGetInt32(value, out int parsed))
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a 32-bit integer; got {value.Kind}.");
        }

        return parsed;
    }

    private static string? OptionalString(JsonNode parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonNode value))
        {
            return null;
        }

        return value.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.String => value.GetString(),
            _ => throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a string or null; got {value.Kind}.")
        };
    }

    private static bool? OptionalBoolean(JsonNode parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonNode value))
        {
            return null;
        }

        return value.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.True => true,
            JsonNodeKind.False => false,
            _ => throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a boolean or null; got {value.Kind}.")
        };
    }

    private static bool TryGetInt32(JsonNode value, out int parsed)
    {
        parsed = 0;
        if(value.Kind != JsonNodeKind.Number)
        {
            return false;
        }

        return int.TryParse(value.GetRawNumber(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed);
    }
}
