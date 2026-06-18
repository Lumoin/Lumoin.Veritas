using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> that yields one row per test case
/// in a vendored W3C OWL 2 test-ontology manifest, loaded from the source
/// tree at discovery time.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class Owl2ManifestDataAttribute: Attribute, ITestDataSource
{
    /// <summary>
    /// Initialises the attribute with the corpus location of the manifest.
    /// </summary>
    /// <param name="suiteFolder">The status arm under <c>Material/Owl2</c> (<c>approved</c> or <c>proposed</c>).</param>
    /// <param name="fileName">The manifest file name (for example <c>all.rdf</c>).</param>
    public Owl2ManifestDataAttribute(string suiteFolder, string fileName)
    {
        ArgumentNullException.ThrowIfNull(suiteFolder);
        ArgumentNullException.ThrowIfNull(fileName);

        SuiteFolder = suiteFolder;
        FileName = fileName;
    }

    /// <summary>Gets the status arm under <c>Material/Owl2</c>.</summary>
    public string SuiteFolder { get; }

    /// <summary>Gets the manifest file name.</summary>
    public string FileName { get; }

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", SuiteFolder, FileName));

        List<object[]> rows = new(tests.Length);
        foreach(Owl2TestCase test in tests)
        {
            rows.Add([test]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [Owl2TestCase testCase, ..])
        {
            return testCase.Identifier;
        }

        return null;
    }
}
