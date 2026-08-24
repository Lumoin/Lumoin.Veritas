using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> that yields one row per entry of the house-authored
/// GeoSPARQL 1.1 requirement census, loaded from the source tree at discovery time — so every census
/// requirement id is a named row of the consuming arm, and a census change moves the row set visibly.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class GeoRequirementDataAttribute: Attribute, ITestDataSource
{
    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        ImmutableArray<GeoRequirementCase> cases = GeoRequirementManifest.Load();
        List<object[]> rows = new(cases.Length);
        foreach(GeoRequirementCase entry in cases)
        {
            rows.Add([entry]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [GeoRequirementCase entry, ..])
        {
            return entry.RequirementId;
        }

        return null;
    }
}
