using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// One frame on the explicit stack the N-Quads reader uses to parse
/// nested triple terms (<c>&lt;&lt;( s p o )&gt;&gt;</c>) without
/// recursion.
/// </summary>
/// <remarks>
/// A frame accumulates the subject, predicate, and object of one
/// triple term as the reader advances. <see cref="Stage"/> records
/// which position the reader is filling next; a nested triple term in
/// the subject or object position pushes a further frame, and closing
/// it pops the frame and deposits the completed term into the parent's
/// open slot.
/// </remarks>
[DebuggerDisplay("TripleTermFrame stage={Stage}")]
internal sealed class NQuadsTripleTermFrame
{
    /// <summary>The subject of the triple term, once parsed.</summary>
    public RdfTerm? Subject { get; set; }

    /// <summary>The predicate of the triple term, once parsed.</summary>
    public NamedNode? Predicate { get; set; }

    /// <summary>The object of the triple term, once parsed.</summary>
    public RdfTerm? Object { get; set; }

    /// <summary>
    /// Which position the reader fills next: 0 subject, 1 predicate,
    /// 2 object, 3 awaiting the closing <c>)&gt;&gt;</c>.
    /// </summary>
    public int Stage { get; set; }
}
