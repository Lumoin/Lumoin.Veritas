using System.Security.Cryptography;
using System.Text.Json;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Integrity gate for the A5 fixture corpus: every file listed in <c>MANIFEST.sha256</c> is present with a
    /// matching SHA-256, no unlisted fixture files exist, and the corpus is loadable JSON. The corpus is frozen —
    /// copied verbatim from the pinned snapshot recorded in <c>Dggs/Fixtures/PROVENANCE.md</c>, never
    /// regenerated here.
    /// </summary>
    [TestClass]
    internal sealed class A5FixtureManifestTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that every manifest-listed fixture file exists on disk with a matching SHA-256.</summary>
        [TestMethod]
        public async Task EveryManifestEntryExistsWithMatchingSha256()
        {
            var manifest = await ReadManifestAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(41, manifest);

            foreach((string relativePath, string expectedSha256) in manifest)
            {
                string path = TestPaths.Fixture("Geo/Dggs/Fixtures", relativePath);
                Assert.IsTrue(File.Exists(path), $"Fixture listed in manifest is missing on disk: {relativePath}.");

                byte[] content = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
                string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
                Assert.AreEqual(expectedSha256, actualSha256, $"Fixture content drifted from the pinned snapshot: {relativePath}.");
            }
        }

        /// <summary>Pins that no JSON fixture file on disk falls outside the manifest's listed paths.</summary>
        [TestMethod]
        public async Task NoFixtureFileExistsOutsideTheManifest()
        {
            var manifest = await ReadManifestAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var unlisted = new List<string>();
            foreach(string path in Directory.EnumerateFiles(TestPaths.Fixture("Geo/Dggs/Fixtures"), "*.json", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(TestPaths.Fixture("Geo/Dggs/Fixtures"), path).Replace(Path.DirectorySeparatorChar, '/');
                if(!manifest.ContainsKey(relativePath))
                {
                    unlisted.Add(relativePath);
                }
            }

            Assert.HasCount(0, unlisted, $"Fixture files not pinned by the manifest: {string.Join(", ", unlisted)}.");
        }

        /// <summary>Pins that <c>fixtures/serialization.json</c> parses with the expected resolution mask, test id, and resolution-30 location array lengths.</summary>
        [TestMethod]
        public async Task SerializationFixtureParsesWithExpectedShape()
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/serialization.json"));
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

            JsonElement root = document.RootElement;
            Assert.AreEqual(31, root.GetProperty("resolutionMasks").GetArrayLength());
            Assert.AreEqual(237, root.GetProperty("testIds").GetArrayLength());
            Assert.AreEqual(10, root.GetProperty("res30Locations").GetArrayLength());
        }

        /// <summary>
        /// Parses <c>MANIFEST.sha256</c> (lines of <c>&lt;sha256&gt;␠␠&lt;corpus-relative path&gt;</c>) into a
        /// path → hash dictionary.
        /// </summary>
        private static async Task<Dictionary<string, string>> ReadManifestAsync(CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(TestPaths.Fixture("Geo/Dggs/Fixtures"), "MANIFEST.sha256");
            Assert.IsTrue(File.Exists(manifestPath), "MANIFEST.sha256 was not copied to the test output directory.");

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
}
