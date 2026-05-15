using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Json;

/// <summary>
/// Parses W3C CBOR-LD 1.0 registry JSON into <see cref="CborLdRegistryEntry"/>
/// instances. The loader owns the <see cref="System.Text.Json"/> dependency
/// on behalf of consumers; <see cref="CborLdRegistryEntry"/> and its
/// supporting types carry no JSON references.
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
    /// <returns>The parsed registry entry.</returns>
    /// <exception cref="CborLdProcessingException">The JSON is not a registry entry, or a required field is missing or malformed.</exception>
    public static CborLdRegistryEntry LoadEntry(Utf8String utf8Json)
    {
        JsonElement root = ParseRoot(utf8Json);
        try
        {
            return LoadEntryFromElement(root);
        }
        finally
        {
            //JsonElement is detached from any document after Clone; no
            //disposal is required. ParseRoot returns a cloned element.
        }
    }

    /// <summary>
    /// Parses a JSON document containing an array of registry entries.
    /// </summary>
    /// <param name="utf8Json">UTF-8 encoded JSON containing a top-level array of entries.</param>
    /// <returns>The parsed entries in document order.</returns>
    /// <exception cref="CborLdProcessingException">The JSON is not an array, or any entry is malformed.</exception>
    public static IReadOnlyList<CborLdRegistryEntry> LoadEntries(Utf8String utf8Json)
    {
        JsonElement root = ParseRoot(utf8Json);
        if(root.ValueKind != JsonValueKind.Array)
        {
            throw new CborLdProcessingException(
                "invalid registry document",
                $"Expected a top-level JSON array of registry entries; got {root.ValueKind}.");
        }

        List<CborLdRegistryEntry> entries = new(root.GetArrayLength());
        int index = 0;
        foreach(JsonElement element in root.EnumerateArray())
        {
            try
            {
                entries.Add(LoadEntryFromElement(element));
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

    private static JsonElement ParseRoot(Utf8String utf8Json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json.Memory);
            return document.RootElement.Clone();
        }
        catch(JsonException ex)
        {
            throw new CborLdProcessingException(
                "invalid registry json",
                $"Failed to parse registry JSON: {ex.Message}",
                ex);
        }
    }

    private static CborLdRegistryEntry LoadEntryFromElement(JsonElement entry)
    {
        if(entry.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Registry entry must be a JSON object; got {entry.ValueKind}.");
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

    private static Dictionary<string, CborLdKeywordCodec> LoadKeywords(JsonElement entry)
    {
        Dictionary<string, CborLdKeywordCodec> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("keywords", out JsonElement keywords))
        {
            return result;
        }
        if(keywords.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'keywords' must be a JSON object; got {keywords.ValueKind}.");
        }
        foreach(JsonProperty property in keywords.EnumerateObject())
        {
            if(!property.Value.TryGetInt32(out int codecId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Keyword codec for '{property.Name}' must be an integer; got {property.Value.ValueKind}.");
            }
            result[property.Name] = new CborLdKeywordCodec(property.Name, codecId);
        }
        return result;
    }

    private static Dictionary<string, CborLdTermCodec> LoadTerms(JsonElement entry)
    {
        Dictionary<string, CborLdTermCodec> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("terms", out JsonElement terms))
        {
            return result;
        }
        if(terms.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'terms' must be a JSON object; got {terms.ValueKind}.");
        }
        foreach(JsonProperty property in terms.EnumerateObject())
        {
            result[property.Name] = LoadTermCodec(property.Name, property.Value);
        }
        return result;
    }

    private static CborLdTermCodec LoadTermCodec(string termName, JsonElement value)
    {
        //Compact form: "termName": <int>
        if(value.ValueKind == JsonValueKind.Number)
        {
            if(!value.TryGetInt32(out int codecId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Term codec id for '{termName}' must be a 32-bit integer.");
            }
            return new CborLdTermCodec(termName, codecId);
        }

        //Full form: "termName": { "id": <int>, "type": <string|null> }
        if(value.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Term codec for '{termName}' must be an integer or an object; got {value.ValueKind}.");
        }
        int id = RequiredInt(value, "id");
        string? type = OptionalString(value, "type");
        return new CborLdTermCodec(termName, id, type);
    }

    private static Dictionary<string, CborLdTypeTableSource> LoadTypeTables(JsonElement entry)
    {
        Dictionary<string, CborLdTypeTableSource> result = new(StringComparer.Ordinal);
        if(!entry.TryGetProperty("typeTables", out JsonElement typeTables))
        {
            return result;
        }
        if(typeTables.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field 'typeTables' must be a JSON object; got {typeTables.ValueKind}.");
        }
        foreach(JsonProperty property in typeTables.EnumerateObject())
        {
            result[property.Name] = LoadTypeTableSource(property.Name, property.Value);
        }
        return result;
    }

    private static CborLdTypeTableSource LoadTypeTableSource(string typeName, JsonElement value)
    {
        //Caller-provided sentinel per W3C CBOR-LD 1.0: the typeTables array
        //may contain the literal string "callerProvidedTable" to indicate
        //the table is supplied at encode/decode time.
        if(value.ValueKind == JsonValueKind.String)
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
        if(value.ValueKind != JsonValueKind.Object)
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Type table '{typeName}' must be an object of string-to-integer mappings or the sentinel string '{CborLdCallerProvidedTypeTableMarker.SentinelValue}'; got {value.ValueKind}.");
        }
        Dictionary<string, int> mappings = new(StringComparer.Ordinal);
        foreach(JsonProperty mapping in value.EnumerateObject())
        {
            if(!mapping.Value.TryGetInt32(out int mappedId))
            {
                throw new CborLdProcessingException(
                    "invalid registry entry",
                    $"Type table '{typeName}' value for key '{mapping.Name}' must be a 32-bit integer.");
            }
            mappings[mapping.Name] = mappedId;
        }
        return CborLdTypeTableSource.FromRegistry(mappings);
    }

    private static int RequiredInt(JsonElement parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonElement value))
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Required field '{fieldName}' is missing.");
        }
        if(!value.TryGetInt32(out int parsed))
        {
            throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a 32-bit integer; got {value.ValueKind}.");
        }
        return parsed;
    }

    private static string? OptionalString(JsonElement parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonElement value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a string or null; got {value.ValueKind}.")
        };
    }

    private static bool? OptionalBoolean(JsonElement parent, string fieldName)
    {
        if(!parent.TryGetProperty(fieldName, out JsonElement value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new CborLdProcessingException(
                "invalid registry entry",
                $"Field '{fieldName}' must be a boolean or null; got {value.ValueKind}.")
        };
    }
}
