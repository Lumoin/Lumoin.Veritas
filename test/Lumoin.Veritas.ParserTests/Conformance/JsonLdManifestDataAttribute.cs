using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> that yields one row per entry of a vendored W3C JSON-LD API test
/// manifest, loaded from the source tree at discovery time. The JSON-LD analogue of
/// <see cref="W3cManifestDataAttribute"/> (the JSON-LD manifests are <c>.jsonld</c>, not Turtle).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class JsonLdManifestDataAttribute: Attribute, ITestDataSource
{
    /// <summary>Initialises the attribute for one operation's manifest.</summary>
    /// <param name="operation">The operation: <c>expand</c>, <c>compact</c>, or <c>toRdf</c>.</param>
    public JsonLdManifestDataAttribute(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Operation = operation;
    }

    /// <summary>Gets the operation whose manifest this attribute loads.</summary>
    public string Operation { get; }

    /// <summary>The <c>option.processingMode</c> / <c>option.specVersion</c> value selecting the superseded JSON-LD 1.0 processing mode.</summary>
    private const string JsonLd10 = "json-ld-1.0";

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        List<object[]> rows = [];
        foreach(JsonLdTestCase testCase in JsonLdManifestLoader.Load(Operation))
        {
            //JSON-LD 1.0 (2014) is superseded by 1.1 (2020); its cases exist
            //only to REJECT 1.1 features. This engine implements 1.1, for which
            //the conformant stance is that 1.0-mode cases are not applicable —
            //out of remit, not an unfinished gap — so they are filtered here
            //and never materialise as a row (no skip is reported for them).
            if(IsJsonLd10(testCase.ProcessingMode) || IsJsonLd10(testCase.SpecVersion))
            {
                continue;
            }

            rows.Add([testCase]);
        }

        return rows;
    }

    /// <summary>Whether a <c>processingMode</c> / <c>specVersion</c> option selects JSON-LD 1.0.</summary>
    /// <param name="value">The option value, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it names 1.0.</returns>
    private static bool IsJsonLd10(string? value)
    {
        return string.Equals(value, JsonLd10, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [JsonLdTestCase testCase, ..])
        {
            return $"{testCase.Operation} {testCase.Id} — {testCase.Name}";
        }

        return null;
    }
}
