using System.IO;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// Integration tests for the workbench
/// <c>--profile-edgemap-distribution</c> scenario. Materialises a
/// tiny NQuads corpus to a temp file, launches the workbench as a
/// child process, asserts on the histogram output.
/// </summary>
[TestClass]
internal sealed class EdgeMapDistributionCliIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CliEdgeMapDistributionOnSmallCorpusReturnsHistogram()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        //Materialise a tiny NQuads corpus in a temp file. Three
        //triples is enough to exercise the build path; the survey
        //then reports tier counts and (for a corpus this small) a
        //SortedArray count of zero.
        string corpusPath = Path.Combine(Path.GetTempPath(), $"workbench-edgemap-{Path.GetRandomFileName()}.nq");
        string corpus =
            "<http://example.org/a> <http://example.org/p> <http://example.org/b> .\n" +
            "<http://example.org/a> <http://example.org/p> <http://example.org/c> .\n" +
            "<http://example.org/d> <http://example.org/p> <http://example.org/e> .\n";
        await File.WriteAllTextAsync(corpusPath, corpus, TestContext.CancellationToken).ConfigureAwait(false);

        try
        {
            CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
                executablePath,
                ["--profile-edgemap-distribution", corpusPath],
                TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(0, result.ExitCode);
            Assert.Contains("[soak]", result.Stdout);
            Assert.Contains("loaded", result.Stdout);
            Assert.Contains("total edgemaps", result.Stdout);
            Assert.Contains("Inline", result.Stdout);
            Assert.Contains("SortedArray", result.Stdout);
        }
        finally
        {
            File.Delete(corpusPath);
        }
    }

    [TestMethod]
    public async Task CliEdgeMapDistributionWithMissingPathArgumentExitsNonZero()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--profile-edgemap-distribution"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("path argument", result.Stderr);
    }

    [TestMethod]
    public async Task CliEdgeMapDistributionWithMissingFileExitsNonZero()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        string missingPath = Path.Combine(Path.GetTempPath(), $"workbench-edgemap-missing-{Path.GetRandomFileName()}.nq");

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--profile-edgemap-distribution", missingPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("File not found", result.Stderr);
    }
}
