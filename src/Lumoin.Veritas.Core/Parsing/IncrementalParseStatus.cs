namespace Lumoin.Veritas.Core.Parsing;

/// <summary>
/// The completeness signal a byte-fed incremental syntax reader returns from each feed: whether the input so far ends
/// at a document boundary or inside an unfinished construct. It is the shared editor-consumption contract every
/// language front-end's incremental reader presents, so an editor can switch on one type across formats.
/// </summary>
/// <remarks>
/// The contract is value-based: incompleteness is reported as <see cref="NeedMore"/>, never as a diagnostic, so an
/// editor does not flag a half-typed tail as an error. Truncation becomes a recorded diagnostic only when the reader's
/// completion step declares the input final.
/// </remarks>
public enum IncrementalParseStatus
{
    /// <summary>The input so far ends at a document boundary: no construct is half-scanned and nothing is left open.</summary>
    Complete = 0,

    /// <summary>The input so far ends inside an unfinished construct; more bytes are needed. An editor must not mark the tail as an error.</summary>
    NeedMore = 1
}
