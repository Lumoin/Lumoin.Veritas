using System;
using System.Diagnostics;
using System.IO;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// A snapshot of the observable runtime facts that
/// <see cref="ExecutionPolicy.Resolve()"/> derives a
/// <see cref="ResolvedExecutionPlan"/> from. Reifying the observation
/// as a value separates the (impure) act of probing the host from the
/// (pure) act of resolving a policy against it: <see cref="Observe"/>
/// reads the real environment once, and the resolution is a total
/// function of policy plus this snapshot — which is what lets the
/// derivation be exercised across every branch (browser, CPU quota,
/// core count, protection state) deterministically.
/// </summary>
/// <param name="ProcessorCount">The logical processor count the runtime reports (<see cref="Environment.ProcessorCount"/>), the budget ceiling when no narrower CPU quota is observed.</param>
/// <param name="CpuQuotaCores">The effective whole-plus-fractional core budget read from a cgroup v2 CPU quota (<c>quota / period</c>), or <c>null</c> when no quota is observed — unlimited, no cgroup, cgroup v1, or a non-Linux host.</param>
/// <param name="IsBrowser">Whether the host is a browser WebAssembly runtime, where there is a single cooperative thread, no thread pool to floor, and no memory to probe.</param>
/// <param name="MemoryErrorCorrectionDetected">The hardware memory-protection probe's reading: <c>true</c> = error correction present, <c>false</c> = affirmatively absent, <c>null</c> = inconclusive or not yet probed. An inconclusive reading resolves conservatively to unprotected.</param>
[DebuggerDisplay("ExecutionEnvironment Procs={ProcessorCount} QuotaCores={CpuQuotaCores} Browser={IsBrowser} Ecc={MemoryErrorCorrectionDetected}")]
internal readonly record struct ExecutionEnvironment(
    int ProcessorCount,
    double? CpuQuotaCores,
    bool IsBrowser,
    bool? MemoryErrorCorrectionDetected)
{
    /// <summary>The cgroup v2 file exposing the CPU bandwidth limit of the current control group.</summary>
    private const string CgroupV2CpuMaxPath = "/sys/fs/cgroup/cpu.max";

    /// <summary>
    /// Observes the real runtime once and captures it as a snapshot.
    /// The memory-protection probe is best-effort: it resolves to
    /// <c>null</c> where it cannot tell (a browser, a hypervisor with
    /// synthetic firmware, or a read failure), and an inconclusive
    /// reading resolves conservatively to unprotected.
    /// </summary>
    /// <returns>The observed environment.</returns>
    public static ExecutionEnvironment Observe()
    {
        bool isBrowser = OperatingSystem.IsBrowser();

        return new ExecutionEnvironment(
            ProcessorCount: Environment.ProcessorCount,
            CpuQuotaCores: isBrowser ? null : ReadCgroupCpuQuotaCores(),
            IsBrowser: isBrowser,
            MemoryErrorCorrectionDetected: isBrowser ? null : MemoryProtectionProbe.Detect());
    }

    /// <summary>
    /// Reads the cgroup v2 CPU quota as an effective core budget, or
    /// returns <c>null</c> when no quota constrains this control group.
    /// Probed only on Linux; any read or parse failure resolves to
    /// <c>null</c> so the caller falls back to the processor count
    /// rather than a misread budget.
    /// </summary>
    /// <returns>The effective core budget (<c>quota / period</c>), or <c>null</c> when unconstrained or unreadable.</returns>
    private static double? ReadCgroupCpuQuotaCores()
    {
        if(!OperatingSystem.IsLinux() || !File.Exists(CgroupV2CpuMaxPath))
        {
            return null;
        }

        string content;
        try
        {
            content = File.ReadAllText(CgroupV2CpuMaxPath);
        }
        catch(IOException)
        {
            return null;
        }
        catch(UnauthorizedAccessException)
        {
            return null;
        }

        return ParseCpuMax(content);
    }

    /// <summary>
    /// Parses the cgroup v2 <c>cpu.max</c> payload — "<c>&lt;quota&gt; &lt;period&gt;</c>"
    /// in microseconds, or "<c>max &lt;period&gt;</c>" for an unconstrained group —
    /// into an effective core budget.
    /// </summary>
    /// <param name="content">The file payload.</param>
    /// <returns>The effective core budget, or <c>null</c> when unconstrained or malformed.</returns>
    internal static double? ParseCpuMax(string content)
    {
        ReadOnlySpan<char> span = content.AsSpan().Trim();
        int separator = span.IndexOf(' ');
        if(separator <= 0)
        {
            return null;
        }

        ReadOnlySpan<char> quotaText = span[..separator];
        ReadOnlySpan<char> periodText = span[(separator + 1)..].Trim();

        //An unconstrained group reports the literal "max" quota: no narrower budget than the processor count.
        if(quotaText.SequenceEqual("max"))
        {
            return null;
        }

        if(long.TryParse(quotaText, out long quota)
            && long.TryParse(periodText, out long period)
            && quota > 0
            && period > 0)
        {
            return quota / (double)period;
        }

        return null;
    }
}
