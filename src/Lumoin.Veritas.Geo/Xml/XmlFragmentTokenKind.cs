namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The closed set of token kinds the XML fragment scanner delivers. The set
/// is closed against silent additions: a new member is a design amendment,
/// never a code-level convenience, so a consumer switching over these kinds
/// can be exhaustive and stay exhaustive. An empty-element tag delivers
/// <see cref="ElementOpen"/> immediately followed by
/// <see cref="ElementClose"/>, so both spellings of an empty element present
/// one token-stream shape.
/// </summary>
internal enum XmlFragmentTokenKind
{
    /// <summary>
    /// A start tag was consumed: the element's expanded name and its full
    /// attribute table are available, namespaces resolved.
    /// </summary>
    ElementOpen = 0,

    /// <summary>
    /// An end tag was consumed, or the synthetic close of an empty-element
    /// tag was delivered: the closing element's expanded name is available.
    /// </summary>
    ElementClose = 1,

    /// <summary>
    /// A maximal character-data region was consumed: the decoded,
    /// line-end-normalized bytes are available as one concatenated value.
    /// A region whose decoded content is empty delivers no token at all.
    /// </summary>
    Text = 2,
}
