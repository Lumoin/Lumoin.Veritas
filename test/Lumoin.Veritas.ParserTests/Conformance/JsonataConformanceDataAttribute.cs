using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> that yields one row per case of the vendored JSONata test suite,
/// loaded from the source tree at discovery time. The display name carries the group so the run output can
/// be triaged per group.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class JsonataConformanceDataAttribute: Attribute, ITestDataSource
{
    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        List<object[]> rows = [];
        foreach(JsonataConformanceCase testCase in JsonataConformanceLoader.LoadRequired())
        {
            rows.Add([testCase]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [JsonataConformanceCase testCase, ..])
        {
            return $"jsonata {testCase.GroupName} :: {testCase.CaseFile}";
        }

        return null;
    }
}
