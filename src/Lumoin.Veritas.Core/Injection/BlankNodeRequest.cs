using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Core;

/// <summary>
/// What a caller needs from a <see cref="BlankNodeDelegate"/>. An empty
/// <see cref="CorrelationKey"/> means "a fresh label, unique per call"; a non-empty
/// key means "the same label for the same key within this <see cref="SolutionId"/>"
/// — the SPARQL <c>BNODE(literal)</c> per-solution-correlated semantics.
/// </summary>
/// <remarks>
/// The label is interned into <see cref="Pool"/>, mirroring how the parsers already
/// thread their interning pool. Passed by <see langword="in"/>; no allocation.
/// </remarks>
/// <param name="SolutionId">The identity of the solution mapping the blank node belongs to, or <see cref="Guid.Empty"/> when there is no solution (e.g. plain parsing).</param>
/// <param name="CorrelationKey">The correlation key for per-solution correlation; empty for a fresh-per-call label.</param>
/// <param name="CallSiteSpan">The source span of the syntactic occurrence, used by call-site-deterministic delegates.</param>
/// <param name="Pool">The pool the produced label is interned into.</param>
[DebuggerDisplay("solution={SolutionId} key.len={CorrelationKey.Length} span={CallSiteSpan}")]
public readonly record struct BlankNodeRequest(
    Guid SolutionId,
    ReadOnlyMemory<byte> CorrelationKey,
    SourceSpan CallSiteSpan,
    Utf8StringPool Pool);
