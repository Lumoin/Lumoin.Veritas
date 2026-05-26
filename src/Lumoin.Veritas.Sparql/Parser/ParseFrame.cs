using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// One frame on <see cref="SparqlParser"/>'s explicit work stack. Carries the
/// production the frame is in, the in-progress accumulators it has built so far,
/// and a stage counter the driver uses to advance the production one step at a
/// time.
/// </summary>
/// <remarks>
/// <para>
/// Fields are intentionally optional and indexed by <see cref="Kind"/>: a single
/// frame layout supports every production the driver knows about, avoiding a
/// frame-type hierarchy whose dispatch would still land in a switch. The
/// accumulator lists are allocated lazily by the step methods that need them.
/// This mirrors the Turtle parser's frame model.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind} stage={Stage} {StartSpan}")]
internal sealed class ParseFrame
{
    /// <summary>Gets or sets the production this frame represents.</summary>
    public ParseFrameKind Kind { get; set; }

    /// <summary>Gets or sets the sub-stage within <see cref="Kind"/> the driver should resume at.</summary>
    public int Stage { get; set; }

    /// <summary>Gets or sets the source span of the first token that started this production.</summary>
    public SourceSpan StartSpan { get; set; }

    /// <summary>Gets or sets the accumulated members of a group graph pattern, in source order.</summary>
    public List<GraphPattern>? Members { get; set; }

    /// <summary>Gets or sets the current contiguous run of triple patterns awaiting flush into a basic graph pattern block.</summary>
    public List<TriplePattern>? PendingTriples { get; set; }
}
