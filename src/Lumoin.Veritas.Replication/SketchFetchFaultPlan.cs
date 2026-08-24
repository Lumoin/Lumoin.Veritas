namespace Lumoin.Veritas.Replication;

/// <summary>
/// A deterministic fault plan for a <see cref="FaultInjectingSketchFetch"/>: given the 1-based index of a fetch on
/// one connection, return the <see cref="SketchFetchFault"/> to inject. Deterministic by call index (not random),
/// so an injected scenario is reproducible — "drop the first two fetches, then pass" certifies that the session
/// converges on the third round, every run.
/// </summary>
/// <param name="callIndex">The 1-based index of the fetch on this connection (the first fetch is 1).</param>
/// <returns>The fault to inject for this fetch.</returns>
public delegate SketchFetchFault SketchFetchFaultPlan(int callIndex);
