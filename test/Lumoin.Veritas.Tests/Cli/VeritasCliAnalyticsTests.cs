using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Workbench;

namespace Lumoin.Veritas.Tests.Cli;

/// <summary>
/// Drives the built <c>Lumoin.Veritas.Cli</c> executable's <c>analytics</c> command as a child process: listing the
/// catalog, a scalar metric (triangle count), and a tuple metric (cliques) over a known triangle, so the shipped
/// artifact's argument parsing, the analytics operation, the catalog, and the result rendering are exercised
/// end-to-end.
/// </summary>
internal sealed partial class VeritasCliIntegrationTests
{
    /// <summary>The <c>analytics --list</c> command prints the registered algorithms.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsCommandListsAlgorithms()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--list"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("triangle-count", result.Stdout);
        Assert.Contains("pagerank", result.Stdout);
        Assert.Contains("cliques", result.Stdout);
    }

    /// <summary>The <c>analytics --algo triangle-count</c> command counts the one triangle in the fixture.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsCommandCountsTriangles()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        string dataPath = WriteTriangleFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--algo", "triangle-count", "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("count", result.Stdout);
        Assert.Contains("1", result.Stdout);
    }

    /// <summary>The <c>analytics --algo cliques --param size=3</c> command enumerates the triangle as a three-clique.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsCommandEnumeratesCliques()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        string dataPath = WriteTriangleFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--algo", "cliques", "--param", "size=3", "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/a", result.Stdout);
        Assert.Contains("http://example.org/b", result.Stdout);
        Assert.Contains("http://example.org/c", result.Stdout);
    }

    /// <summary>An unknown algorithm name is reported as an error with a non-zero exit code.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsCommandRejectsUnknownAlgorithm()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        string dataPath = WriteTriangleFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--algo", "no-such-algorithm", "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("Unknown analytics algorithm", result.Stderr);
    }

    /// <summary>The <c>--param predicates=…</c> option restricts the edge relation, changing the triangle count.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task AnalyticsCommandFiltersByPredicate()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        string dataPath = WriteTwoPredicateFixture();

        CliResult all = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--algo", "triangle-count", "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, all.ExitCode, $"stderr: {all.Stderr}");
        Assert.Contains("2", all.Stdout);

        CliResult filtered = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["analytics", "--algo", "triangle-count", "--param", "predicates=http://example.org/knows", "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, filtered.ExitCode, $"stderr: {filtered.Stderr}");
        Assert.Contains("1", filtered.Stdout);
    }

    /// <summary>Writes a single-triangle Turtle dataset (nodes a, b, c pairwise connected) to a fresh temporary directory.</summary>
    /// <returns>The data file path.</returns>
    private static string WriteTriangleFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-analytics-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "triangle.ttl");
        File.WriteAllText(dataPath, "@prefix : <http://example.org/> .\n:a :knows :b .\n:b :knows :c .\n:a :knows :c .\n");

        return dataPath;
    }

    /// <summary>Writes a two-predicate Turtle dataset: a <c>:knows</c> triangle {a,b,c} plus <c>:likes</c> edges that close a second triangle {a,b,d} only when both predicates count as edges. So all predicates give two triangles, <c>:knows</c> alone gives one.</summary>
    /// <returns>The data file path.</returns>
    private static string WriteTwoPredicateFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-analytics-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "two-predicates.ttl");
        File.WriteAllText(
            dataPath,
            "@prefix : <http://example.org/> .\n:a :knows :b .\n:b :knows :c .\n:a :knows :c .\n:a :likes :d .\n:b :likes :d .\n");

        return dataPath;
    }
}
