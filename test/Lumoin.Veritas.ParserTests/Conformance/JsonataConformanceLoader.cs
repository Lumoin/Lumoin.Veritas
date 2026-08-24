using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Loads the vendored JSONata test suite into <see cref="JsonataConformanceCase"/> rows. Each
/// <c>groups/&lt;group&gt;/case###.json</c> file is a case object (or, in a couple of files, an array of
/// case objects); the loader resolves an <c>expr-file</c> to its sibling text, a <c>dataset</c> name to its
/// document under <c>datasets/</c>, and the single expected-outcome field (<c>result</c> /
/// <c>undefinedResult</c> / <c>code</c> / <c>error</c>) into a closed expectation.
/// </summary>
internal static class JsonataConformanceLoader
{
    /// <summary>Loads every case under <c>Material/Jsonata/groups/*/*.json</c>, in group then file order.</summary>
    /// <returns>One case per case object (multi-case array files expand to one case per element).</returns>
    /// <remarks>
    /// Every <c>.json</c> file in a group directory is a case file: most are named <c>case###.json</c>, but
    /// several groups (for example <c>joins</c>, <c>parent-operator</c>, <c>function-distinct</c>) name their
    /// case files descriptively (<c>errors.json</c>, <c>parent.json</c>, <c>distinct.json</c>) and a few
    /// groups have no <c>case###.json</c> files at all, so globbing only <c>case*.json</c> would silently drop
    /// whole groups from the measure. The glob is therefore <c>*.json</c> over the group directory (datasets
    /// live in a separate sibling directory, and expression files use the <c>.jsonata</c> extension).
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The vendored corpus directory is absent.</exception>
    public static IReadOnlyList<JsonataConformanceCase> LoadRequired()
    {
        string corpusDirectory = W3cCorpusPath.LibraryDirectory("Jsonata");
        string groupsDirectory = Path.Combine(corpusDirectory, "groups");
        if(!Directory.Exists(groupsDirectory))
        {
            throw new DirectoryNotFoundException($"The vendored JSONata corpus is missing at '{groupsDirectory}'.");
        }

        string datasetsDirectory = Path.Combine(corpusDirectory, "datasets");
        List<JsonataConformanceCase> cases = [];

        string[] groupDirectories = Directory.GetDirectories(groupsDirectory);
        Array.Sort(groupDirectories, StringComparer.Ordinal);
        foreach(string groupDirectory in groupDirectories)
        {
            string groupName = Path.GetFileName(groupDirectory);
            string[] caseFiles = Directory.GetFiles(groupDirectory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(caseFiles, StringComparer.Ordinal);
            foreach(string caseFile in caseFiles)
            {
                LoadCaseFile(caseFile, groupName, groupDirectory, datasetsDirectory, cases);
            }
        }

        return cases;
    }

    /// <summary>Parses one case file and appends its case(s); an array-shaped file expands element-wise.</summary>
    /// <param name="path">The absolute case file path.</param>
    /// <param name="groupName">The owning group name.</param>
    /// <param name="groupDirectory">The owning group directory (for resolving an <c>expr-file</c>).</param>
    /// <param name="datasetsDirectory">The shared datasets directory (for resolving a <c>dataset</c> name).</param>
    /// <param name="cases">The list to populate.</param>
    private static void LoadCaseFile(string path, string groupName, string groupDirectory, string datasetsDirectory, List<JsonataConformanceCase> cases)
    {
        string caseFileName = Path.GetFileName(path);
        JsonNode root = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(path)));
        switch(root.Kind)
        {
            case JsonNodeKind.Object:
            {
                cases.Add(BuildCaseSafely(root, groupName, caseFileName, groupDirectory, datasetsDirectory));

                break;
            }

            case JsonNodeKind.Array:
            {
                int index = 0;
                foreach(JsonNode element in root.EnumerateArray())
                {
                    string label = $"{caseFileName}[{index}]";
                    cases.Add(BuildCaseSafely(element, groupName, label, groupDirectory, datasetsDirectory));
                    index++;
                }

                break;
            }

            default:
            {
                //A case file whose root is neither an object nor an array is unexpected; surface it as a
                //visible skipped row rather than dropping it silently from the measure.
                cases.Add(LoadErrorCase(groupName, caseFileName, "The case file root was neither an object nor an array."));

                break;
            }
        }
    }

    /// <summary>
    /// Builds one case, capturing any host-adapter resolution failure (for example a lone UTF-16 surrogate
    /// in an expression string the JSON adapter cannot materialise) as a load-error sentinel the runner
    /// skips, so one unreadable case cannot abort the whole discovery pass.
    /// </summary>
    /// <param name="caseObject">The case object node.</param>
    /// <param name="groupName">The owning group name.</param>
    /// <param name="caseFileLabel">The case file label.</param>
    /// <param name="groupDirectory">The owning group directory.</param>
    /// <param name="datasetsDirectory">The shared datasets directory.</param>
    /// <returns>The built case, or a load-error sentinel case.</returns>
    private static JsonataConformanceCase BuildCaseSafely(JsonNode caseObject, string groupName, string caseFileLabel, string groupDirectory, string datasetsDirectory)
    {
        try
        {
            return BuildCase(caseObject, groupName, caseFileLabel, groupDirectory, datasetsDirectory);
        }
        catch(InvalidOperationException exception)
        {
            return LoadErrorCase(groupName, caseFileLabel, exception.Message);
        }
        catch(IOException exception)
        {
            return LoadErrorCase(groupName, caseFileLabel, exception.Message);
        }
    }

    /// <summary>Builds a load-error sentinel case carrying the resolution failure message.</summary>
    /// <param name="groupName">The owning group name.</param>
    /// <param name="caseFileLabel">The case file label.</param>
    /// <param name="message">The resolution failure message.</param>
    /// <returns>The sentinel case.</returns>
    private static JsonataConformanceCase LoadErrorCase(string groupName, string caseFileLabel, string message)
    {
        return new JsonataConformanceCase(groupName, caseFileLabel, string.Empty, null, false, JsonataConformanceExpectation.Undefined(), false, message);
    }

    /// <summary>Builds one case from a case object, resolving the expression, input, bindings, and expected outcome.</summary>
    /// <param name="caseObject">The case object node.</param>
    /// <param name="groupName">The owning group name.</param>
    /// <param name="caseFileLabel">The case file label (possibly suffixed with an array index).</param>
    /// <param name="groupDirectory">The owning group directory.</param>
    /// <param name="datasetsDirectory">The shared datasets directory.</param>
    /// <returns>The built case.</returns>
    private static JsonataConformanceCase BuildCase(JsonNode caseObject, string groupName, string caseFileLabel, string groupDirectory, string datasetsDirectory)
    {
        string expression = ResolveExpression(caseObject, groupDirectory);
        JsonNode? input = ResolveInput(caseObject, datasetsDirectory);
        bool hasNonEmptyBindings = HasNonEmptyBindings(caseObject);
        JsonataConformanceExpectation expectation = ResolveExpectation(caseObject);
        bool unordered = caseObject.TryGetProperty("unordered", out JsonNode unorderedNode) && unorderedNode.Kind == JsonNodeKind.True;

        return new JsonataConformanceCase(groupName, caseFileLabel, expression, input, hasNonEmptyBindings, expectation, unordered, null);
    }

    /// <summary>Resolves the expression text from the inline <c>expr</c> field or the sibling <c>expr-file</c>.</summary>
    /// <param name="caseObject">The case object node.</param>
    /// <param name="groupDirectory">The owning group directory.</param>
    /// <returns>The expression source text.</returns>
    private static string ResolveExpression(JsonNode caseObject, string groupDirectory)
    {
        if(caseObject.TryGetProperty("expr", out JsonNode expr) && expr.Kind == JsonNodeKind.String)
        {
            return expr.GetString();
        }

        if(caseObject.TryGetProperty("expr-file", out JsonNode exprFile) && exprFile.Kind == JsonNodeKind.String)
        {
            string exprPath = Path.Combine(groupDirectory, exprFile.GetString());

            return File.ReadAllText(exprPath);
        }

        return string.Empty;
    }

    /// <summary>Resolves the input document: an inline <c>data</c> node, a named <c>dataset</c> document, or <see langword="null"/> for the no-input case.</summary>
    /// <param name="caseObject">The case object node.</param>
    /// <param name="datasetsDirectory">The shared datasets directory.</param>
    /// <returns>The resolved input node, or <see langword="null"/> for the <c>dataset: null</c> no-input case.</returns>
    private static JsonNode? ResolveInput(JsonNode caseObject, string datasetsDirectory)
    {
        if(caseObject.TryGetProperty("data", out JsonNode data))
        {
            return data;
        }

        if(caseObject.TryGetProperty("dataset", out JsonNode dataset) && dataset.Kind == JsonNodeKind.String)
        {
            string datasetPath = Path.Combine(datasetsDirectory, dataset.GetString() + ".json");

            return StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(datasetPath)));
        }

        //Either no input fields, or 'dataset: null': evaluate against the no-input (undefined) case.
        return null;
    }

    /// <summary>Determines whether the case supplies a non-empty external <c>bindings</c> map.</summary>
    /// <param name="caseObject">The case object node.</param>
    /// <returns><see langword="true"/> when a <c>bindings</c> object with at least one member is present.</returns>
    private static bool HasNonEmptyBindings(JsonNode caseObject)
    {
        if(!caseObject.TryGetProperty("bindings", out JsonNode bindings) || bindings.Kind != JsonNodeKind.Object)
        {
            return false;
        }

        foreach(KeyValuePair<string, JsonNode> _ in bindings.EnumerateObject())
        {
            return true;
        }

        return false;
    }

    /// <summary>Resolves the single expected-outcome field into a closed expectation.</summary>
    /// <param name="caseObject">The case object node.</param>
    /// <returns>The expectation: a result value, the undefined value, or an error code.</returns>
    /// <remarks>
    /// The suite uses four outcome shapes: <c>result</c> (a value), <c>undefinedResult</c> (the undefined
    /// value), <c>code</c> (an error code string), and <c>error</c> (an object carrying a <c>code</c> and an
    /// optional <c>token</c>). The latter two both express an expected error and resolve to the same
    /// <see cref="JsonataConformanceOutcomeKind.Error"/>.
    /// </remarks>
    private static JsonataConformanceExpectation ResolveExpectation(JsonNode caseObject)
    {
        if(caseObject.TryGetProperty("result", out JsonNode result))
        {
            return JsonataConformanceExpectation.Result(result);
        }

        if(caseObject.TryGetProperty("undefinedResult", out JsonNode undefined) && undefined.Kind == JsonNodeKind.True)
        {
            return JsonataConformanceExpectation.Undefined();
        }

        if(caseObject.TryGetProperty("code", out JsonNode code) && code.Kind == JsonNodeKind.String)
        {
            string? token = caseObject.TryGetProperty("token", out JsonNode tokenNode) && tokenNode.Kind == JsonNodeKind.String ? tokenNode.GetString() : null;

            return JsonataConformanceExpectation.Error(code.GetString(), token);
        }

        if(caseObject.TryGetProperty("error", out JsonNode error) && error.Kind == JsonNodeKind.Object)
        {
            return ResolveErrorObject(error);
        }

        //No recognised outcome field: treat as an undefined-result expectation so the runner classifies it
        //rather than throwing during the load.
        return JsonataConformanceExpectation.Undefined();
    }

    /// <summary>Resolves an <c>error</c> outcome object into an error expectation by its <c>code</c> and optional <c>token</c>.</summary>
    /// <param name="error">The <c>error</c> object node.</param>
    /// <returns>The error expectation.</returns>
    private static JsonataConformanceExpectation ResolveErrorObject(JsonNode error)
    {
        string code = error.TryGetProperty("code", out JsonNode errorCode) && errorCode.Kind == JsonNodeKind.String ? errorCode.GetString() : string.Empty;
        string? token = error.TryGetProperty("token", out JsonNode errorToken) && errorToken.Kind == JsonNodeKind.String ? errorToken.GetString() : null;

        return JsonataConformanceExpectation.Error(code, token);
    }
}
