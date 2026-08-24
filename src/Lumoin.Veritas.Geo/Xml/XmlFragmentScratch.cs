using System;

using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The working state of one <see cref="XmlFragmentScanner"/>: the
/// element stack sized to the transport depth cap, the namespace-binding
/// stack and the arena that owns decoded declaration values, the per-tag
/// attribute table, the per-token decode buffer, and the text segment list
/// that maps decoded positions back to document offsets. One sealed holder
/// owns every buffer, so allocation stays in one place; the scanner borrows
/// it and disposes it exactly once. All cross-phase state inside these
/// buffers is stored as integer extents — never as captured spans — because
/// the growing buffers replace their arrays on growth.
/// </summary>
internal sealed class XmlFragmentScratch: IDisposable
{
    /// <summary>Where an extent's bytes live, so accessors can re-slice the right store at call time.</summary>
    public enum ByteStore
    {
        /// <summary>The extent indexes the input document span.</summary>
        Input = 0,

        /// <summary>The extent indexes the binding arena, which owns decoded declaration values.</summary>
        Arena = 1,

        /// <summary>The extent indexes the per-token decode buffer.</summary>
        Decode = 2,

        /// <summary>The bytes are the permanently bound XML namespace name; the extent is ignored.</summary>
        BuiltinXml = 3,
    }

    /// <summary>
    /// One open element: its qualified and local name extents into the input,
    /// its resolved namespace extent, the binding and arena marks its close
    /// truncates back to, and the offset of its opening angle bracket.
    /// </summary>
    public readonly record struct ElementFrame(
        int QualifiedNameStart,
        int QualifiedNameLength,
        int LocalNameStart,
        int LocalNameLength,
        ByteStore NamespaceStore,
        int NamespaceStart,
        int NamespaceLength,
        int BindingMark,
        int ArenaMark,
        int TagOpenOffset);

    /// <summary>
    /// One in-scope namespace declaration: the prefix extent into the input
    /// (length zero for the default namespace), and the bound value's extent
    /// in its store. A bound value of length zero is the default namespace's
    /// un-declaration; a prefixed binding can never carry one because an
    /// empty prefixed declaration refuses at its declaration.
    /// </summary>
    public readonly record struct NamespaceBinding(
        int PrefixStart,
        int PrefixLength,
        ByteStore UriStore,
        int UriStart,
        int UriLength);

    /// <summary>
    /// One attribute of the current start tag: the offset of its qualified
    /// name as written, the name's prefix length and local extent into the
    /// input, the normalized value's extent in its store, the offset of the
    /// first byte inside the value's quotes, and the resolved namespace
    /// extent. Namespace declarations never appear here — the scanner
    /// consumes them as bindings.
    /// </summary>
    public readonly record struct AttributeEntry(
        int NameOffset,
        int PrefixLength,
        int LocalNameStart,
        int LocalNameLength,
        ByteStore ValueStore,
        int ValueStart,
        int ValueLength,
        int ValueOffset,
        ByteStore NamespaceStore,
        int NamespaceStart,
        int NamespaceLength);

    /// <summary>
    /// One contiguity segment of the current text token: from this decoded
    /// position onward, until the next segment begins, decoded bytes map to
    /// document bytes at a constant distance from
    /// <paramref name="DocumentStart"/>.
    /// </summary>
    public readonly record struct TextSegment(int DecodedStart, int DocumentStart);

    /// <summary>
    /// An owned heap array that grows on demand for append-style writers: a
    /// growth carries the live prefix into the replacement, so the elements
    /// already produced survive the swap, and every span taken before a growth
    /// is invalid after it.
    /// </summary>
    /// <typeparam name="T">The element type of the buffer.</typeparam>
    public sealed class GrowingArray<T>
    {
        /// <summary>The array holding the elements; a growth replaces it.</summary>
        private T[] Elements { get; set; }

        /// <summary>The number of elements the buffer holds before it must grow.</summary>
        public int Capacity => Elements.Length;

        /// <summary>The whole buffer at its current capacity.</summary>
        public Span<T> Span => Elements;

        /// <summary>Allocates the buffer at its initial capacity.</summary>
        /// <param name="initialCapacity">The initial capacity in elements.</param>
        public GrowingArray(int initialCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            Elements = new T[initialCapacity];
        }

        /// <summary>
        /// Grows the buffer to at least <paramref name="required"/> elements
        /// while preserving the first <paramref name="contentLength"/> of them;
        /// a capacity that already satisfies the requirement leaves the array
        /// untouched. The new capacity is the larger of the requirement and
        /// twice the current one, widened before the doubling can wrap and
        /// capped at the maximum array length.
        /// </summary>
        /// <param name="required">The minimum required capacity in elements.</param>
        /// <param name="contentLength">The live prefix that must survive the growth; never beyond the current capacity.</param>
        public void GrowPreservingContents(int required, int contentLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(required);
            ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(contentLength, Elements.Length);
            if(Elements.Length >= required)
            {
                return;
            }

            int doubled = (int)Math.Min((long)Elements.Length * 2, Array.MaxLength);
            T[] replacement = new T[Math.Max(required, doubled)];

            //The live prefix moves into the replacement before the old array is released.
            Elements.AsSpan(0, contentLength).CopyTo(replacement);
            Elements = replacement;
        }
    }

    /// <summary>The element stack, one exact-size heap array at the transport depth cap.</summary>
    public Memory<ElementFrame> ElementFrames { get; }

    /// <summary>The namespace-binding stack, popped by mark at element close.</summary>
    public GrowingArray<NamespaceBinding> Bindings { get; }

    /// <summary>
    /// The arena owning decoded declaration values. Appends only grow it;
    /// an element's close truncates the logical length back to its mark
    /// without writing, so spans exposed by the closing token survive until
    /// the next read appends over them.
    /// </summary>
    public GrowingArray<byte> Arena { get; }

    /// <summary>The attribute table of the current start tag.</summary>
    public GrowingArray<AttributeEntry> Attributes { get; }

    /// <summary>
    /// The per-token decode buffer for normalized attribute values and
    /// assembled text. Append-style within a token: growth preserves
    /// contents, and every reference into it is an extent re-sliced at
    /// accessor time.
    /// </summary>
    public GrowingArray<byte> DecodeBuffer { get; }

    /// <summary>The decoded-to-document segment list of the current text token.</summary>
    public GrowingArray<TextSegment> TextSegments { get; }

    /// <summary>Allocates every buffer at its initial capacity.</summary>
    private XmlFragmentScratch()
    {
        ElementFrames = new ElementFrame[GeometryCodecText.MaximumTransportDepth];
        Bindings = new GrowingArray<NamespaceBinding>(initialCapacity: 8);
        Arena = new GrowingArray<byte>(initialCapacity: 64);
        Attributes = new GrowingArray<AttributeEntry>(initialCapacity: 8);
        DecodeBuffer = new GrowingArray<byte>(initialCapacity: 256);
        TextSegments = new GrowingArray<TextSegment>(initialCapacity: 8);
    }

    /// <summary>Creates the working state one scanner borrows for its lifetime.</summary>
    public static XmlFragmentScratch Create()
    {
        return new XmlFragmentScratch();
    }

    /// <summary>Releases nothing; a second disposal is a no-op.</summary>
    public void Dispose()
    {
        //Every buffer is a heap array the collector owns, so there is nothing to return.
    }
}
