using System;

using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// A forward token cursor over one whole-document UTF-8 span, tokenizing the
/// XML fragment subset the geospatial codecs read: elements, attributes and
/// character data with namespace resolution, entity and character-reference
/// decoding, and a hard security floor — no document type declarations, no
/// entity declarations, no processing instructions — because these readers
/// ingest untrusted input. The scanner never throws for content: every
/// content condition is a refusal by value, a refusal is terminal, and
/// exhaustion is a sticky false with the no-offense sentinel. Every exposed
/// span is valid until the next read or disposal, whichever comes first;
/// advance the original value, never a copy, or the copy's position silently
/// diverges. Construction performs no input work and allocates nothing; the
/// scratch is allocated on the first read that needs it and dropped in
/// <see cref="Dispose"/>.
/// </summary>
internal ref struct XmlFragmentScanner
{
    /// <summary>The seven-item classification of markup opening with an exclamation mark.</summary>
    private enum BangConstruct
    {
        /// <summary>A comment opens at the cursor.</summary>
        Comment = 0,

        /// <summary>A CDATA section opens at the cursor.</summary>
        CdataSection = 1,

        /// <summary>A construct the security floor prohibits opens at the cursor.</summary>
        Prohibited = 2,

        /// <summary>The bytes diverge from every recognized construct at a known offset.</summary>
        Diverged = 3,

        /// <summary>The input ends while still a prefix of some recognized construct.</summary>
        Truncated = 4,
    }

    /// <summary>The whole document the scanner walks and every input extent indexes.</summary>
    private ReadOnlySpan<byte> Document { get; }

    /// <summary>The working state, absent until the first read that needs it.</summary>
    private XmlFragmentScratch? Scratch { get; set; }

    /// <summary>True once <see cref="Dispose"/> ran; reads afterwards fail loud.</summary>
    private bool Disposed { get; set; }

    /// <summary>The terminal refusal's kind; the none value means no refusal has occurred.</summary>
    private GeometryCodecRefusalKind FailureKind { get; set; }

    /// <summary>The terminal refusal's byte offset.</summary>
    private int FailureOffset { get; set; }

    /// <summary>True once the root element closed and the trailing tail validated.</summary>
    private bool IsExhausted { get; set; }

    /// <summary>True once the prolog was consumed and validated.</summary>
    private bool PrologConsumed { get; set; }

    /// <summary>True once the root element's start tag was consumed.</summary>
    private bool RootSeen { get; set; }

    /// <summary>The number of open elements; equally the used prefix of the element stack.</summary>
    private int Depth { get; set; }

    /// <summary>True when an empty-element tag's synthetic close is owed as the next token.</summary>
    private bool PendingEmptyClose { get; set; }

    /// <summary>The cursor into the document.</summary>
    private int Position { get; set; }

    /// <summary>The logical length of the binding arena; pops truncate it without writing.</summary>
    private int ArenaLength { get; set; }

    /// <summary>The number of in-scope namespace bindings; pops truncate it.</summary>
    private int BindingCount { get; set; }

    /// <summary>The append cursor of the per-token decode buffer.</summary>
    private int DecodeLength { get; set; }

    /// <summary>True while a token is current and its accessors are legal.</summary>
    private bool HasToken { get; set; }

    /// <summary>The current token's kind.</summary>
    private XmlFragmentTokenKind CurrentKind { get; set; }

    /// <summary>The current token's anchor offset.</summary>
    private int CurrentTokenStart { get; set; }

    /// <summary>The offset of the byte closing the current start tag.</summary>
    private int CurrentStartTagClose { get; set; }

    /// <summary>The current element token's local name extent into the input.</summary>
    private int ElementLocalNameStart { get; set; }

    /// <summary>The current element token's local name length.</summary>
    private int ElementLocalNameLength { get; set; }

    /// <summary>The store holding the current element token's namespace bytes.</summary>
    private XmlFragmentScratch.ByteStore ElementNamespaceStore { get; set; }

    /// <summary>The current element token's namespace extent start.</summary>
    private int ElementNamespaceStart { get; set; }

    /// <summary>The current element token's namespace extent length; zero means no namespace.</summary>
    private int ElementNamespaceLength { get; set; }

    /// <summary>The number of attributes of the current start tag, declarations excluded.</summary>
    private int CurrentAttributeCount { get; set; }

    /// <summary>The store holding the current text token's bytes.</summary>
    private XmlFragmentScratch.ByteStore TextStore { get; set; }

    /// <summary>The current text token's extent start in its store.</summary>
    private int TextStart { get; set; }

    /// <summary>The current text token's decoded length.</summary>
    private int TextLength { get; set; }

    /// <summary>Whether every decoded byte of the current text token is XML whitespace.</summary>
    private bool TextWhitespaceValue { get; set; }

    /// <summary>The number of live decoded-to-document segments of the current text token.</summary>
    private int SegmentCount { get; set; }

    /// <summary>True while the current text region is a single verbatim input run.</summary>
    private bool RegionClean { get; set; }

    /// <summary>The input start of the clean region's verbatim run.</summary>
    private int RegionAliasStart { get; set; }

    /// <summary>The length of the clean region's verbatim run.</summary>
    private int RegionAliasLength { get; set; }

    /// <summary>The document offset of the first byte that contributed decoded content; minus one until one does.</summary>
    private int RegionFirstContent { get; set; }

    /// <summary>Creates a cursor over one whole document; no input is read and nothing is rented here.</summary>
    public XmlFragmentScanner(ReadOnlySpan<byte> utf8Document)
    {
        Document = utf8Document;
    }

    /// <summary>Drops the scratch; a second disposal is a no-op.</summary>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        Disposed = true;
        HasToken = false;
        Scratch?.Dispose();
        Scratch = null;
    }

    /// <summary>
    /// Advances to the next token. True delivers a token; false with the
    /// no-offense sentinel is exhaustion, which is sticky; false with a kind
    /// is the refusal, which is terminal — every later call repeats the same
    /// kind and byte offset, and the cursor never advances past an offense.
    /// </summary>
    /// <param name="kind">The delivered token's kind.</param>
    /// <param name="refusal">
    /// <see cref="GeometryCodecRefusal.None"/> on delivery and at exhaustion;
    /// the offense otherwise.
    /// </param>
    /// <returns>True when a token was delivered.</returns>
    public bool TryReadNext(out XmlFragmentTokenKind kind, out GeometryCodecRefusal refusal)
    {
        ObjectDisposedException.ThrowIf(Disposed, typeof(XmlFragmentScanner));
        kind = default;
        if(FailureKind != GeometryCodecRefusalKind.None)
        {
            refusal = new GeometryCodecRefusal(FailureKind, FailureOffset);

            return false;
        }

        if(IsExhausted)
        {
            refusal = GeometryCodecRefusal.None;

            return false;
        }

        HasToken = false;
        bool produced = ReadCore();
        if(FailureKind != GeometryCodecRefusalKind.None)
        {
            refusal = new GeometryCodecRefusal(FailureKind, FailureOffset);

            return false;
        }

        if(!produced)
        {
            IsExhausted = true;
            refusal = GeometryCodecRefusal.None;

            return false;
        }

        kind = CurrentKind;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>The absolute document offset of the current token's anchor byte: an element token's opening angle bracket, or a text token's first contributing content byte.</summary>
    public int TokenStartOffset
    {
        get
        {
            EnsureCurrentToken();

            return CurrentTokenStart;
        }
    }

    /// <summary>The absolute offset of the byte closing the current start tag, in both tag spellings.</summary>
    public int StartTagCloseOffset
    {
        get
        {
            EnsureOpenToken();

            return CurrentStartTagClose;
        }
    }

    /// <summary>
    /// The current element token's resolved namespace name; an empty span
    /// means the element is in no namespace. Valid until the next read or
    /// disposal.
    /// </summary>
    public ReadOnlySpan<byte> ElementNamespace
    {
        get
        {
            EnsureElementToken();

            return SliceStore(ElementNamespaceStore, ElementNamespaceStart, ElementNamespaceLength);
        }
    }

    /// <summary>The current element token's local name, aliasing the input. Valid until the next read or disposal.</summary>
    public ReadOnlySpan<byte> ElementLocalName
    {
        get
        {
            EnsureElementToken();

            return Document.Slice(ElementLocalNameStart, ElementLocalNameLength);
        }
    }

    /// <summary>The number of attributes on the current start tag; namespace declarations are consumed as bindings and never counted.</summary>
    public int AttributeCount
    {
        get
        {
            EnsureOpenToken();

            return CurrentAttributeCount;
        }
    }

    /// <summary>The resolved namespace name of the indexed attribute; an empty span means no namespace. Valid until the next read or disposal.</summary>
    public ReadOnlySpan<byte> AttributeNamespace(int index)
    {
        XmlFragmentScratch.AttributeEntry entry = AttributeAt(index);

        return SliceStore(entry.NamespaceStore, entry.NamespaceStart, entry.NamespaceLength);
    }

    /// <summary>The local name of the indexed attribute, aliasing the input. Valid until the next read or disposal.</summary>
    public ReadOnlySpan<byte> AttributeLocalName(int index)
    {
        XmlFragmentScratch.AttributeEntry entry = AttributeAt(index);

        return Document.Slice(entry.LocalNameStart, entry.LocalNameLength);
    }

    /// <summary>
    /// The decoded, normalized value of the indexed attribute. Whether the
    /// span aliases the input or the scanner's scratch is deliberately
    /// unobservable. Valid until the next read or disposal.
    /// </summary>
    public ReadOnlySpan<byte> AttributeValue(int index)
    {
        XmlFragmentScratch.AttributeEntry entry = AttributeAt(index);

        return SliceStore(entry.ValueStore, entry.ValueStart, entry.ValueLength);
    }

    /// <summary>The absolute offset of the first byte of the indexed attribute's qualified name as written.</summary>
    public int AttributeNameOffset(int index) => AttributeAt(index).NameOffset;

    /// <summary>
    /// The absolute offset of the first byte inside the indexed attribute's
    /// value quotes — well-defined regardless of decoding, and the anchor
    /// for whole-value offenses.
    /// </summary>
    public int AttributeValueOffset(int index) => AttributeAt(index).ValueOffset;

    /// <summary>
    /// Finds an attribute of the current start tag by expanded name; an
    /// empty namespace argument means an attribute in no namespace.
    /// </summary>
    /// <param name="namespaceUri">The namespace name to match, empty for none.</param>
    /// <param name="localName">The local name to match.</param>
    /// <param name="index">The found attribute's index, for the per-index accessors.</param>
    /// <returns>True when a matching attribute exists.</returns>
    public bool TryFindAttribute(ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName, out int index)
    {
        EnsureOpenToken();
        for(int i = 0; i < CurrentAttributeCount; i++)
        {
            XmlFragmentScratch.AttributeEntry entry = Scratch!.Attributes.Span[i];
            if(!Document.Slice(entry.LocalNameStart, entry.LocalNameLength).SequenceEqual(localName))
            {
                continue;
            }

            if(SliceStore(entry.NamespaceStore, entry.NamespaceStart, entry.NamespaceLength).SequenceEqual(namespaceUri))
            {
                index = i;

                return true;
            }
        }

        index = 0;

        return false;
    }

    /// <summary>
    /// The current text token's decoded, line-end-normalized bytes — one
    /// concatenated value per maximal character-data region. Whether the
    /// span aliases the input or the scanner's scratch is deliberately
    /// unobservable. Valid until the next read or disposal.
    /// </summary>
    public ReadOnlySpan<byte> Text
    {
        get
        {
            EnsureTextToken();

            return SliceStore(TextStore, TextStart, TextLength);
        }
    }

    /// <summary>
    /// True when every decoded byte of the current text token is XML
    /// whitespace, computed reference-blind over the decoded bytes — a
    /// space arriving by character reference counts.
    /// </summary>
    public bool TextIsWhitespace
    {
        get
        {
            EnsureTextToken();

            return TextWhitespaceValue;
        }
    }

    /// <summary>
    /// Maps a position inside the current text token's decoded bytes back
    /// to its absolute document offset, through the segment list the
    /// assembler recorded — the member that lets a consumer refuse inside
    /// decoded text at the first offending input byte.
    /// </summary>
    /// <param name="decodedOffset">A position inside the decoded text.</param>
    /// <returns>The absolute document offset the decoded position came from.</returns>
    public int MapTextOffset(int decodedOffset)
    {
        EnsureTextToken();
        ArgumentOutOfRangeException.ThrowIfNegative(decodedOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(decodedOffset, TextLength);
        if(TextStore == XmlFragmentScratch.ByteStore.Input)
        {
            return TextStart + decodedOffset;
        }

        Span<XmlFragmentScratch.TextSegment> segments = Scratch!.TextSegments.Span;
        int low = 0;
        int high = SegmentCount - 1;
        while(low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if(segments[middle].DecodedStart <= decodedOffset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        XmlFragmentScratch.TextSegment segment = segments[low];

        return segment.DocumentStart + (decodedOffset - segment.DecodedStart);
    }

    /// <summary>Produces the next token or drains to exhaustion; a refusal is recorded in the failure state.</summary>
    private bool ReadCore()
    {
        if(PendingEmptyClose)
        {
            EmitPendingClose();

            return true;
        }

        if(!PrologConsumed)
        {
            if(!TryConsumeProlog())
            {
                return false;
            }

            PrologConsumed = true;
        }

        while(true)
        {
            if(Depth == 0 && RootSeen)
            {
                return ReadEpilog();
            }

            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == (byte)'<')
            {
                if(Position + 1 >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                byte next = Document[Position + 1];
                if(next == (byte)'/')
                {
                    return TryScanEndTag();
                }

                if(next == (byte)'?')
                {
                    return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
                }

                if(next == (byte)'!')
                {
                    BangConstruct construct = ClassifyBangConstruct(out int divergenceOffset);
                    if(construct == BangConstruct.Prohibited)
                    {
                        return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
                    }

                    if(construct == BangConstruct.Diverged)
                    {
                        return Fail(GeometryCodecRefusalKind.MalformedDocument, divergenceOffset);
                    }

                    if(construct == BangConstruct.Truncated)
                    {
                        return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                    }

                    //A comment or CDATA section in element content opens a
                    //character-data region, because any adjacent data must
                    //concatenate with what these constructs surround.
                    bool producedText = false;
                    if(!TryScanTextRegion(ref producedText))
                    {
                        return false;
                    }

                    if(producedText)
                    {
                        return true;
                    }

                    continue;
                }

                return TryScanStartTag();
            }

            bool produced = false;
            if(!TryScanTextRegion(ref produced))
            {
                return false;
            }

            if(produced)
            {
                return true;
            }
        }
    }

    /// <summary>Records the terminal refusal and reports failure to the caller.</summary>
    private bool Fail(GeometryCodecRefusalKind kind, int offset)
    {
        FailureKind = kind;
        FailureOffset = offset;
        HasToken = false;

        return false;
    }

    /// <summary>
    /// Consumes the optional byte-order mark, the optional XML declaration,
    /// and every comment and whitespace run before the root element, leaving
    /// the cursor at the root start tag's opening angle bracket.
    /// </summary>
    private bool TryConsumeProlog()
    {
        if(Document.Length == 0)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, 0);
        }

        if(GeometryCodecText.StartsWithByteOrderMark(Document))
        {
            Position = GeometryCodecText.Utf8ByteOrderMark.Length;
        }

        if(Document[Position..].StartsWith(XmlVocabulary.DeclarationOpening))
        {
            int declarationStart = Position;
            int afterOpening = Position + XmlVocabulary.DeclarationOpening.Length;
            if(afterOpening >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            //Without the required whitespace the bytes are a processing
            //instruction whose target merely starts with the reserved
            //letters, and the security floor takes it.
            if(!XmlLexicon.IsWhitespace(Document[afterOpening]))
            {
                return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, declarationStart);
            }

            if(!TryParseDeclaration(declarationStart))
            {
                return false;
            }
        }

        while(true)
        {
            SkipWhitespace();
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value != (byte)'<')
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(Position + 1 >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte next = Document[Position + 1];
            if(next == (byte)'?')
            {
                return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
            }

            if(next == (byte)'/')
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(next == (byte)'!')
            {
                BangConstruct construct = ClassifyBangConstruct(out int divergenceOffset);
                if(construct == BangConstruct.Comment)
                {
                    if(!TryScanComment())
                    {
                        return false;
                    }

                    continue;
                }

                if(construct == BangConstruct.Prohibited)
                {
                    return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
                }

                if(construct == BangConstruct.CdataSection)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                if(construct == BangConstruct.Truncated)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                return Fail(GeometryCodecRefusalKind.MalformedDocument, divergenceOffset);
            }

            return true;
        }
    }

    /// <summary>
    /// Parses the XML declaration's exact grammar: a required 1.0 version,
    /// an optional UTF-8 encoding in its two admitted spellings, an optional
    /// yes-or-no standalone, whitespace per the productions, either quote.
    /// </summary>
    private bool TryParseDeclaration(int declarationStart)
    {
        Position = declarationStart + XmlVocabulary.DeclarationOpening.Length;
        SkipWhitespace();
        if(!TryMatchPseudoAttributeName(XmlVocabulary.VersionName))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        if(!TryParseEq())
        {
            return false;
        }

        if(!TryParsePseudoAttributeValue(out int valueStart, out int valueLength))
        {
            return false;
        }

        if(!Document.Slice(valueStart, valueLength).SequenceEqual(XmlVocabulary.VersionValue))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, valueStart);
        }

        bool encodingSeen = false;
        bool standaloneSeen = false;
        while(true)
        {
            int whitespaceCount = SkipWhitespace();
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            if(Document[Position] == (byte)'?')
            {
                if(Position + 1 >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                if(Document[Position + 1] != (byte)'>')
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position + 1);
                }

                Position += XmlVocabulary.DeclarationClose.Length;

                return true;
            }

            if(whitespaceCount == 0)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(!encodingSeen && !standaloneSeen && TryMatchPseudoAttributeName(XmlVocabulary.EncodingName))
            {
                encodingSeen = true;
                if(!TryParseEq())
                {
                    return false;
                }

                if(!TryParsePseudoAttributeValue(out valueStart, out valueLength))
                {
                    return false;
                }

                ReadOnlySpan<byte> encoding = Document.Slice(valueStart, valueLength);
                if(!encoding.SequenceEqual(XmlVocabulary.Utf8EncodingUppercase)
                    && !encoding.SequenceEqual(XmlVocabulary.Utf8EncodingLowercase))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, valueStart);
                }

                continue;
            }

            if(!standaloneSeen && TryMatchPseudoAttributeName(XmlVocabulary.StandaloneName))
            {
                standaloneSeen = true;
                if(!TryParseEq())
                {
                    return false;
                }

                if(!TryParsePseudoAttributeValue(out valueStart, out valueLength))
                {
                    return false;
                }

                ReadOnlySpan<byte> standalone = Document.Slice(valueStart, valueLength);
                if(!standalone.SequenceEqual(XmlVocabulary.YesValue) && !standalone.SequenceEqual(XmlVocabulary.NoValue))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, valueStart);
                }

                continue;
            }

            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }
    }

    /// <summary>Matches a pseudo-attribute name at the cursor and consumes it; no movement on mismatch.</summary>
    private bool TryMatchPseudoAttributeName(ReadOnlySpan<byte> name)
    {
        if(!Document[Position..].StartsWith(name))
        {
            return false;
        }

        Position += name.Length;

        return true;
    }

    /// <summary>Consumes optional whitespace, the equals sign, and optional whitespace.</summary>
    private bool TryParseEq()
    {
        SkipWhitespace();
        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        if(Document[Position] != (byte)'=')
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        Position++;
        SkipWhitespace();

        return true;
    }

    /// <summary>
    /// Consumes a quote-delimited declaration value, validating its bytes
    /// against the character production; the caller compares the raw bytes
    /// ordinally, so no decoding exists on this path.
    /// </summary>
    private bool TryParsePseudoAttributeValue(out int valueStart, out int valueLength)
    {
        valueStart = 0;
        valueLength = 0;
        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        byte quote = Document[Position];
        if(quote is not ((byte)'"' or (byte)'\''))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        Position++;
        valueStart = Position;
        while(true)
        {
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == quote)
            {
                valueLength = Position - valueStart;
                Position++;

                return true;
            }

            if(value < 0x20 && !XmlLexicon.IsWhitespace(value))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(value >= 0x80)
            {
                if(!TryDecodeScalarAt(Position, out int scalar, out int length))
                {
                    return false;
                }

                if(!XmlLexicon.IsCharacter(scalar))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                Position += length;

                continue;
            }

            Position++;
        }
    }

    /// <summary>
    /// Drains the epilog after the root element closed: comments and
    /// whitespace skip, the security floor keeps its kinds, and everything
    /// else is trailing content. Returning false with no recorded failure is
    /// exhaustion.
    /// </summary>
    private bool ReadEpilog()
    {
        while(true)
        {
            SkipWhitespace();
            if(Position >= Document.Length)
            {
                return false;
            }

            byte value = Document[Position];
            if(value != (byte)'<')
            {
                return Fail(GeometryCodecRefusalKind.TrailingContent, Position);
            }

            if(Position + 1 >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte next = Document[Position + 1];
            if(next == (byte)'?')
            {
                return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
            }

            if(next == (byte)'!')
            {
                BangConstruct construct = ClassifyBangConstruct(out _);
                if(construct == BangConstruct.Comment)
                {
                    if(!TryScanComment())
                    {
                        return false;
                    }

                    continue;
                }

                if(construct == BangConstruct.Prohibited)
                {
                    return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
                }

                if(construct == BangConstruct.Truncated)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                return Fail(GeometryCodecRefusalKind.TrailingContent, Position);
            }

            return Fail(GeometryCodecRefusalKind.TrailingContent, Position);
        }
    }

    /// <summary>
    /// Classifies the markup at the cursor's exclamation mark: a comment, a
    /// CDATA section, a security-floor construct, a divergence from every
    /// recognized construct at a reported offset, or input that ends while
    /// still a prefix of one.
    /// </summary>
    private readonly BangConstruct ClassifyBangConstruct(out int divergenceOffset)
    {
        divergenceOffset = 0;
        ReadOnlySpan<byte> remainder = Document[Position..];
        if(remainder.StartsWith(XmlVocabulary.CommentOpening))
        {
            return BangConstruct.Comment;
        }

        if(remainder.StartsWith(XmlVocabulary.CdataOpening))
        {
            return BangConstruct.CdataSection;
        }

        if(remainder.StartsWith(XmlVocabulary.DoctypeOpening)
            || remainder.StartsWith(XmlVocabulary.EntityDeclarationOpening)
            || remainder.StartsWith(XmlVocabulary.ElementDeclarationOpening)
            || remainder.StartsWith(XmlVocabulary.AttributeListDeclarationOpening)
            || remainder.StartsWith(XmlVocabulary.NotationDeclarationOpening))
        {
            return BangConstruct.Prohibited;
        }

        int longest = CommonPrefixLength(remainder, XmlVocabulary.CommentOpening);
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.CdataOpening));
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.DoctypeOpening));
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.EntityDeclarationOpening));
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.ElementDeclarationOpening));
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.AttributeListDeclarationOpening));
        longest = Math.Max(longest, CommonPrefixLength(remainder, XmlVocabulary.NotationDeclarationOpening));
        if(longest == remainder.Length)
        {
            return BangConstruct.Truncated;
        }

        divergenceOffset = Position + longest;

        return BangConstruct.Diverged;
    }

    /// <summary>The length of the longest common prefix of two spans.</summary>
    private static int CommonPrefixLength(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int bound = Math.Min(left.Length, right.Length);
        int index = 0;
        while(index < bound && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Consumes a comment from its opening angle bracket. The first interior
    /// double hyphen must be the terminator's; content characters are
    /// validated in document order first, so an earlier character offense
    /// wins over a later terminator offense.
    /// </summary>
    private bool TryScanComment()
    {
        int contentStart = Position + XmlVocabulary.CommentOpening.Length;
        ReadOnlySpan<byte> remainder = Document[contentStart..];
        ReadOnlySpan<byte> doubleHyphen = XmlVocabulary.CommentClose[..2];
        int pairIndex = remainder.IndexOf(doubleHyphen);
        int contentLength = pairIndex < 0 ? remainder.Length : pairIndex;
        if(!ValidateSkippedContent(contentStart, contentLength))
        {
            return false;
        }

        if(pairIndex < 0)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        int pairPosition = contentStart + pairIndex;
        if(pairPosition + 2 >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        if(Document[pairPosition + 2] != (byte)'>')
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, pairPosition);
        }

        Position = pairPosition + XmlVocabulary.CommentClose.Length;

        return true;
    }

    /// <summary>Validates a skipped run — comment content — against UTF-8 validity and the character production.</summary>
    private bool ValidateSkippedContent(int start, int length)
    {
        int position = start;
        int end = start + length;
        while(position < end)
        {
            byte value = Document[position];
            if(value < 0x80)
            {
                if(value < 0x20 && !XmlLexicon.IsWhitespace(value))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, position);
                }

                position++;

                continue;
            }

            if(!TryDecodeScalarAt(position, out int scalar, out int scalarLength))
            {
                return false;
            }

            if(!XmlLexicon.IsCharacter(scalar))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, position);
            }

            position += scalarLength;
        }

        return true;
    }

    /// <summary>
    /// Consumes a CDATA section from its opening angle bracket, contributing
    /// its interior verbatim to the current text region — references decode
    /// nowhere here, but line ends normalize and characters validate.
    /// </summary>
    private bool TryScanCdataSection()
    {
        int contentStart = Position + XmlVocabulary.CdataOpening.Length;
        ReadOnlySpan<byte> remainder = Document[contentStart..];
        int closeIndex = remainder.IndexOf(XmlVocabulary.CdataClose);
        int contentLength = closeIndex < 0 ? remainder.Length : closeIndex;
        int position = contentStart;
        int end = contentStart + contentLength;
        while(position < end)
        {
            byte value = Document[position];
            if(value == 0xD)
            {
                int lineEndLength = position + 1 < end && Document[position + 1] == 0xA ? 2 : 1;
                AppendTextLineEnd(position);
                position += lineEndLength;

                continue;
            }

            if(value < 0x80)
            {
                if(value < 0x20 && !XmlLexicon.IsWhitespace(value))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, position);
                }

                AppendTextVerbatim(position, length: 1, XmlLexicon.IsWhitespace(value));
                position++;

                continue;
            }

            if(!TryDecodeScalarAt(position, out int scalar, out int scalarLength))
            {
                return false;
            }

            if(!XmlLexicon.IsCharacter(scalar))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, position);
            }

            AppendTextVerbatim(position, scalarLength, isWhitespace: false);
            position += scalarLength;
        }

        if(closeIndex < 0)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        Position = end + XmlVocabulary.CdataClose.Length;

        return true;
    }

    /// <summary>
    /// Assembles one maximal character-data region — plain runs, CDATA
    /// interiors and the gaps comments leave, concatenated — into either an
    /// input-aliasing clean run or the decode buffer. A region contributing
    /// zero decoded bytes produces no token and reports that through
    /// <paramref name="produced"/>.
    /// </summary>
    private bool TryScanTextRegion(ref bool produced)
    {
        EnsureScratch();
        DecodeLength = 0;
        SegmentCount = 0;
        RegionClean = true;
        RegionAliasStart = 0;
        RegionAliasLength = 0;
        RegionFirstContent = -1;
        TextWhitespaceValue = true;
        while(true)
        {
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == (byte)'<')
            {
                if(Position + 1 >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                if(Document[Position + 1] == (byte)'!')
                {
                    BangConstruct construct = ClassifyBangConstruct(out int divergenceOffset);
                    if(construct == BangConstruct.Comment)
                    {
                        MaterializeRegion();
                        if(!TryScanComment())
                        {
                            return false;
                        }

                        continue;
                    }

                    if(construct == BangConstruct.CdataSection)
                    {
                        MaterializeRegion();
                        if(!TryScanCdataSection())
                        {
                            return false;
                        }

                        continue;
                    }

                    if(construct == BangConstruct.Prohibited)
                    {
                        return Fail(GeometryCodecRefusalKind.ProhibitedConstruct, Position);
                    }

                    if(construct == BangConstruct.Truncated)
                    {
                        return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                    }

                    return Fail(GeometryCodecRefusalKind.MalformedDocument, divergenceOffset);
                }

                break;
            }

            if(value == (byte)'&')
            {
                MaterializeRegion();
                int ampersand = Position;
                if(!TryScanReference(out int scalar))
                {
                    return false;
                }

                AppendTextScalar(scalar, ampersand);

                continue;
            }

            if(!ScanCharacterDataRun())
            {
                return false;
            }
        }

        bool hasContent = RegionClean ? RegionAliasLength > 0 : DecodeLength > 0;
        if(hasContent)
        {
            CurrentKind = XmlFragmentTokenKind.Text;
            CurrentTokenStart = RegionFirstContent;
            TextStore = RegionClean ? XmlFragmentScratch.ByteStore.Input : XmlFragmentScratch.ByteStore.Decode;
            TextStart = RegionClean ? RegionAliasStart : 0;
            TextLength = RegionClean ? RegionAliasLength : DecodeLength;
            HasToken = true;
            produced = true;
        }

        return true;
    }

    /// <summary>
    /// Scans one plain character-data run up to the next markup, reference,
    /// or end of input: line ends normalize, characters validate, and the
    /// CDATA-section-close sequence is detected over this run's raw bytes
    /// alone, never over the assembled region.
    /// </summary>
    private bool ScanCharacterDataRun()
    {
        int bracketRun = 0;
        while(Position < Document.Length)
        {
            byte value = Document[Position];
            if(value is (byte)'<' or (byte)'&')
            {
                return true;
            }

            if(value == (byte)'>' && bracketRun >= 2)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position - 2);
            }

            bracketRun = value == (byte)']' ? bracketRun + 1 : 0;
            if(value == 0xD)
            {
                int lineEndLength = Position + 1 < Document.Length && Document[Position + 1] == 0xA ? 2 : 1;
                AppendTextLineEnd(Position);
                Position += lineEndLength;

                continue;
            }

            if(value < 0x80)
            {
                if(value < 0x20 && !XmlLexicon.IsWhitespace(value))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                AppendTextVerbatim(Position, length: 1, XmlLexicon.IsWhitespace(value));
                Position++;

                continue;
            }

            if(!TryDecodeScalarAt(Position, out int scalar, out int scalarLength))
            {
                return false;
            }

            if(!XmlLexicon.IsCharacter(scalar))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            AppendTextVerbatim(Position, scalarLength, isWhitespace: false);
            Position += scalarLength;
        }

        return true;
    }

    /// <summary>
    /// Copies the clean region's verbatim run into the decode buffer with
    /// its one contiguity segment, switching the region to assembled mode;
    /// a no-op once assembled.
    /// </summary>
    private void MaterializeRegion()
    {
        if(!RegionClean)
        {
            return;
        }

        RegionClean = false;
        if(RegionAliasLength == 0)
        {
            return;
        }

        AddTextSegment(decodedStart: 0, RegionAliasStart);
        ReadOnlySpan<byte> run = Document.Slice(RegionAliasStart, RegionAliasLength);
        for(int index = 0; index < run.Length; index++)
        {
            AppendDecodedByte(run[index]);
        }
    }

    /// <summary>Appends verbatim input bytes to the current text region, extending the clean run when still aliasing.</summary>
    private void AppendTextVerbatim(int sourcePosition, int length, bool isWhitespace)
    {
        if(RegionFirstContent < 0)
        {
            RegionFirstContent = sourcePosition;
        }

        if(!isWhitespace)
        {
            TextWhitespaceValue = false;
        }

        if(RegionClean)
        {
            if(RegionAliasLength == 0)
            {
                RegionAliasStart = sourcePosition;
            }

            RegionAliasLength += length;

            return;
        }

        for(int index = 0; index < length; index++)
        {
            AddTextSegmentIfDiscontinuous(DecodeLength, sourcePosition + index);
            AppendDecodedByte(Document[sourcePosition + index]);
        }
    }

    /// <summary>Appends the single normalized line feed a line end contributes, anchored at the carriage return.</summary>
    private void AppendTextLineEnd(int carriageReturnPosition)
    {
        if(RegionFirstContent < 0)
        {
            RegionFirstContent = carriageReturnPosition;
        }

        MaterializeRegion();
        AddTextSegmentIfDiscontinuous(DecodeLength, carriageReturnPosition);
        AppendDecodedByte(0xA);
    }

    /// <summary>Appends a reference's decoded scalar to the text region as inert data, anchored at the ampersand.</summary>
    private void AppendTextScalar(int scalar, int ampersandPosition)
    {
        if(RegionFirstContent < 0)
        {
            RegionFirstContent = ampersandPosition;
        }

        if(scalar is not (0x20 or 0x9 or 0xA or 0xD))
        {
            TextWhitespaceValue = false;
        }

        MaterializeRegion();
        AddTextSegmentIfDiscontinuous(DecodeLength, ampersandPosition);
        AppendScalarUtf8(scalar);
    }

    /// <summary>Records a decoded-to-document segment unconditionally.</summary>
    private void AddTextSegment(int decodedStart, int documentStart)
    {
        XmlFragmentScratch.GrowingArray<XmlFragmentScratch.TextSegment> segments = Scratch!.TextSegments;
        if(SegmentCount >= segments.Capacity)
        {
            segments.GrowPreservingContents(SegmentCount + 1, SegmentCount);
        }

        segments.Span[SegmentCount] = new XmlFragmentScratch.TextSegment(decodedStart, documentStart);
        SegmentCount++;
    }

    /// <summary>Records a decoded-to-document segment when the mapping distance changed.</summary>
    private void AddTextSegmentIfDiscontinuous(int decodedStart, int documentStart)
    {
        if(SegmentCount > 0)
        {
            XmlFragmentScratch.TextSegment last = Scratch!.TextSegments.Span[SegmentCount - 1];
            if(documentStart - last.DocumentStart == decodedStart - last.DecodedStart)
            {
                return;
            }
        }

        AddTextSegment(decodedStart, documentStart);
    }

    /// <summary>Appends one byte to the decode buffer, growing it while preserving contents.</summary>
    private void AppendDecodedByte(byte value)
    {
        XmlFragmentScratch.GrowingArray<byte> buffer = Scratch!.DecodeBuffer;
        if(DecodeLength >= buffer.Capacity)
        {
            buffer.GrowPreservingContents(DecodeLength + 1, DecodeLength);
        }

        buffer.Span[DecodeLength] = value;
        DecodeLength++;
    }

    /// <summary>Appends a scalar's UTF-8 encoding to the decode buffer.</summary>
    private void AppendScalarUtf8(int scalar)
    {
        if(scalar < 0x80)
        {
            AppendDecodedByte((byte)scalar);

            return;
        }

        if(scalar < 0x800)
        {
            AppendDecodedByte((byte)(0xC0 | (scalar >> 6)));
            AppendDecodedByte((byte)(0x80 | (scalar & 0x3F)));

            return;
        }

        if(scalar < 0x10000)
        {
            AppendDecodedByte((byte)(0xE0 | (scalar >> 12)));
            AppendDecodedByte((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            AppendDecodedByte((byte)(0x80 | (scalar & 0x3F)));

            return;
        }

        AppendDecodedByte((byte)(0xF0 | (scalar >> 18)));
        AppendDecodedByte((byte)(0x80 | ((scalar >> 12) & 0x3F)));
        AppendDecodedByte((byte)(0x80 | ((scalar >> 6) & 0x3F)));
        AppendDecodedByte((byte)(0x80 | (scalar & 0x3F)));
    }

    /// <summary>
    /// Consumes one reference at the ampersand: a decimal or hexadecimal
    /// character reference adjudicated by value with overflow detected, or
    /// one of the five predefined entities — the entire declared set, since
    /// no document type declaration can exist. Every in-reference offense
    /// anchors at the ampersand; input ending inside the reference is
    /// truncation at the input length.
    /// </summary>
    private bool TryScanReference(out int scalar)
    {
        scalar = 0;
        int ampersand = Position;
        Position++;
        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        if(Document[Position] == (byte)'#')
        {
            Position++;
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            bool hexadecimal = Document[Position] == (byte)'x';
            if(hexadecimal)
            {
                Position++;
            }

            int digits = 0;
            long value = 0;
            while(true)
            {
                if(Position >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                byte digit = Document[Position];
                if(digit == (byte)';')
                {
                    break;
                }

                bool acceptable = hexadecimal ? XmlLexicon.IsHexDigit(digit) : XmlLexicon.IsDigit(digit);
                if(!acceptable)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, ampersand);
                }

                value = (value * (hexadecimal ? 16 : 10)) + (hexadecimal ? XmlLexicon.HexDigitValue(digit) : digit - (byte)'0');

                //Clamp above the scalar ceiling so an arbitrarily long digit
                //run stays an ordinary out-of-range refusal instead of
                //wrapping around.
                if(value > 0x110000)
                {
                    value = 0x110000;
                }

                digits++;
                Position++;
            }

            if(digits == 0)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, ampersand);
            }

            Position++;
            if(value > 0x10FFFF || !XmlLexicon.IsCharacter((int)value))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, ampersand);
            }

            scalar = (int)value;

            return true;
        }

        int nameStart = Position;
        while(true)
        {
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == (byte)';')
            {
                break;
            }

            //Entity names are lowercase ASCII and none exceeds four bytes,
            //so anything longer or stranger can extend no reference.
            if(value is not (>= (byte)'a' and <= (byte)'z') || Position - nameStart >= 4)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, ampersand);
            }

            Position++;
        }

        ReadOnlySpan<byte> name = Document[nameStart..Position];
        Position++;
        if(name.SequenceEqual(XmlVocabulary.AmpersandEntityName))
        {
            scalar = XmlVocabulary.AmpersandReplacement[0];
        }
        else if(name.SequenceEqual(XmlVocabulary.LessThanEntityName))
        {
            scalar = XmlVocabulary.LessThanReplacement[0];
        }
        else if(name.SequenceEqual(XmlVocabulary.GreaterThanEntityName))
        {
            scalar = XmlVocabulary.GreaterThanReplacement[0];
        }
        else if(name.SequenceEqual(XmlVocabulary.ApostropheEntityName))
        {
            scalar = XmlVocabulary.ApostropheReplacement[0];
        }
        else if(name.SequenceEqual(XmlVocabulary.QuotationEntityName))
        {
            scalar = XmlVocabulary.QuotationReplacement[0];
        }
        else
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, ampersand);
        }

        return true;
    }

    /// <summary>
    /// Consumes a start tag: the element name, the phased attribute pass —
    /// per-declaration offenses adjudicate immediately in document order,
    /// binding-dependent resolution and expanded-name duplicates adjudicate
    /// after the attribute list parses cleanly — the frame push, and the
    /// token state. The transport depth cap refuses before anything is
    /// consumed.
    /// </summary>
    private bool TryScanStartTag()
    {
        int open = Position;
        if(Depth + 1 > GeometryCodecText.MaximumTransportDepth)
        {
            return Fail(GeometryCodecRefusalKind.NestingTooDeep, open);
        }

        EnsureScratch();
        DecodeLength = 0;
        int bindingMark = BindingCount;
        int arenaMark = ArenaLength;
        Position++;
        if(!TryScanName(out int qualifiedStart, out int qualifiedLength, out int prefixLength, out int localStart, out int localLength))
        {
            return false;
        }

        if(prefixLength == XmlVocabulary.XmlnsName.Length
            && Document.Slice(qualifiedStart, prefixLength).SequenceEqual(XmlVocabulary.XmlnsName))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, qualifiedStart);
        }

        int attributeCount = 0;
        int tagClose;
        bool empty = false;
        while(true)
        {
            int whitespaceCount = SkipWhitespace();
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == (byte)'>')
            {
                tagClose = Position;
                Position++;

                break;
            }

            if(value == (byte)'/')
            {
                if(Position + 1 >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                if(Document[Position + 1] != (byte)'>')
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position + 1);
                }

                tagClose = Position + 1;
                empty = true;
                Position += 2;

                break;
            }

            if(whitespaceCount == 0)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(!TryScanAttribute(bindingMark, ref attributeCount))
            {
                return false;
            }
        }

        if(!TryResolveTag(qualifiedStart, prefixLength, localStart, localLength, attributeCount, tagClose))
        {
            return false;
        }

        Scratch!.ElementFrames.Span[Depth] = new XmlFragmentScratch.ElementFrame(
            qualifiedStart,
            qualifiedLength,
            localStart,
            localLength,
            ElementNamespaceStore,
            ElementNamespaceStart,
            ElementNamespaceLength,
            bindingMark,
            arenaMark,
            open);
        Depth++;
        RootSeen = true;
        CurrentKind = XmlFragmentTokenKind.ElementOpen;
        CurrentTokenStart = open;
        CurrentStartTagClose = tagClose;
        ElementLocalNameStart = localStart;
        ElementLocalNameLength = localLength;
        CurrentAttributeCount = attributeCount;
        PendingEmptyClose = empty;
        HasToken = true;

        return true;
    }

    /// <summary>
    /// Consumes one attribute or namespace declaration inside a start tag:
    /// the name, the equals sign, the quoted normalized value, the
    /// per-declaration constraint checks, and the immediate duplicate
    /// checks. Declarations bind; ordinary attributes join the table with
    /// their namespace unresolved.
    /// </summary>
    private bool TryScanAttribute(int bindingMark, ref int attributeCount)
    {
        if(!TryScanName(out int nameStart, out _, out int prefixLength, out int localStart, out int localLength))
        {
            return false;
        }

        bool defaultDeclaration = prefixLength == 0
            && localLength == XmlVocabulary.XmlnsName.Length
            && Document.Slice(localStart, localLength).SequenceEqual(XmlVocabulary.XmlnsName);
        bool prefixedDeclaration = prefixLength == XmlVocabulary.XmlnsName.Length
            && Document.Slice(nameStart, prefixLength).SequenceEqual(XmlVocabulary.XmlnsName);
        if(prefixedDeclaration
            && localLength == XmlVocabulary.XmlnsName.Length
            && Document.Slice(localStart, localLength).SequenceEqual(XmlVocabulary.XmlnsName))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, nameStart);
        }

        if(!TryParseEq())
        {
            return false;
        }

        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        byte quote = Document[Position];
        if(quote is not ((byte)'"' or (byte)'\''))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        Position++;
        int valueOffset = Position;
        if(!TryScanAttributeValue(quote, valueOffset, out XmlFragmentScratch.ByteStore valueStore, out int valueStart, out int valueLength, out int closingQuote))
        {
            return false;
        }

        if(defaultDeclaration || prefixedDeclaration)
        {
            Span<XmlFragmentScratch.NamespaceBinding> bindings = Scratch!.Bindings.Span;
            for(int i = bindingMark; i < BindingCount; i++)
            {
                XmlFragmentScratch.NamespaceBinding existing = bindings[i];
                bool samePrefix = prefixedDeclaration
                    ? existing.PrefixLength == localLength
                        && existing.PrefixLength > 0
                        && Document.Slice(existing.PrefixStart, existing.PrefixLength).SequenceEqual(Document.Slice(localStart, localLength))
                    : existing.PrefixLength == 0;
                if(samePrefix)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, nameStart);
                }
            }

            ReadOnlySpan<byte> uri = SliceStore(valueStore, valueStart, valueLength);
            if(prefixedDeclaration)
            {
                if(valueLength == 0)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, closingQuote);
                }

                bool declaresXmlPrefix = localLength == XmlVocabulary.XmlPrefix.Length
                    && Document.Slice(localStart, localLength).SequenceEqual(XmlVocabulary.XmlPrefix);
                if(declaresXmlPrefix)
                {
                    if(!uri.SequenceEqual(XmlVocabulary.XmlNamespace))
                    {
                        return Fail(GeometryCodecRefusalKind.MalformedDocument, valueOffset);
                    }
                }
                else if(uri.SequenceEqual(XmlVocabulary.XmlNamespace) || uri.SequenceEqual(XmlVocabulary.XmlnsNamespace))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, valueOffset);
                }
            }
            else if(uri.SequenceEqual(XmlVocabulary.XmlNamespace) || uri.SequenceEqual(XmlVocabulary.XmlnsNamespace))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, valueOffset);
            }

            XmlFragmentScratch.ByteStore uriStore = valueStore;
            int uriStart = valueStart;
            if(valueStore == XmlFragmentScratch.ByteStore.Decode)
            {
                //The decode buffer is per-token scratch but a binding lives
                //for its whole element scope, so a decoded value's bytes are
                //copied into the arena the bindings own.
                uriStore = XmlFragmentScratch.ByteStore.Arena;
                uriStart = AppendArena(uri);
            }

            AppendBinding(new XmlFragmentScratch.NamespaceBinding(
                prefixedDeclaration ? localStart : 0,
                prefixedDeclaration ? localLength : 0,
                uriStore,
                uriStart,
                valueLength));

            return true;
        }

        Span<XmlFragmentScratch.AttributeEntry> entries = Scratch!.Attributes.Span;
        ReadOnlySpan<byte> qualifiedName = Document.Slice(nameStart, prefixLength == 0 ? localLength : prefixLength + 1 + localLength);
        for(int i = 0; i < attributeCount; i++)
        {
            XmlFragmentScratch.AttributeEntry existing = entries[i];
            int existingQualifiedLength = existing.PrefixLength == 0
                ? existing.LocalNameLength
                : existing.PrefixLength + 1 + existing.LocalNameLength;
            if(Document.Slice(existing.NameOffset, existingQualifiedLength).SequenceEqual(qualifiedName))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, nameStart);
            }
        }

        AppendAttribute(new XmlFragmentScratch.AttributeEntry(
            nameStart,
            prefixLength,
            localStart,
            localLength,
            valueStore,
            valueStart,
            valueLength,
            valueOffset,
            XmlFragmentScratch.ByteStore.Input,
            NamespaceStart: 0,
            NamespaceLength: 0), ref attributeCount);

        return true;
    }

    /// <summary>
    /// Scans a quoted attribute value applying the single normalization
    /// pass: line ends normalize first, literal whitespace becomes a space,
    /// reference-produced characters append verbatim as inert data, a raw
    /// left angle bracket refuses, and characters validate. A value needing
    /// no work aliases the input; a normalized one lands in the decode
    /// buffer.
    /// </summary>
    private bool TryScanAttributeValue(
        byte quote,
        int valueOffset,
        out XmlFragmentScratch.ByteStore store,
        out int start,
        out int length,
        out int closingQuote)
    {
        store = XmlFragmentScratch.ByteStore.Input;
        start = valueOffset;
        length = 0;
        closingQuote = 0;
        bool clean = true;
        int cleanLength = 0;
        int decodeStart = DecodeLength;
        while(true)
        {
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte value = Document[Position];
            if(value == quote)
            {
                closingQuote = Position;
                Position++;

                break;
            }

            if(value == (byte)'<')
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(value == (byte)'&')
            {
                MaterializeValue(valueOffset, ref clean, cleanLength);
                if(!TryScanReference(out int scalar))
                {
                    return false;
                }

                AppendScalarUtf8(scalar);

                continue;
            }

            if(value == 0xD)
            {
                MaterializeValue(valueOffset, ref clean, cleanLength);
                AppendDecodedByte((byte)' ');
                Position += Position + 1 < Document.Length && Document[Position + 1] == 0xA ? 2 : 1;

                continue;
            }

            if(value is 0xA or 0x9)
            {
                MaterializeValue(valueOffset, ref clean, cleanLength);
                AppendDecodedByte((byte)' ');
                Position++;

                continue;
            }

            if(value < 0x20)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            if(value >= 0x80)
            {
                if(!TryDecodeScalarAt(Position, out int scalar, out int scalarLength))
                {
                    return false;
                }

                if(!XmlLexicon.IsCharacter(scalar))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                if(clean)
                {
                    cleanLength += scalarLength;
                }
                else
                {
                    for(int index = 0; index < scalarLength; index++)
                    {
                        AppendDecodedByte(Document[Position + index]);
                    }
                }

                Position += scalarLength;

                continue;
            }

            if(clean)
            {
                cleanLength++;
            }
            else
            {
                AppendDecodedByte(value);
            }

            Position++;
        }

        if(clean)
        {
            length = cleanLength;
        }
        else
        {
            store = XmlFragmentScratch.ByteStore.Decode;
            start = decodeStart;
            length = DecodeLength - decodeStart;
        }

        return true;
    }

    /// <summary>Copies a value's clean prefix into the decode buffer on the first normalization event; a no-op once copied.</summary>
    private void MaterializeValue(int valueOffset, ref bool clean, int cleanLength)
    {
        if(!clean)
        {
            return;
        }

        clean = false;
        ReadOnlySpan<byte> prefix = Document.Slice(valueOffset, cleanLength);
        for(int index = 0; index < prefix.Length; index++)
        {
            AppendDecodedByte(prefix[index]);
        }
    }

    /// <summary>
    /// Resolves the finished tag's binding-dependent facts in document
    /// order: the element's namespace, every attribute's namespace, and the
    /// expanded-name duplicate rule. An undeclared prefix refuses at the
    /// byte closing the start tag, where the absence became final.
    /// </summary>
    private bool TryResolveTag(int qualifiedStart, int prefixLength, int localStart, int localLength, int attributeCount, int tagClose)
    {
        if(prefixLength == 0)
        {
            ResolveDefaultNamespace();
        }
        else if(IsXmlPrefix(qualifiedStart, prefixLength))
        {
            ElementNamespaceStore = XmlFragmentScratch.ByteStore.BuiltinXml;
            ElementNamespaceStart = 0;
            ElementNamespaceLength = XmlVocabulary.XmlNamespace.Length;
        }
        else
        {
            if(!TryResolvePrefix(qualifiedStart, prefixLength, out XmlFragmentScratch.ByteStore store, out int uriStart, out int uriLength))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, tagClose);
            }

            ElementNamespaceStore = store;
            ElementNamespaceStart = uriStart;
            ElementNamespaceLength = uriLength;
        }

        Span<XmlFragmentScratch.AttributeEntry> entries = Scratch!.Attributes.Span;
        for(int i = 0; i < attributeCount; i++)
        {
            XmlFragmentScratch.AttributeEntry entry = entries[i];
            if(entry.PrefixLength == 0)
            {
                continue;
            }

            if(IsXmlPrefix(entry.NameOffset, entry.PrefixLength))
            {
                entries[i] = entry with
                {
                    NamespaceStore = XmlFragmentScratch.ByteStore.BuiltinXml,
                    NamespaceStart = 0,
                    NamespaceLength = XmlVocabulary.XmlNamespace.Length
                };

                continue;
            }

            if(!TryResolvePrefix(entry.NameOffset, entry.PrefixLength, out XmlFragmentScratch.ByteStore store, out int uriStart, out int uriLength))
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, tagClose);
            }

            entries[i] = entry with
            {
                NamespaceStore = store,
                NamespaceStart = uriStart,
                NamespaceLength = uriLength
            };
        }

        for(int j = 1; j < attributeCount; j++)
        {
            XmlFragmentScratch.AttributeEntry later = entries[j];
            ReadOnlySpan<byte> laterLocal = Document.Slice(later.LocalNameStart, later.LocalNameLength);
            ReadOnlySpan<byte> laterNamespace = SliceStore(later.NamespaceStore, later.NamespaceStart, later.NamespaceLength);
            for(int i = 0; i < j; i++)
            {
                XmlFragmentScratch.AttributeEntry earlier = entries[i];
                if(!Document.Slice(earlier.LocalNameStart, earlier.LocalNameLength).SequenceEqual(laterLocal))
                {
                    continue;
                }

                if(SliceStore(earlier.NamespaceStore, earlier.NamespaceStart, earlier.NamespaceLength).SequenceEqual(laterNamespace))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, later.NameOffset);
                }
            }
        }

        return true;
    }

    /// <summary>Resolves the element's default-namespace state from the innermost default declaration, when one is in scope.</summary>
    private void ResolveDefaultNamespace()
    {
        ElementNamespaceStore = XmlFragmentScratch.ByteStore.Input;
        ElementNamespaceStart = 0;
        ElementNamespaceLength = 0;
        Span<XmlFragmentScratch.NamespaceBinding> bindings = Scratch!.Bindings.Span;
        for(int i = BindingCount - 1; i >= 0; i--)
        {
            XmlFragmentScratch.NamespaceBinding binding = bindings[i];
            if(binding.PrefixLength != 0)
            {
                continue;
            }

            //A zero-length value is the un-declaration: the element is in no
            //namespace, which the zero-length default state already says.
            if(binding.UriLength > 0)
            {
                ElementNamespaceStore = binding.UriStore;
                ElementNamespaceStart = binding.UriStart;
                ElementNamespaceLength = binding.UriLength;
            }

            return;
        }
    }

    /// <summary>True when the prefix at the given extent is the reserved xml prefix.</summary>
    private readonly bool IsXmlPrefix(int prefixStart, int prefixLength) =>
        prefixLength == XmlVocabulary.XmlPrefix.Length
            && Document.Slice(prefixStart, prefixLength).SequenceEqual(XmlVocabulary.XmlPrefix);

    /// <summary>Finds the innermost binding for a prefix; false means the prefix is undeclared.</summary>
    private readonly bool TryResolvePrefix(int prefixStart, int prefixLength, out XmlFragmentScratch.ByteStore store, out int uriStart, out int uriLength)
    {
        store = XmlFragmentScratch.ByteStore.Input;
        uriStart = 0;
        uriLength = 0;
        ReadOnlySpan<byte> prefix = Document.Slice(prefixStart, prefixLength);
        Span<XmlFragmentScratch.NamespaceBinding> bindings = Scratch!.Bindings.Span;
        for(int i = BindingCount - 1; i >= 0; i--)
        {
            XmlFragmentScratch.NamespaceBinding binding = bindings[i];
            if(binding.PrefixLength != prefixLength)
            {
                continue;
            }

            if(Document.Slice(binding.PrefixStart, binding.PrefixLength).SequenceEqual(prefix))
            {
                store = binding.UriStore;
                uriStart = binding.UriStart;
                uriLength = binding.UriLength;

                return true;
            }
        }

        return false;
    }

    /// <summary>Appends a binding to the in-scope stack, growing it while preserving contents.</summary>
    private void AppendBinding(XmlFragmentScratch.NamespaceBinding binding)
    {
        XmlFragmentScratch.GrowingArray<XmlFragmentScratch.NamespaceBinding> bindings = Scratch!.Bindings;
        if(BindingCount >= bindings.Capacity)
        {
            bindings.GrowPreservingContents(BindingCount + 1, BindingCount);
        }

        bindings.Span[BindingCount] = binding;
        BindingCount++;
    }

    /// <summary>Copies bytes into the binding arena and reports where they landed.</summary>
    private int AppendArena(ReadOnlySpan<byte> bytes)
    {
        XmlFragmentScratch.GrowingArray<byte> arena = Scratch!.Arena;
        if(ArenaLength + bytes.Length > arena.Capacity)
        {
            arena.GrowPreservingContents(ArenaLength + bytes.Length, ArenaLength);
        }

        int start = ArenaLength;
        bytes.CopyTo(arena.Span[ArenaLength..]);
        ArenaLength += bytes.Length;

        return start;
    }

    /// <summary>Appends an attribute entry to the tag's table, growing it while preserving contents.</summary>
    private void AppendAttribute(XmlFragmentScratch.AttributeEntry entry, ref int attributeCount)
    {
        XmlFragmentScratch.GrowingArray<XmlFragmentScratch.AttributeEntry> attributes = Scratch!.Attributes;
        if(attributeCount >= attributes.Capacity)
        {
            attributes.GrowPreservingContents(attributeCount + 1, attributeCount);
        }

        attributes.Span[attributeCount] = entry;
        attributeCount++;
    }

    /// <summary>
    /// Consumes an end tag, which must repeat the open element's qualified
    /// name byte for byte — the refusal anchors at the first byte where the
    /// names cease to match. The scope pops by truncation without writing,
    /// after resolution, so the close token's spans survive until the next
    /// read.
    /// </summary>
    private bool TryScanEndTag()
    {
        int open = Position;
        Position += 2;
        XmlFragmentScratch.ElementFrame frame = Scratch!.ElementFrames.Span[Depth - 1];
        for(int i = 0; i < frame.QualifiedNameLength; i++)
        {
            if(Position >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            if(Document[Position] != Document[frame.QualifiedNameStart + i])
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
            }

            Position++;
        }

        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        //A byte that is neither whitespace nor the closing bracket means the
        //end tag's name continued past the start tag's — the byte where the
        //names ceased to match.
        if(!XmlLexicon.IsWhitespace(Document[Position]) && Document[Position] != (byte)'>')
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        SkipWhitespace();
        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        if(Document[Position] != (byte)'>')
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        Position++;
        EmitClose(in frame, open);

        return true;
    }

    /// <summary>Delivers the synthetic close of an empty-element tag, anchored at the tag's opening angle bracket.</summary>
    private void EmitPendingClose()
    {
        PendingEmptyClose = false;
        XmlFragmentScratch.ElementFrame frame = Scratch!.ElementFrames.Span[Depth - 1];
        EmitClose(in frame, frame.TagOpenOffset);
    }

    /// <summary>Sets the close token from the frame, then pops the scope by truncating the binding stack and arena to the frame's marks.</summary>
    private void EmitClose(in XmlFragmentScratch.ElementFrame frame, int tokenStart)
    {
        ElementLocalNameStart = frame.LocalNameStart;
        ElementLocalNameLength = frame.LocalNameLength;
        ElementNamespaceStore = frame.NamespaceStore;
        ElementNamespaceStart = frame.NamespaceStart;
        ElementNamespaceLength = frame.NamespaceLength;
        BindingCount = frame.BindingMark;
        ArenaLength = frame.ArenaMark;
        Depth--;
        CurrentKind = XmlFragmentTokenKind.ElementClose;
        CurrentTokenStart = tokenStart;
        CurrentAttributeCount = 0;
        HasToken = true;
    }

    /// <summary>
    /// Scans a namespace-constrained name at the cursor: a name-start
    /// scalar, name characters, and at most one colon with a name-start
    /// scalar after it. Names alias the input; no decoding exists here
    /// because references are not recognized inside names.
    /// </summary>
    private bool TryScanName(out int nameStart, out int nameLength, out int prefixLength, out int localStart, out int localLength)
    {
        nameStart = Position;
        nameLength = 0;
        prefixLength = 0;
        localStart = Position;
        localLength = 0;
        if(Position >= Document.Length)
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
        }

        if(!TryDecodeScalarAt(Position, out int scalar, out int scalarLength))
        {
            return false;
        }

        if(!XmlLexicon.IsNameStart(scalar))
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
        }

        Position += scalarLength;
        int colon = -1;
        while(Position < Document.Length)
        {
            byte value = Document[Position];
            if(value == (byte)':')
            {
                if(colon >= 0)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                colon = Position;
                Position++;
                if(Position >= Document.Length)
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
                }

                if(!TryDecodeScalarAt(Position, out scalar, out scalarLength))
                {
                    return false;
                }

                if(!XmlLexicon.IsNameStart(scalar))
                {
                    return Fail(GeometryCodecRefusalKind.MalformedDocument, Position);
                }

                Position += scalarLength;

                continue;
            }

            if(value < 0x80)
            {
                if(!XmlLexicon.IsNameCharacter(value))
                {
                    break;
                }

                Position++;

                continue;
            }

            if(!TryDecodeScalarAt(Position, out scalar, out scalarLength))
            {
                return false;
            }

            if(!XmlLexicon.IsNameCharacter(scalar))
            {
                break;
            }

            Position += scalarLength;
        }

        nameLength = Position - nameStart;
        if(colon >= 0)
        {
            prefixLength = colon - nameStart;
            localStart = colon + 1;
            localLength = Position - localStart;
        }
        else
        {
            localStart = nameStart;
            localLength = nameLength;
        }

        return true;
    }

    /// <summary>
    /// Decodes one UTF-8 scalar at a position without moving the cursor,
    /// enforcing the exact continuation windows so overlong forms and
    /// encoded surrogates refuse at the first byte that cannot extend a
    /// valid sequence; a sequence cut by end of input is truncation at the
    /// input length.
    /// </summary>
    private bool TryDecodeScalarAt(int position, out int scalar, out int length)
    {
        scalar = 0;
        length = 0;
        byte lead = Document[position];
        if(lead < 0x80)
        {
            scalar = lead;
            length = 1;

            return true;
        }

        int continuations;
        byte firstLow = 0x80;
        byte firstHigh = 0xBF;
        if(lead is >= 0xC2 and <= 0xDF)
        {
            continuations = 1;
            scalar = lead & 0x1F;
        }
        else if(lead == 0xE0)
        {
            continuations = 2;
            scalar = lead & 0x0F;
            firstLow = 0xA0;
        }
        else if(lead is (>= 0xE1 and <= 0xEC) or 0xEE or 0xEF)
        {
            continuations = 2;
            scalar = lead & 0x0F;
        }
        else if(lead == 0xED)
        {
            continuations = 2;
            scalar = lead & 0x0F;
            firstHigh = 0x9F;
        }
        else if(lead == 0xF0)
        {
            continuations = 3;
            scalar = lead & 0x07;
            firstLow = 0x90;
        }
        else if(lead is >= 0xF1 and <= 0xF3)
        {
            continuations = 3;
            scalar = lead & 0x07;
        }
        else if(lead == 0xF4)
        {
            continuations = 3;
            scalar = lead & 0x07;
            firstHigh = 0x8F;
        }
        else
        {
            return Fail(GeometryCodecRefusalKind.MalformedDocument, position);
        }

        for(int i = 1; i <= continuations; i++)
        {
            int continuationPosition = position + i;
            if(continuationPosition >= Document.Length)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, Document.Length);
            }

            byte continuation = Document[continuationPosition];
            byte low = i == 1 ? firstLow : (byte)0x80;
            byte high = i == 1 ? firstHigh : (byte)0xBF;
            if(continuation < low || continuation > high)
            {
                return Fail(GeometryCodecRefusalKind.MalformedDocument, continuationPosition);
            }

            scalar = (scalar << 6) | (continuation & 0x3F);
        }

        length = continuations + 1;

        return true;
    }

    /// <summary>Consumes the whitespace run at the cursor and reports how many bytes it held.</summary>
    private int SkipWhitespace()
    {
        int count = 0;
        while(Position < Document.Length && XmlLexicon.IsWhitespace(Document[Position]))
        {
            Position++;
            count++;
        }

        return count;
    }

    /// <summary>Allocates the scratch on the first read that needs it.</summary>
    private void EnsureScratch()
    {
        Scratch ??= XmlFragmentScratch.Create();
    }

    /// <summary>
    /// Re-slices an extent through its current store at call time. An arena
    /// extent may lie beyond the arena's truncated logical length after a
    /// scope pop; the bytes are intact until a later read appends over
    /// them, which is exactly the lifetime the exposed spans promise.
    /// </summary>
    private readonly ReadOnlySpan<byte> SliceStore(XmlFragmentScratch.ByteStore store, int start, int length) =>
        store switch
        {
            XmlFragmentScratch.ByteStore.Input => Document.Slice(start, length),
            XmlFragmentScratch.ByteStore.Arena => ((ReadOnlySpan<byte>)Scratch!.Arena.Span).Slice(start, length),
            XmlFragmentScratch.ByteStore.Decode => ((ReadOnlySpan<byte>)Scratch!.DecodeBuffer.Span).Slice(start, length),
            _ => XmlVocabulary.XmlNamespace,
        };

    /// <summary>Fails loud when no token is current: before the first read, after a refusal, after exhaustion, and on the defaulted value.</summary>
    private readonly void EnsureCurrentToken()
    {
        ObjectDisposedException.ThrowIf(Disposed, typeof(XmlFragmentScanner));
        if(!HasToken)
        {
            throw new InvalidOperationException("The scanner has no current token.");
        }
    }

    /// <summary>Fails loud when the current token is not an element token.</summary>
    private readonly void EnsureElementToken()
    {
        EnsureCurrentToken();
        if(CurrentKind == XmlFragmentTokenKind.Text)
        {
            throw new InvalidOperationException("The current token is not an element token.");
        }
    }

    /// <summary>Fails loud when the current token is not a start tag; attribute state is legal only there, in both tag spellings.</summary>
    private readonly void EnsureOpenToken()
    {
        EnsureCurrentToken();
        if(CurrentKind != XmlFragmentTokenKind.ElementOpen)
        {
            throw new InvalidOperationException("The current token is not a start tag.");
        }
    }

    /// <summary>Fails loud when the current token is not character data.</summary>
    private readonly void EnsureTextToken()
    {
        EnsureCurrentToken();
        if(CurrentKind != XmlFragmentTokenKind.Text)
        {
            throw new InvalidOperationException("The current token is not character data.");
        }
    }

    /// <summary>Bounds-checks an attribute index against the current start tag and returns its entry.</summary>
    private readonly XmlFragmentScratch.AttributeEntry AttributeAt(int index)
    {
        EnsureOpenToken();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, CurrentAttributeCount);

        return Scratch!.Attributes.Span[index];
    }
}
