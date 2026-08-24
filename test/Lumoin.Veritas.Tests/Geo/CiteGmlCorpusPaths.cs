namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Locates the vendored CITE GML corpus in the source tree. The corpus layout and its
/// upstream pins are recorded in <c>Geo/CiteGmlCorpus/PROVENANCE.md</c>; byte integrity is
/// asserted by <see cref="CiteGmlCorpusManifestTests"/>.
/// </summary>
internal static class CiteGmlCorpusPaths
{
    /// <summary>The corpus root in the checked-out source tree.</summary>
    public static string CorpusRoot { get; } = TestPaths.Fixture("Geo/CiteGmlCorpus");

    /// <summary>
    /// Resolves a corpus-relative path, forward-slash separated as the manifest spells it, to an
    /// absolute path in the source tree.
    /// </summary>
    public static string GetPath(string relativePath)
    {
        return Path.Combine(CorpusRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
