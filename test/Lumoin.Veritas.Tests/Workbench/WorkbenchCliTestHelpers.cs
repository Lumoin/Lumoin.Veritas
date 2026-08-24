using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// Shared test infrastructure for tests that exercise the workbench
/// executable as a child process. Resolves the built executable
/// path, launches the process with captured streams, and returns a
/// <see cref="CliResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each integration test resolves the
/// executable, passes string-array arguments (no shell quoting
/// quirks), and asserts on <see cref="CliResult.ExitCode"/> plus
/// the captured streams. Tests that cannot run because the
/// executable was not built call
/// <see cref="Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(string)"/>
/// rather than failing — the precondition isn't met so the test
/// cannot judge.
/// </para>
/// </remarks>
internal static class WorkbenchCliTestHelpers
{
    /// <summary>
    /// Locates the workbench executable produced by the most
    /// recent build. Searches both Debug and Release output
    /// directories under the workbench project's <c>bin</c> folder.
    /// Returns <c>null</c> if no executable is found in either
    /// configuration.
    /// </summary>
    /// <returns>The full path to the executable, or <c>null</c>.</returns>
    public static string? GetExecutablePath()
    {
        //Tests run from
        //test/Lumoin.Veritas.Tests/bin/<config>/<tfm>/, so the repo
        //root is five levels up. The workbench's apphost is at
        //test/Lumoin.Veritas.Workbench/bin/<config>/<tfm>/Lumoin.Veritas.Workbench{.exe?}.
        string basePath = AppContext.BaseDirectory;
        string repoRoot = Path.GetFullPath(Path.Combine(basePath, "../../../../.."));
        //The last segment of basePath is the target-framework folder
        //(e.g. "net10.0"). Trim the trailing separator so the last
        //segment resolves cleanly.
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
                "test", "Lumoin.Veritas.Workbench", "bin", configuration, targetFramework,
                $"Lumoin.Veritas.Workbench{extension}");
            if(File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the workbench executable with the supplied argument
    /// array and captures the exit code, standard output, and
    /// standard error. The arguments are passed via
    /// <see cref="ProcessStartInfo.ArgumentList"/> so no shell
    /// quoting applies; arguments containing spaces or special
    /// characters round-trip unchanged.
    /// </summary>
    /// <param name="executablePath">The path returned by <see cref="GetExecutablePath"/>.</param>
    /// <param name="arguments">The argument tokens to pass to the executable.</param>
    /// <param name="cancellationToken">Cancellation token for the read operations and the wait-for-exit.</param>
    /// <returns>The captured <see cref="CliResult"/>.</returns>
    public static async Task<CliResult> RunCliAsync(
        string executablePath,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach(string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CliResult(process.ExitCode, stdout, stderr);
    }
}
