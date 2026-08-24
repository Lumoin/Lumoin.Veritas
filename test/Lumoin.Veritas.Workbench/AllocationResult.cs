using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// One-shot allocation-and-timing measurement for a single
/// hypertrie build. The build runs once against a fixed-size
/// synthetic corpus; this record carries the resulting wall-clock
/// time, the number of bytes the build allocated on the current
/// thread, and a post-build heap snapshot for retention context.
/// </summary>
/// <param name="Elapsed">Wall-clock build duration.</param>
/// <param name="AllocatedBytes">Bytes allocated by the build on the current thread.</param>
/// <param name="PeakGen2Bytes">Heap size in bytes after the build, sampled via <see cref="GC.GetTotalMemory(bool)"/>.</param>
[DebuggerDisplay("AllocationResult {Elapsed.TotalSeconds,nq:F2}s, {AllocatedBytes,nq:N0} bytes")]
internal readonly record struct AllocationResult(TimeSpan Elapsed, long AllocatedBytes, long PeakGen2Bytes);
