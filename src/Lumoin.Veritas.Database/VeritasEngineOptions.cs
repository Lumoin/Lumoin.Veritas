using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The configuration of a <see cref="VeritasEngine"/>: what is wired and how it
/// behaves. By default everything is wired and fully usable; setting a knob to
/// <c>null</c> is the deliberate optimisation that drops a feature for a
/// lighter, faster deployment that links less machinery.
/// </summary>
public sealed record VeritasEngineOptions
{
    /// <summary>The reasoning configuration; the default reasons fully (bounded), and <c>null</c> links no reasoning machinery so the engine serves simple-entailment results.</summary>
    public ReasoningConfiguration? Reasoning { get; init; } = ReasoningConfiguration.Default;

    /// <summary>The execution policy sizing the compute lane and the host's resource use.</summary>
    public ExecutionPolicy Execution { get; init; } = ExecutionPolicy.Default;

    /// <summary>The fully-wired default options.</summary>
    public static VeritasEngineOptions Default { get; } = new();
}
