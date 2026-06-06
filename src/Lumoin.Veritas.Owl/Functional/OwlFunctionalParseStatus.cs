namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// The completeness of the input an <see cref="OwlFunctionalSyntaxIncrementalReader"/>
/// has been fed so far.
/// </summary>
/// <remarks>
/// The status is the value-based editor signal the incremental reader reports
/// instead of a diagnostic: <see cref="NeedMore"/> means the input ends inside
/// a token, group, or undecided constructor head — the rest of the document
/// may simply not have arrived yet, so an editor must not mark the tail as an
/// error — and <see cref="Complete"/> means the input ends at a document
/// boundary. Truncation becomes an error only when
/// <see cref="OwlFunctionalSyntaxIncrementalReader.Complete"/> declares the
/// input final.
/// </remarks>
public enum OwlFunctionalParseStatus
{
    /// <summary>The input fed so far ends at a document boundary: no token, group, or constructor head is open.</summary>
    Complete,

    /// <summary>The input ends mid-token, mid-group, or after an undecided constructor head; feed more before judging.</summary>
    NeedMore
}
