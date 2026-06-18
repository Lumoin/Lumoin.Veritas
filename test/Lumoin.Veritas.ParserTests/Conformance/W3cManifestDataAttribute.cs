using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> that yields one row per
/// W3C test declared in a vendored manifest, loading the manifest
/// from the source tree at discovery time.
/// </summary>
/// <remarks>
/// <para>
/// The conformance suites are manifest-driven rather than file-glob
/// driven: the manifest declares each test's type — positive or
/// negative syntax, evaluation, canonicalization — and pairs an
/// action file with its expected-result file. This attribute parses
/// the manifest into <see cref="W3cTestCase"/> rows so the consuming
/// test method dispatches on the type rather than guessing from file
/// extensions, while keeping the corpus read directly from the source
/// tree (no build-time copy).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class W3cManifestDataAttribute: Attribute, ITestDataSource
{
    /// <summary>
    /// Initialises the attribute with the corpus location of the manifest.
    /// </summary>
    /// <param name="libraryFolder">The library subfolder under Material (for example <c>NQuads</c> or <c>Turtle</c>).</param>
    /// <param name="suiteFolder">The suite subfolder under the library folder (for example <c>n-triples</c> or <c>turtle</c>).</param>
    public W3cManifestDataAttribute(string libraryFolder, string suiteFolder)
    {
        ArgumentNullException.ThrowIfNull(libraryFolder);
        ArgumentNullException.ThrowIfNull(suiteFolder);

        LibraryFolder = libraryFolder;
        SuiteFolder = suiteFolder;
    }

    /// <summary>Gets the library subfolder under Material.</summary>
    public string LibraryFolder { get; }

    /// <summary>Gets the suite subfolder under the library folder.</summary>
    public string SuiteFolder { get; }

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        string manifestPath = W3cCorpusPath.For(LibraryFolder, SuiteFolder, "manifest.ttl");
        W3cManifest manifest = W3cManifestLoader.Load(manifestPath);

        List<object[]> rows = new(manifest.Tests.Length);
        foreach(W3cTestCase testCase in manifest.Tests)
        {
            rows.Add([testCase]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        if(data is [W3cTestCase testCase, ..])
        {
            return $"{testCase.Name} ({testCase.Type})";
        }

        return null;
    }
}
