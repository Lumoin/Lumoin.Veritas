using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// The outcome of a soak scenario run: how many iterations of the
/// scenario loop completed within the requested duration, how long
/// the loop actually ran (always ≥ the requested duration since the
/// last iteration is allowed to complete), and a scenario-specific
/// auxiliary count.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auxiliary count.</b> For
/// <see cref="HypertrieSoak.RunQuerySoakAsync"/> the auxiliary count is
/// the total number of solutions emitted across every iteration —
/// useful for confirming the query path produced results rather
/// than silently failing. For
/// <see cref="HypertrieSoak.RunBuildSoakAsync"/> the auxiliary count is
/// zero; the build path produces no per-iteration tally beyond the
/// iteration count itself.
/// </para>
/// </remarks>
/// <param name="Iterations">
/// The number of complete iterations of the scenario loop. Zero is
/// a legitimate outcome when the requested duration is shorter than
/// a single iteration.
/// </param>
/// <param name="Elapsed">
/// The wall-clock elapsed time of the scenario loop, measured from
/// just before the first iteration starts to just after the last
/// iteration ends. Excludes any setup work the scenario performs
/// before the timing loop begins.
/// </param>
/// <param name="AuxiliaryCount">
/// A scenario-specific count: query solutions for query soaks, zero
/// for build soaks.
/// </param>
[DebuggerDisplay("SoakResult {Iterations} iter in {Elapsed.TotalSeconds,nq:F2}s (aux {AuxiliaryCount})")]
internal readonly record struct SoakResult(long Iterations, TimeSpan Elapsed, long AuxiliaryCount);
