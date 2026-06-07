namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// The completeness of the input an <see cref="OwlManchesterSyntaxIncrementalReader"/>
/// has been fed so far.
/// </summary>
/// <remarks>
/// The status is the value-based editor signal the incremental reader reports
/// instead of a diagnostic: <see cref="NeedMore"/> means the input ends inside
/// a token or group, or after a token that grammatically requires a
/// continuation — a frame or section keyword awaiting its content, a comma, an
/// infix operator — so an editor must not mark the tail as an error.
/// <see cref="Complete"/> means a document may validly end here. Truncation
/// becomes an error only when
/// <see cref="OwlManchesterSyntaxIncrementalReader.Complete"/> declares the
/// input final.
/// </remarks>
public enum OwlManchesterParseStatus
{
    /// <summary>The input fed so far ends where a document may validly end.</summary>
    Complete,

    /// <summary>The input ends mid-token, mid-group, or after a token that requires a continuation; feed more before judging.</summary>
    NeedMore
}
