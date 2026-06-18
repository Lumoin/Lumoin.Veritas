using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Resolves paths into the vendored W3C corpus and the hand-authored
/// harness fixtures, reading them directly from the source tree rather
/// than from a build-time copy in the output directory.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is large (hundreds of files) and read-only, so copying it
/// into every build output is wasted I/O and disk. Instead the harness
/// anchors on this source file's compile-time location via
/// <see cref="CallerFilePathAttribute"/> and walks to the sibling
/// <c>Material/</c> and <c>Conformance/Fixtures/</c> directories. The
/// conformance tests therefore run against the checked-out source tree;
/// they are development-time tests, not something shipped in a package.
/// </para>
/// </remarks>
internal static class W3cCorpusPath
{
    /// <summary>
    /// Resolves a path under <c>Material/&lt;libraryFolder&gt;/&lt;suiteFolder&gt;/&lt;fileName&gt;</c>
    /// in the source tree.
    /// </summary>
    /// <param name="libraryFolder">The library subfolder under Material (for example <c>NQuads</c> or <c>Turtle</c>).</param>
    /// <param name="suiteFolder">The suite subfolder under the library folder (for example <c>n-triples</c> or <c>turtle</c>).</param>
    /// <param name="fileName">The file name within the suite folder.</param>
    /// <returns>An absolute path within the checked-out source tree.</returns>
    public static string For(string libraryFolder, string suiteFolder, string fileName)
    {
        ArgumentNullException.ThrowIfNull(libraryFolder);
        ArgumentNullException.ThrowIfNull(suiteFolder);
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.Combine(TestProjectDirectory(), "Material", libraryFolder, suiteFolder, fileName);
    }

    /// <summary>
    /// Resolves a path under <c>Conformance/Fixtures/&lt;fileName&gt;</c>
    /// for hand-authored harness unit-test fixtures, read from the source tree.
    /// </summary>
    /// <param name="fileName">The fixture file name.</param>
    /// <returns>An absolute path within the checked-out source tree.</returns>
    public static string FixturePath(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.Combine(TestProjectDirectory(), "Conformance", "Fixtures", fileName);
    }

    /// <summary>
    /// Resolves the <c>Material/&lt;libraryFolder&gt;</c> directory — for corpora (such as the JSON-LD suite)
    /// whose manifests and fixtures sit directly under the library folder rather than in a per-suite subfolder.
    /// </summary>
    /// <param name="libraryFolder">The library subfolder under Material (for example <c>JsonLd</c>).</param>
    /// <returns>An absolute directory path within the checked-out source tree.</returns>
    public static string LibraryDirectory(string libraryFolder)
    {
        ArgumentNullException.ThrowIfNull(libraryFolder);

        return Path.Combine(TestProjectDirectory(), "Material", libraryFolder);
    }

    private static string TestProjectDirectory()
    {
        //ThisSourceFile resolves to .../Lumoin.Veritas.ParserTests/Conformance/W3cCorpusPath.cs at compile
        //time. A deterministic (ContinuousIntegrationBuild) compilation path-maps it to the
        //non-existent /_/ root, so when the directory it implies is absent the project directory is
        //resolved from the runtime output directory
        //(.../Lumoin.Veritas.ParserTests/bin/<configuration>/<tfm>/) instead.
        string conformanceDirectory = Path.GetDirectoryName(ThisSourceFile())!;
        string fromSource = Path.GetDirectoryName(conformanceDirectory)!;
        if(Directory.Exists(Path.Combine(fromSource, "Material")))
        {
            return fromSource;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
    }

    private static string ThisSourceFile([CallerFilePath] string path = "")
    {
        return path;
    }
}
