using System.Threading.Tasks;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// Integration tests that exercise the workbench executable as a
/// child process. Each test resolves the built executable via
/// <see cref="WorkbenchCliTestHelpers.GetExecutablePath"/>, launches
/// it with captured streams, and asserts on the exit code and
/// captured output. Tests fall back to
/// <see cref="Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(string)"/>
/// when the executable is missing rather than failing.
/// </summary>
[TestClass]
internal sealed class HypertrieSoakCliIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CliProfileBuildWithShortDurationAndSmallCorpusReturnsSuccessExitCode()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--profile-build", "--duration", "1", "--triple-count", "1000"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("[soak]", result.Stdout);
        Assert.Contains("build iterations", result.Stdout);
    }

    [TestMethod]
    public async Task CliProfileQueryWithShortDurationAndSmallCorpusReturnsSuccessExitCode()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--profile-query", "--duration", "1", "--triple-count", "1000"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("[soak]", result.Stdout);
        Assert.Contains("query iterations", result.Stdout);
    }

    [TestMethod]
    public async Task CliHelpReturnsSuccessExitCodeAndPrintsUsage()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--help"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Contains("--profile-build", result.Stdout);
        Assert.Contains("--profile-query", result.Stdout);
    }

    [TestMethod]
    public async Task CliUnknownCommandReturnsNonZeroExitCodeAndWritesToStderr()
    {
        string? executablePath = WorkbenchCliTestHelpers.GetExecutablePath();
        if(executablePath is null)
        {
            Assert.Inconclusive("Workbench executable not found. Build the workbench project first.");

            return;
        }

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executablePath,
            ["--no-such-command"],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("Unknown command", result.Stderr);
    }
}
