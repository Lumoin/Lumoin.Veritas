using System;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Validation;

namespace Lumoin.Veritas.Studio.Wasm;

/// <summary>
/// Projects a SHACL <see cref="ValidationReport"/> into the compact JSON the Studio result view renders:
/// a conformance flag and one row per result (focus node, severity, constraint component, message). This
/// is a UI projection, not the canonical <c>sh:ValidationReport</c> RDF graph (which <see cref="ValidationReportSerializer"/>
/// produces) — the app emits the flat shape its table consumes, the same way it picks its own wire format.
/// </summary>
internal static class ShaclReportJson
{
    /// <summary>
    /// Serializes a validation report as the UI's report JSON.
    /// </summary>
    /// <param name="report">The validation report.</param>
    /// <param name="dictionary">The dictionary that resolves the results' focus-node term ids back to terms.</param>
    /// <returns>The report JSON (a conformance flag and the result rows).</returns>
    public static string From(ValidationReport report, TermDictionary dictionary)
    {
        StringBuilder json = new();
        json.Append("{\"conforms\":").Append(report.Conforms ? "true" : "false").Append(",\"results\":[");
        bool first = true;
        foreach(ValidationResult result in report.Results)
        {
            if(!first)
            {
                json.Append(',');
            }

            first = false;
            json.Append("{\"focusNode\":").Append(JsonString(LabelOf(dictionary.Resolve(result.FocusNode))))
                .Append(",\"severity\":").Append(JsonString(LocalName(result.Severity.Iri.ToString())))
                .Append(",\"constraint\":").Append(JsonString(LocalName(result.SourceConstraintComponent.ToString())))
                .Append(",\"message\":").Append(JsonString(MessageOf(result))).Append('}');
        }

        json.Append("]}");

        return json.ToString();
    }

    /// <summary>The first human-readable message attached to a result, or an empty string when none.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The message, or an empty string.</returns>
    private static string MessageOf(ValidationResult result)
    {
        foreach(string value in result.Messages.Values)
        {
            return value;
        }

        return string.Empty;
    }

    /// <summary>A short display label for a term: an IRI's local name, a literal's value, or a blank-node label.</summary>
    /// <param name="term">The term.</param>
    /// <returns>The display label.</returns>
    private static string LabelOf(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => LocalName(named.Iri.ToString()),
            BlankNode blank => "_:" + blank.Label.ToString(),
            Literal literal => literal.Value.ToString(),
            _ => term.ToString() ?? string.Empty
        };
    }

    /// <summary>The local name of an IRI: the part after the last <c>#</c> or <c>/</c>, else the whole IRI.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The local name.</returns>
    private static string LocalName(string iri)
    {
        int cut = Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/'));

        return cut >= 0 && cut < iri.Length - 1 ? iri[(cut + 1)..] : iri;
    }

    /// <summary>A JSON string literal: the value escaped per RFC 8259 and double-quoted.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The quoted, escaped JSON string.</returns>
    private static string JsonString(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach(char character in value)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when character < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }

        builder.Append('"');

        return builder.ToString();
    }
}
