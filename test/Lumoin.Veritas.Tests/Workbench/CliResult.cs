using System.Diagnostics;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// The captured outcome of running the workbench executable as a
/// child process: exit code plus the entire <c>stdout</c> and
/// <c>stderr</c> streams.
/// </summary>
/// <param name="ExitCode">
/// The process exit code. Zero indicates success per the
/// workbench's documented exit-code contract.
/// </param>
/// <param name="Stdout">
/// Everything written to the child's standard output, read to
/// completion after the process exited.
/// </param>
/// <param name="Stderr">
/// Everything written to the child's standard error, read to
/// completion after the process exited.
/// </param>
[DebuggerDisplay("CliResult Exit={ExitCode} Stdout={Stdout.Length}b Stderr={Stderr.Length}b")]
internal readonly record struct CliResult(int ExitCode, string Stdout, string Stderr);
