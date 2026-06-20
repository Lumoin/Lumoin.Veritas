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
    /// Initialises the attribute with the corpus location of the manifest and
    /// the consuming arm's remit.
    /// </summary>
    /// <param name="suiteFolder">The status arm under <c>Material/Owl2</c> (<c>approved</c> or <c>proposed</c>).</param>
    /// <param name="fileName">The manifest file name (for example <c>all.rdf</c>).</param>
    /// <param name="remit">
    /// The arm's remit: cases outside it are filtered here and never materialise
    /// as rows, because their applicability is a property of the manifest
    /// metadata alone — they are another arm's job or out of the W3C remit, not
    /// a gap the arm could decide. Defaults to <see cref="Owl2TestRemit.All"/>.
    /// </param>
    public Owl2ManifestDataAttribute(string suiteFolder, string fileName, Owl2TestRemit remit = Owl2TestRemit.All)
    {
        ArgumentNullException.ThrowIfNull(suiteFolder);
        ArgumentNullException.ThrowIfNull(fileName);

        SuiteFolder = suiteFolder;
        FileName = fileName;
        Remit = remit;
    }

    /// <summary>Gets the status arm under <c>Material/Owl2</c>.</summary>
    public string SuiteFolder { get; }

    /// <summary>Gets the manifest file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the consuming arm's remit, applied as a row filter at discovery time.</summary>
    public Owl2TestRemit Remit { get; }

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", SuiteFolder, FileName));
        Owl2RemitPredicate inRemit = Owl2Remit.For(Remit);

        List<object[]> rows = [];
        foreach(Owl2TestCase test in tests)
        {
            if(inRemit(test))
            {
                rows.Add([test]);
            }
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
