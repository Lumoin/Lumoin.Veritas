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

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        List<object[]> rows = [];
        foreach(JsonLdTestCase testCase in JsonLdManifestLoader.Load(Operation))
        {
            rows.Add([testCase]);
        }

        return rows;
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
