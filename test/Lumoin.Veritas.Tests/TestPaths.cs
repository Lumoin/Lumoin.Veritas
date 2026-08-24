using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Tests
{
    /// <summary>
    /// The one path-resolution seam for this test project: fixture corpora are read
    /// directly from the source tree, and temporary files land in one named
    /// subdirectory of the build output so the cleanup target is unambiguous and
    /// scanners can exempt a single directory.
    /// </summary>
    /// <remarks>
    /// Fixture corpora are large and read-only, so copying them into every build
    /// output is wasted I/O and disk. The resolver anchors on this source file's
    /// compile-time location and walks to the requested project-relative path. A
    /// deterministic (ContinuousIntegrationBuild) compilation path-maps the anchor to
    /// the non-existent <c>/_/</c> root, so when the directory it implies is absent
    /// the project directory is resolved from the runtime output directory instead.
    /// </remarks>
    internal static class TestPaths
    {
        /// <summary>The name of the temporary-file subdirectory under the build output.</summary>
        public const string TempDirectoryName = "TestTemp";

        /// <summary>The resolved test-project root directory.</summary>
        private static string ProjectRoot { get; } = ResolveProjectRoot();

        /// <summary>
        /// Resolves a fixture path from project-root-relative segments; segments may use
        /// forward slashes (e.g. <c>Fixture("Geo/Dggs/Fixtures", "fixtures/serialization.json")</c>).
        /// </summary>
        /// <param name="segments">Path segments relative to the test project root.</param>
        /// <returns>An absolute path within the checked-out source tree.</returns>
        public static string Fixture(params string[] segments)
        {
            ArgumentNullException.ThrowIfNull(segments);

            string path = ProjectRoot;
            foreach(string segment in segments)
            {
                path = Path.Combine(path, segment.Replace('/', Path.DirectorySeparatorChar));
            }

            return path;
        }

        /// <summary>
        /// Resolves (and creates) a named temporary directory under the build output's
        /// <see cref="TempDirectoryName"/> subdirectory.
        /// </summary>
        /// <param name="name">The directory name, typically the test class or scenario name.</param>
        /// <returns>An absolute path to an existing directory.</returns>
        public static string TempDirectory(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            string path = Path.Combine(AppContext.BaseDirectory, TempDirectoryName, name);
            Directory.CreateDirectory(path);

            return path;
        }

        /// <summary>
        /// Resolves a temporary file path inside <see cref="TempDirectory"/> for the given
        /// scenario, creating the directory but not the file.
        /// </summary>
        /// <param name="directoryName">The scenario directory name.</param>
        /// <param name="fileName">The file name within the scenario directory.</param>
        /// <returns>An absolute file path whose directory exists.</returns>
        public static string TempFile(string directoryName, string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            return Path.Combine(TempDirectory(directoryName), fileName);
        }

        /// <summary>
        /// Resolves the project root from this file's compile-time location, falling back to
        /// the runtime output directory when the source tree is absent.
        /// </summary>
        private static string ResolveProjectRoot()
        {
            string fromSource = Path.GetDirectoryName(ThisSourceFile())!;
            if(Directory.Exists(fromSource))
            {
                return fromSource;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
        }

        /// <summary>Answers this source file's compile-time path.</summary>
        private static string ThisSourceFile([CallerFilePath] string path = "")
        {
            return path;
        }
    }
}
