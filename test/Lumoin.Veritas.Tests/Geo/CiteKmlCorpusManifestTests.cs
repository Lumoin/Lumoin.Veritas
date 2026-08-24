using System.Security.Cryptography;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Integrity gate for the CITE KML corpus: every file listed in <c>MANIFEST.sha256</c> is present
/// with a matching SHA-256 and no unlisted corpus file exists. The corpus is frozen — copied
/// verbatim from the pinned upstream commits recorded in
/// <c>Geo/CiteKmlCorpus/PROVENANCE.md</c>, never regenerated here — and the clearance
/// outcomes in <see cref="CiteKmlCorpusExpectations"/> rest on these exact bytes.
/// </summary>
[TestClass]
internal sealed class CiteKmlCorpusManifestTests
{
    /// <summary>The number of artifacts the manifest pins; the vendored universe never drifts silently.</summary>
    private const int PinnedArtifactCount = 209;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task EveryManifestEntryExistsWithMatchingSha256()
    {
        var manifest = await ReadManifestAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(PinnedArtifactCount, manifest);

        foreach((string relativePath, string expectedSha256) in manifest)
        {
            string path = CiteKmlCorpusPaths.GetPath(relativePath);
            Assert.IsTrue(File.Exists(path), $"Corpus file listed in manifest is missing on disk: {relativePath}.");

            byte[] content = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
            Assert.AreEqual(expectedSha256, actualSha256, $"Corpus content drifted from the pinned snapshot: {relativePath}.");
        }
    }

    [TestMethod]
    public async Task NoCorpusFileExistsOutsideTheManifest()
    {
        var manifest = await ReadManifestAsync(TestContext.CancellationToken).ConfigureAwait(false);

        var unlisted = new List<string>();
        foreach(string path in Directory.EnumerateFiles(CiteKmlCorpusPaths.CorpusRoot, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path);
            if(extension is not (".xml" or ".kml" or ".kmz"))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(CiteKmlCorpusPaths.CorpusRoot, path).Replace(Path.DirectorySeparatorChar, '/');
            if(!manifest.ContainsKey(relativePath))
            {
                unlisted.Add(relativePath);
            }
        }

        Assert.HasCount(0, unlisted, $"Corpus files not pinned by the manifest: {string.Join(", ", unlisted)}.");
    }

    /// <summary>
    /// Parses <c>MANIFEST.sha256</c> (lines of <c>&lt;sha256&gt;␠␠&lt;corpus-relative path&gt;</c>) into a
    /// path → hash dictionary.
    /// </summary>
    private static async Task<Dictionary<string, string>> ReadManifestAsync(CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(CiteKmlCorpusPaths.CorpusRoot, "MANIFEST.sha256");
        Assert.IsTrue(File.Exists(manifestPath), "MANIFEST.sha256 was not found under the corpus root.");

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(string line in await File.ReadAllLinesAsync(manifestPath, cancellationToken).ConfigureAwait(false))
        {
            if(string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separatorIndex = line.IndexOf("  ", StringComparison.Ordinal);
            Assert.IsGreaterThan(0, separatorIndex, $"Malformed manifest line: '{line}'.");
            manifest.Add(line[(separatorIndex + 2)..], line[..separatorIndex]);
        }

        return manifest;
    }
}
