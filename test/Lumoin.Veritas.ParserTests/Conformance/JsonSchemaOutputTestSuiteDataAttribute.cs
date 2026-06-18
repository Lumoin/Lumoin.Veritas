using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> yielding one row per case of the JSON Schema output-tests
/// suite (draft 2020-12), loaded from the source tree at discovery time.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class JsonSchemaOutputTestSuiteDataAttribute: Attribute, ITestDataSource
{
    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        List<object[]> rows = [];
        foreach(JsonSchemaOutputTestCase testCase in JsonSchemaOutputTestSuiteLoader.Load())
        {
            rows.Add([testCase]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [JsonSchemaOutputTestCase testCase, ..])
        {
            return $"jsonschema-output {testCase.File} :: {testCase.GroupDescription} :: {testCase.TestDescription} [{testCase.Format}]";
        }

        return null;
    }
}
