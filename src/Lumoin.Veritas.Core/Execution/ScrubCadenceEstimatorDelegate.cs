using System;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Computes the target interval between full scrub walks from a
/// <see cref="ScrubCadenceContext"/>. The configurable policy for how
/// often integrity is re-verified: a deployment supplies its own
/// reliability model, or takes <see cref="ScrubCadenceEstimators.Default"/>.
/// The result is the target <em>initiation</em> cadence; the realised
/// per-block coverage latency is load-dependent and read from telemetry.
/// </summary>
/// <param name="context">The protection, data-size, and cluster inputs.</param>
/// <returns>The target interval between scrub walks.</returns>
public delegate TimeSpan ScrubCadenceEstimatorDelegate(ScrubCadenceContext context);
