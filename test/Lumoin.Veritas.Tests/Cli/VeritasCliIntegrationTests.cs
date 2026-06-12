using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Workbench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Cli;

/// <summary>
/// Drives the built <c>Lumoin.Veritas.Cli</c> executable as a child process: the <c>query</c>
/// command over captured stdout, and the <c>serve</c> command's real Kestrel SPARQL endpoint over
/// an <see cref="HttpClient"/>. The HTTP test launches the actual executable with <c>--port 0</c>,
/// reads the bound address the server prints, and queries it — so the shipped artifact (process,
/// argument parsing, and Kestrel) is exercised end-to-end, not an in-process stand-in.
/// </summary>
[TestClass]
internal sealed partial class VeritasCliIntegrationTests
{
    /// <summary>The compiled pattern matching the SPARQL endpoint URL the <c>serve</c> command prints (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The endpoint-address regex.</returns>
    [GeneratedRegex(@"(https?://[^\s]+/sparql)")]
    private static partial Regex ListeningEndpointRegex();

    /// <summary>The compiled pattern matching the worker-thread floor line the <c>serve</c> command prints (source-generated, so no per-call regex compilation).</summary>
    /// <returns>The worker-thread floor regex.</returns>
    [GeneratedRegex(@"Worker-thread minimum floored at (\d+)\.")]
    private static partial Regex WorkerThreadFloorRegex();

    /// <summary>Gets or sets the test execution context (supplies the cancellation token).</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The <c>query</c> command evaluates a SELECT against a Turtle file and prints the CSV rows.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task QueryCommandSelectsKnownObjects()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, string queryPath) = WriteFixture();

        CliResult result = await WorkbenchCliTestHelpers.RunCliAsync(
            executable,
            ["query", queryPath, "--data", dataPath],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, $"stderr: {result.Stderr}");
        Assert.Contains("http://example.org/bob", result.Stdout);
        Assert.Contains("http://example.org/carol", result.Stdout);
    }

    /// <summary>The <c>serve</c> command's Kestrel endpoint answers a SPARQL Protocol GET over HTTP.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandAnswersHttpQuery()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        _ = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            Uri? endpoint = await ReadListeningEndpointAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(endpoint, "The server did not report a listening address.");

            using HttpClient client = new();
            Uri requestUri = new(endpoint, "?query=" + Uri.EscapeDataString("PREFIX : <http://example.org/> SELECT ?who WHERE { :alice :knows ?who }"));
            string body = await client.GetStringAsync(requestUri, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.Contains("http://example.org/bob", body);
            Assert.Contains("http://example.org/carol", body);
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>serve</c> command floors the worker-thread pool minimum at four times the processor count at startup.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task ServeCommandFloorsWorkerThreadPoolAtStartup()
    {
        string? executable = GetCliExecutablePath();
        if(executable is null)
        {
            Assert.Inconclusive("The Lumoin.Veritas.Cli executable was not built; build it to run this test.");

            return;
        }

        (string dataPath, _) = WriteFixture();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("serve");
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("--data");
        process.StartInfo.ArgumentList.Add(dataPath);

        process.Start();
        _ = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);

        try
        {
            int? floor = await ReadWorkerThreadFloorAsync(process, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsNotNull(floor, "The server did not report a worker-thread floor.");
            Assert.IsTrue(
                floor >= Environment.ProcessorCount * 4,
                $"Worker-thread floor {floor} is below four times the processor count ({Environment.ProcessorCount * 4}).");
        }
        finally
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the child's standard output until it prints the SPARQL endpoint address (or the timeout elapses).</summary>
    /// <param name="process">The running server process.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>The endpoint URI, or <see langword="null"/> if it was not reported in time.</returns>
    private static async Task<Uri?> ReadListeningEndpointAsync(Process process, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while(await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                Match match = ListeningEndpointRegex().Match(line);
                if(match.Success)
                {
                    return new Uri(match.Groups[1].Value);
                }
            }
        }
        catch(OperationCanceledException)
        {
            //Fall through to the null return: the address line never arrived.
        }

        return null;
    }

    /// <summary>Reads the child's standard output until it prints the worker-thread floor (or the timeout elapses).</summary>
    /// <param name="process">The running server process.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>The reported worker-thread floor, or <see langword="null"/> if it was not reported in time.</returns>
    private static async Task<int?> ReadWorkerThreadFloorAsync(Process process, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while(await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                Match match = WorkerThreadFloorRegex().Match(line);
                if(match.Success)
                {
                    return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }
        }
        catch(OperationCanceledException)
        {
            //Fall through to the null return: the floor line never arrived.
        }

        return null;
    }

    /// <summary>Writes a small Turtle dataset and a SELECT query to a fresh temporary directory.</summary>
    /// <returns>The data file path and the query file path.</returns>
    private static (string DataPath, string QueryPath) WriteFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        string dataPath = Path.Combine(directory, "data.ttl");
        File.WriteAllText(dataPath, "@prefix : <http://example.org/> .\n:alice :knows :bob .\n:alice :knows :carol .\n");

        string queryPath = Path.Combine(directory, "query.rq");
        File.WriteAllText(queryPath, "PREFIX : <http://example.org/>\nSELECT ?who WHERE { :alice :knows ?who } ORDER BY ?who\n");

        return (dataPath, queryPath);
    }

    /// <summary>Locates the built CLI executable under <c>src/Lumoin.Veritas.Cli/bin/&lt;config&gt;/&lt;tfm&gt;/</c>, or <see langword="null"/> when it has not been built.</summary>
    /// <returns>The executable path, or <see langword="null"/>.</returns>
    private static string? GetCliExecutablePath()
    {
        string basePath = AppContext.BaseDirectory;
        string repoRoot = Path.GetFullPath(Path.Combine(basePath, "../../../../.."));
        string targetFramework = Path.GetFileName(basePath.TrimEnd(Path.DirectorySeparatorChar));
        if(string.IsNullOrEmpty(targetFramework))
        {
            targetFramework = "net10.0";
        }

        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string[] configurations = ["Debug", "Release"];

        foreach(string configuration in configurations)
        {
            string candidate = Path.Combine(
                repoRoot,
                "src", "Lumoin.Veritas.Cli", "bin", configuration, targetFramework,
                $"Lumoin.Veritas.Cli{extension}");
            if(File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
