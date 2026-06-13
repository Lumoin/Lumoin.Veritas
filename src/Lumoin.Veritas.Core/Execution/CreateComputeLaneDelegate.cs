namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Builds an <see cref="IComputeLane"/> from a resolved policy. The
/// override seam for the platform selection: a host that wants a
/// non-default execution substrate — a custom platform implementation,
/// or a forced choice in a test — supplies one of these instead of the
/// default platform factory.
/// </summary>
/// <param name="policy">The policy the lane sizes itself from.</param>
/// <returns>The lane.</returns>
public delegate IComputeLane CreateComputeLaneDelegate(ExecutionPolicy policy);
