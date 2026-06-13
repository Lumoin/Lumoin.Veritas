namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Observes the current runtime facts the compute lane resolves its
/// width against. The lane calls this on each control-plane tick to
/// re-read the CPU budget; production binds it to
/// <see cref="ExecutionEnvironment.Observe"/>, and tests bind a
/// controlled snapshot so a quota change and the resize it drives are
/// deterministically exercisable.
/// </summary>
/// <returns>The observed environment.</returns>
internal delegate ExecutionEnvironment ObserveEnvironmentDelegate();
