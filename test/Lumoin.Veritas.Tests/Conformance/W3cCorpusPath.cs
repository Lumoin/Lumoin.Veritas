using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Tests.Conformance;

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

    private static string TestProjectDirectory()
    {
        //ThisSourceFile resolves to .../Lumoin.Veritas.Tests/Conformance/W3cCorpusPath.cs.
        //The test project directory is two levels up.
        string conformanceDirectory = Path.GetDirectoryName(ThisSourceFile())!;
        return Path.GetDirectoryName(conformanceDirectory)!;
    }

    private static string ThisSourceFile([CallerFilePath] string path = "")
    {
        return path;
    }
}
