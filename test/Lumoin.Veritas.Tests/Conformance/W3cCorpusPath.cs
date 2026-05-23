using System;
using System.IO;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Resolves paths into the vendored W3C corpus that
/// MSBuild copies under
/// <see cref="AppContext.BaseDirectory"/>/<c>Material/</c> during build.
/// </summary>
/// <remarks>
/// <para>
/// The harness uses this helper rather than a hard-coded path
/// so the corpus location stays in one place and so unit tests
/// can locate hand-authored fixtures the same way they locate
/// vendored material.
/// </para>
/// </remarks>
internal static class W3cCorpusPath
{
    /// <summary>
    /// Resolves a path under <c>Material/&lt;libraryFolder&gt;/&lt;suiteFolder&gt;/&lt;fileName&gt;</c>.
    /// </summary>
    /// <param name="libraryFolder">The library subfolder under Material (for example <c>NQuads</c> or <c>Turtle</c>).</param>
    /// <param name="suiteFolder">The suite subfolder under the library folder (for example <c>n-triples</c> or <c>turtle</c>).</param>
    /// <param name="fileName">The file name within the suite folder.</param>
    /// <returns>An absolute path under the test binary's base directory.</returns>
    public static string For(string libraryFolder, string suiteFolder, string fileName)
    {
        ArgumentNullException.ThrowIfNull(libraryFolder);
        ArgumentNullException.ThrowIfNull(suiteFolder);
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.Combine(AppContext.BaseDirectory, "Material", libraryFolder, suiteFolder, fileName);
    }

    /// <summary>
    /// Resolves a path under <c>Conformance/Fixtures/&lt;fileName&gt;</c>
    /// for hand-authored harness unit-test fixtures.
    /// </summary>
    /// <param name="fileName">The fixture file name.</param>
    /// <returns>An absolute path under the test binary's base directory.</returns>
    public static string FixturePath(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.Combine(AppContext.BaseDirectory, "Conformance", "Fixtures", fileName);
    }
}
