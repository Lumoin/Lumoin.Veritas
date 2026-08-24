namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// The outcome of one step of a <see cref="ParseFrame"/> on
/// <see cref="SparqlParser"/>'s explicit work stack.
/// </summary>
/// <remarks>
/// The driver advances a frame one stage at a time; each step reports whether to
/// keep advancing the current frame, that the production needs tokens not yet
/// available (the future streaming shape; this batch buffers the whole query so
/// it does not arise), or that the production is complete and its node is ready
/// for the parent frame to consume.
/// </remarks>
internal enum ParseStatus
{
    /// <summary>The frame advanced; the driver should continue stepping it (or a child it pushed).</summary>
    Continue,

    /// <summary>The current production needs more tokens than have been fed; feed more and resume.</summary>
    NeedMore,

    /// <summary>The production is complete; the driver pops the frame and hands its node to the parent.</summary>
    Produced
}
