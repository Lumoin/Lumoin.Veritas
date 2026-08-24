using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// One live replicate-command child process under test: started with captured standard streams, driven over
/// standard input one verb at a time, and awaited line-granularly with bounded waits. Dedicated reader tasks
/// drain standard output and standard error from process start, so a chatty child never fills a pipe buffer and
/// deadlocks a run-to-completion read against a live daemon; teardown KILLS the child on every exit path, and a
/// battery deletes the child's store directory strictly after <see cref="WaitForExitAsync"/> observed the exit,
/// because the child holds file handles until then.
/// </summary>
internal sealed class ReplicateProcess: IAsyncDisposable
{
    /// <summary>The line log's gate; the drains append under it and the waits scan under it.</summary>
    private readonly Lock gate = new();

    /// <summary>Every standard-output line the child has written, in arrival order; never truncated, so a wait can scan the whole history.</summary>
    private readonly List<string> outputLines = [];

    /// <summary>Every standard-error line the child has written, in arrival order.</summary>
    private readonly List<string> errorLines = [];

    /// <summary>The pulse a waiting scan awaits for the next appended line; replaced under the gate on every append.</summary>
    private TaskCompletionSource lineArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The child process.</summary>
    private Process Child { get; }

    /// <summary>The task draining standard output into the line log.</summary>
    private Task OutputDrain { get; }

    /// <summary>The task draining standard error into the line log.</summary>
    private Task ErrorDrain { get; }

    /// <summary>Launches the replicate command with the given arguments and starts both stream drains.</summary>
    /// <param name="executablePath">The command-line executable path.</param>
    /// <param name="arguments">The argument tokens, passed without shell quoting.</param>
    private ReplicateProcess(string executablePath, IReadOnlyList<string> arguments)
    {
        Child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach(string argument in arguments)
        {
            Child.StartInfo.ArgumentList.Add(argument);
        }

        Child.Start();
        Child.StandardInput.AutoFlush = true;
        OutputDrain = DrainAsync(Child.StandardOutput, outputLines);
        ErrorDrain = DrainAsync(Child.StandardError, errorLines);
    }

    /// <summary>Launches a replicate child over the built command-line executable.</summary>
    /// <param name="arguments">The argument tokens after the <c>replicate</c> command name; the command name is prepended here.</param>
    /// <returns>The running child.</returns>
    /// <exception cref="InvalidOperationException">The command-line executable has not been built.</exception>
    public static ReplicateProcess Start(params string[] arguments)
    {
        string executable = FindExecutable() ?? throw new InvalidOperationException("The command-line executable was not found under src/Lumoin.Veritas.Cli/bin; build the solution first.");
        string[] withCommand = new string[arguments.Length + 1];
        withCommand[0] = "replicate";
        arguments.CopyTo(withCommand, 1);

        return new ReplicateProcess(executable, withCommand);
    }

    /// <summary>
    /// Locates the command-line executable produced by the most recent build, searching both configurations
    /// under the CLI project's output folder — the same resolution shape the workbench process tests use.
    /// </summary>
    /// <returns>The full path, or <see langword="null"/> when no executable is found.</returns>
    public static string? FindExecutable()
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

    /// <summary>Writes one verb line to the child's standard input.</summary>
    /// <param name="verb">The verb line.</param>
    /// <returns>A task that completes when the line is flushed.</returns>
    public async Task SendAsync(string verb)
    {
        await Child.StandardInput.WriteLineAsync(verb).ConfigureAwait(false);
    }

    /// <summary>Sends one verb and waits for its reply: the first standard-output line AT OR AFTER the send point that starts with <paramref name="replyPrefix"/>. Lines the child writes concurrently (heal-round traces, interval pulls) are skipped by the prefix match, not consumed.</summary>
    /// <param name="verb">The verb line.</param>
    /// <param name="replyPrefix">The reply line's leading text.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The reply line.</returns>
    public async Task<string> SendAndWaitAsync(string verb, string replyPrefix, CancellationToken cancellationToken)
    {
        int from;
        lock(gate)
        {
            from = outputLines.Count;
        }

        await SendAsync(verb).ConfigureAwait(false);

        return await WaitForLineAsync(replyPrefix, from, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits for the first standard-output line ANYWHERE in the child's history that starts with <paramref name="prefix"/> — for lines the child emits on its own schedule, such as a heal round's publish marker.</summary>
    /// <param name="prefix">The line's leading text.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The matched line.</returns>
    public Task<string> WaitForAnyLineAsync(string prefix, CancellationToken cancellationToken)
    {
        return WaitForLineAsync(prefix, 0, cancellationToken);
    }

    /// <summary>Whether any standard-output line so far starts with <paramref name="prefix"/> — the non-waiting history check an absence assertion reads after the child exited.</summary>
    /// <param name="prefix">The line's leading text.</param>
    /// <returns>Whether such a line has arrived.</returns>
    public bool SawLine(string prefix)
    {
        lock(gate)
        {
            return FindLine(prefix, 0) is not null;
        }
    }

    /// <summary>Every standard-error line the child has written so far.</summary>
    /// <returns>A snapshot of the error lines.</returns>
    public IReadOnlyList<string> ErrorLines()
    {
        lock(gate)
        {
            return [.. errorLines];
        }
    }

    /// <summary>Waits for the child to exit — after a <c>quit</c> verb, so the store's file handles are released before a battery reads or corrupts the store directory.</summary>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The child's exit code.</returns>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await Child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(OutputDrain, ErrorDrain).ConfigureAwait(false);

        return Child.ExitCode;
    }

    /// <summary>
    /// Kills the child and joins its exit and both drains, LEAVING the process object alive so the line history
    /// stays readable and the later disposal still runs — the abrupt stop a row uses to take one replica of a
    /// cluster down while the rest keep serving.
    /// </summary>
    /// <returns>A task that completes when the child is gone and its streams are drained.</returns>
    public async Task KillAsync()
    {
        try
        {
            if(!Child.HasExited)
            {
                Child.Kill(entireProcessTree: true);
            }
        }
        catch(InvalidOperationException)
        {
            //The child exited between the check and the kill; the join below still completes.
        }

        await Child.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(OutputDrain, ErrorDrain).ConfigureAwait(false);
    }

    /// <summary>Kills the child if it is still alive, joins the exit and both drains, and releases the process — the every-exit-path teardown, safe after a graceful <see cref="WaitForExitAsync"/> or an explicit <see cref="KillAsync"/> too.</summary>
    /// <returns>A task that completes when the child is gone and its streams are drained.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if(!Child.HasExited)
            {
                Child.Kill(entireProcessTree: true);
            }
        }
        catch(InvalidOperationException)
        {
            //The child exited between the check and the kill; the join below still completes.
        }

        await Child.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(OutputDrain, ErrorDrain).ConfigureAwait(false);
        Child.Dispose();
    }

    /// <summary>Waits for the first output line at or after <paramref name="from"/> that starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The line's leading text.</param>
    /// <param name="from">The line index the scan starts at.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The matched line.</returns>
    private async Task<string> WaitForLineAsync(string prefix, int from, CancellationToken cancellationToken)
    {
        while(true)
        {
            Task arrived;
            lock(gate)
            {
                if(FindLine(prefix, from) is string line)
                {
                    return line;
                }

                arrived = lineArrived.Task;
            }

            await arrived.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Scans the output history from <paramref name="from"/> for a line starting with <paramref name="prefix"/>; the caller holds the gate.</summary>
    /// <param name="prefix">The line's leading text.</param>
    /// <param name="from">The line index the scan starts at.</param>
    /// <returns>The matched line, or <see langword="null"/>.</returns>
    private string? FindLine(string prefix, int from)
    {
        for(int i = from; i < outputLines.Count; i++)
        {
            if(outputLines[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return outputLines[i];
            }
        }

        return null;
    }

    /// <summary>Drains one stream into its line list from process start, pulsing waiting scans per line; ends at the stream's end when the child exits.</summary>
    /// <param name="reader">The stream to drain.</param>
    /// <param name="linesToAppendTo">The list the drained lines are APPENDED to (an in/out accumulator, not just read).</param>
    /// <returns>The drain task.</returns>
    private async Task DrainAsync(TextReader reader, List<string> linesToAppendTo)
    {
        while(await reader.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            lock(gate)
            {
                linesToAppendTo.Add(line);
                TaskCompletionSource pulse = lineArrived;
                lineArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pulse.SetResult();
            }
        }
    }
}
