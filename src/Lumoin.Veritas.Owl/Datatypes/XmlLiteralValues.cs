using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Xml;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The value identity of the <c>rdf:XMLLiteral</c> datatype: two lexical forms
/// denote the same value exactly when their exclusive Canonical XML forms are
/// byte-equal, the lexical-to-value mapping of RDF Concepts
/// (<c>REC-rdf-concepts-20040210</c> section 5.1).
/// </summary>
/// <remarks>
/// <para>
/// Both sides are canonicalized at compare time and neither stored lexical form
/// is trusted to be canonical already, so a raw byte difference alone never
/// yields <see cref="DatatypeValueIdentity.Distinct"/>. Only two successful
/// canonicalizations that differ do; every failure — content that is not
/// well-balanced, a token the scanner rejects, a prefix used without a
/// declaration — is <see cref="DatatypeValueIdentity.Indeterminate"/>, and so is
/// any input carrying a comment or a processing instruction, which the byte
/// scanner does not surface and the with-comments mapping would distinguish.
/// </para>
/// <para>
/// The canonical form the comparison rides sorts attributes by namespace IRI
/// then local name (resolving each prefix against the open-element declarations
/// itself), renders a namespace declaration only where it is visibly utilized,
/// expands empty-element form to an explicit close, normalizes attribute quoting
/// and in-tag whitespace, resolves character, entity and CDATA content into
/// escaped text, and preserves text-node whitespace exactly — leading whitespace
/// distinguishes values.
/// </para>
/// </remarks>
internal static class XmlLiteralValues
{
    /// <summary>The byte count the first canonical-form rental covers; a form beyond it grows the rental by doubling.</summary>
    private const int InitialFormCapacity = 256;

    /// <summary>
    /// The three-valued value identity of two <c>rdf:XMLLiteral</c> lexical forms.
    /// </summary>
    /// <param name="first">The first literal's lexical form.</param>
    /// <param name="second">The second literal's lexical form.</param>
    /// <returns><see cref="DatatypeValueIdentity.Same"/> when the two canonicalize alike, <see cref="DatatypeValueIdentity.Distinct"/> when both canonicalize and differ, and <see cref="DatatypeValueIdentity.Indeterminate"/> when either side does not canonicalize or carries a comment or processing instruction.</returns>
    public static DatatypeValueIdentity Compare(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if(first.SequenceEqual(second))
        {
            //One lexical form denotes one value under any deterministic mapping, so the
            //identical pair needs no canonicalization and no comment guard.
            return DatatypeValueIdentity.Same;
        }

        if(HasCommentOrProcessingInstruction(first) || HasCommentOrProcessingInstruction(second))
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        using CanonicalFormBuffer firstForm = new();
        using CanonicalFormBuffer secondForm = new();
        if(!TryCanonicalize(first, firstForm) || !TryCanonicalize(second, secondForm))
        {
            return DatatypeValueIdentity.Indeterminate;
        }

        return firstForm.WrittenSpan.SequenceEqual(secondForm.WrittenSpan)
            ? DatatypeValueIdentity.Same
            : DatatypeValueIdentity.Distinct;
    }

    /// <summary>
    /// Whether a fragment carries a comment or a processing instruction, found by
    /// a CDATA-aware linear byte scan: inside a CDATA section nothing is markup,
    /// and outside one an <c>&lt;!--</c> or <c>&lt;?</c> opens a construct the
    /// canonical comparison cannot see. A raw <c>&lt;</c> cannot occur in an
    /// attribute value, so a tag's interior needs no separate treatment.
    /// </summary>
    /// <param name="content">The fragment bytes.</param>
    /// <returns><see langword="true"/> when a comment or processing instruction is present.</returns>
    private static bool HasCommentOrProcessingInstruction(ReadOnlySpan<byte> content)
    {
        int i = 0;
        while(i < content.Length)
        {
            int next = content.Slice(i).IndexOf((byte)'<');
            if(next < 0)
            {
                return false;
            }

            i += next;
            if(StartsWith(content, i, "<![CDATA["u8))
            {
                int close = IndexOf(content, i + "<![CDATA["u8.Length, "]]>"u8);
                if(close < 0)
                {
                    //An unterminated section is not well-formed; the canonicalization reports it.
                    return false;
                }

                i = close + "]]>"u8.Length;

                continue;
            }

            if(StartsWith(content, i, "<!--"u8) || StartsWith(content, i, "<?"u8))
            {
                return true;
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// Serializes a fragment into its exclusive Canonical XML form, reporting
    /// failure when the content is not well-formed: a token the scanner rejects,
    /// an end tag with no matching open element or a different name, an element
    /// left open at the end, or a prefix used without a declaration.
    /// </summary>
    /// <param name="content">The fragment bytes.</param>
    /// <param name="output">The buffer the canonical form is written into.</param>
    /// <returns><see langword="true"/> when the fragment canonicalized.</returns>
    private static bool TryCanonicalize(ReadOnlySpan<byte> content, CanonicalFormBuffer output)
    {
        XmlByteScanner scanner = new(XmlScanStrictness.Strict, parseInternalDtd: false);
        try
        {
            scanner.Feed(content);
            scanner.Complete();
        }
        catch(FormatException)
        {
            //The strict scanner surfaces a malformed token only by throwing; the
            //fragment simply represents no value, which is a verdict, not a fault.
            return false;
        }

        List<XmlNamespaceBinding> scope = [];
        List<XmlNamespaceBinding> rendered = [];
        Stack<ElementFrame> open = new();
        while(scanner.TryDequeue(out XmlScanEvent scanEvent))
        {
            if(scanEvent.Kind == XmlScanEventKind.StartElement)
            {
                if(!TryWriteStartElement(output, scanEvent, scope, rendered, open))
                {
                    return false;
                }
            }
            else if(scanEvent.Kind == XmlScanEventKind.EndElement)
            {
                if(open.Count == 0 || !open.Peek().Name.Span.SequenceEqual(scanEvent.Name.Span))
                {
                    return false;
                }

                ElementFrame frame = open.Pop();
                output.WriteEndTag(scanEvent.Name.Span);
                Truncate(scope, frame.ScopeMark);
                Truncate(rendered, frame.RenderedMark);
            }
            else if(scanEvent.Kind == XmlScanEventKind.Text)
            {
                output.WriteEscaped(scanEvent.Text.Span, attribute: false);
            }
        }

        return open.Count == 0;
    }

    /// <summary>
    /// Writes an element's start tag — its visibly utilized namespace axis then
    /// its sorted attribute axis — and either closes it at once (an empty
    /// element renders as an explicit pair) or pushes its scope for the content
    /// that follows.
    /// </summary>
    /// <param name="output">The canonical-form buffer.</param>
    /// <param name="start">The start-element event.</param>
    /// <param name="scope">The in-scope namespace declarations, innermost last, appended to.</param>
    /// <param name="rendered">The declarations already written on an output ancestor, innermost last, appended to.</param>
    /// <param name="open">The open-element stack.</param>
    /// <returns><see langword="true"/> when the tag was written.</returns>
    private static bool TryWriteStartElement(CanonicalFormBuffer output, XmlScanEvent start, List<XmlNamespaceBinding> scope, List<XmlNamespaceBinding> rendered, Stack<ElementFrame> open)
    {
        int scopeMark = scope.Count;
        int renderedMark = rendered.Count;
        foreach(XmlScanAttribute attribute in start.Attributes)
        {
            if(XmlCanonicalWriting.TryReadDeclaration(attribute, out XmlNamespaceBinding binding))
            {
                scope.Add(binding);
            }
        }

        output.Write("<"u8);
        output.Write(start.Name.Span);
        if(!TryWriteNamespaceAxis(output, start, scope, rendered) || !TryWriteAttributeAxis(output, start.Attributes, scope))
        {
            return false;
        }

        output.Write(">"u8);
        if(!start.IsEmpty)
        {
            open.Push(new ElementFrame(start.Name, scopeMark, renderedMark));

            return true;
        }

        output.WriteEndTag(start.Name.Span);
        Truncate(scope, scopeMark);
        Truncate(rendered, renderedMark);

        return true;
    }

    /// <summary>
    /// Writes the declarations of the prefixes an element visibly utilizes — its
    /// own and those of its prefixed attributes — sorted by prefix, each skipped
    /// when an output ancestor already bound it to the same IRI. A declaration
    /// nothing utilizes never reaches the canonical form, so it cannot
    /// distinguish two values.
    /// </summary>
    /// <param name="output">The canonical-form buffer.</param>
    /// <param name="start">The start-element event.</param>
    /// <param name="scope">The in-scope namespace declarations, innermost last.</param>
    /// <param name="rendered">The declarations already written on an output ancestor, appended to.</param>
    /// <returns><see langword="true"/> when every utilized prefix resolved.</returns>
    private static bool TryWriteNamespaceAxis(CanonicalFormBuffer output, XmlScanEvent start, List<XmlNamespaceBinding> scope, List<XmlNamespaceBinding> rendered)
    {
        List<XmlNamespaceBinding> utilized = [];
        if(!TryCollectUtilized(XmlCanonicalWriting.PrefixOf(start.Name.Span), scope, utilized))
        {
            return false;
        }

        foreach(XmlScanAttribute attribute in start.Attributes)
        {
            ReadOnlySpan<byte> prefix = XmlCanonicalWriting.PrefixOf(attribute.Name.Span);
            if(XmlCanonicalWriting.IsNamespaceDeclaration(attribute.Name.Span) || prefix.IsEmpty)
            {
                //An unprefixed attribute is in no namespace, and a declaration is
                //carried by the namespace axis rather than utilizing one itself.
                continue;
            }

            if(!TryCollectUtilized(prefix, scope, utilized))
            {
                return false;
            }
        }

        utilized.Sort(static (left, right) => left.Prefix.Span.SequenceCompareTo(right.Prefix.Span));
        foreach(XmlNamespaceBinding binding in utilized)
        {
            WriteDeclarationIfNew(output, binding, rendered);
        }

        return true;
    }

    /// <summary>
    /// Adds the binding of one utilized prefix to the element's namespace axis,
    /// skipping the implicit <c>xml</c> prefix (never declared) and a prefix
    /// already collected. An unprefixed name with no default declaration in scope
    /// is in no namespace and collects the empty binding, which renders only where
    /// it cancels an inherited default.
    /// </summary>
    /// <param name="prefix">The utilized prefix; empty for the default namespace.</param>
    /// <param name="scope">The in-scope namespace declarations, innermost last.</param>
    /// <param name="utilizedToAppendTo">The bindings collected for this element, appended to.</param>
    /// <returns><see langword="true"/> when the prefix resolved or needs no declaration.</returns>
    private static bool TryCollectUtilized(ReadOnlySpan<byte> prefix, List<XmlNamespaceBinding> scope, List<XmlNamespaceBinding> utilizedToAppendTo)
    {
        if(prefix.SequenceEqual(XmlCanonicalWriting.XmlPrefix.Span))
        {
            return true;
        }

        foreach(XmlNamespaceBinding collected in utilizedToAppendTo)
        {
            if(collected.Prefix.Span.SequenceEqual(prefix))
            {
                return true;
            }
        }

        int index = LastBinding(scope, prefix);
        if(index >= 0)
        {
            utilizedToAppendTo.Add(scope[index]);

            return true;
        }

        if(!prefix.IsEmpty)
        {
            //A prefix used without a declaration is not namespace-well-formed.
            return false;
        }

        utilizedToAppendTo.Add(new XmlNamespaceBinding(default, default));

        return true;
    }

    /// <summary>
    /// Writes a namespace declaration unless it carries no information: a prefix
    /// an output ancestor already bound to the same IRI is suppressed, and an
    /// empty default declaration is written only to cancel a rendered non-empty
    /// default. A written declaration joins the rendered context.
    /// </summary>
    /// <param name="output">The canonical-form buffer.</param>
    /// <param name="binding">The candidate binding.</param>
    /// <param name="renderedToAppendTo">The declarations already written on an output ancestor, appended to.</param>
    private static void WriteDeclarationIfNew(CanonicalFormBuffer output, XmlNamespaceBinding binding, List<XmlNamespaceBinding> renderedToAppendTo)
    {
        int index = LastBinding(renderedToAppendTo, binding.Prefix.Span);
        bool boundAlike = index >= 0 && renderedToAppendTo[index].NamespaceIri.Span.SequenceEqual(binding.NamespaceIri.Span);
        if(boundAlike)
        {
            return;
        }

        if(binding.NamespaceIri.Span.IsEmpty && (index < 0 || renderedToAppendTo[index].NamespaceIri.Span.IsEmpty))
        {
            return;
        }

        output.WriteDeclaration(binding.Prefix.Span, binding.NamespaceIri.Span);
        renderedToAppendTo.Add(binding);
    }

    /// <summary>
    /// Writes an element's non-declaration attributes sorted by namespace IRI
    /// then local name, each quoted with double quotes and escaped. The scanner
    /// resolves no namespaces, so every prefix is resolved here against the
    /// open-element declarations before the sort key is formed, and an
    /// unresolved prefix fails the element before any attribute is written.
    /// </summary>
    /// <param name="output">The canonical-form buffer.</param>
    /// <param name="attributes">The start tag's attributes in document order.</param>
    /// <param name="scope">The in-scope namespace declarations, innermost last.</param>
    /// <returns><see langword="true"/> when every attribute prefix resolved.</returns>
    private static bool TryWriteAttributeAxis(CanonicalFormBuffer output, IReadOnlyList<XmlScanAttribute> attributes, List<XmlNamespaceBinding> scope)
    {
        List<XmlSortedAttribute> sorted = [];
        foreach(XmlScanAttribute attribute in attributes)
        {
            if(XmlCanonicalWriting.IsNamespaceDeclaration(attribute.Name.Span))
            {
                continue;
            }

            if(!TryAttributeNamespace(attribute.Name.Span, scope, out Utf8String namespaceIri))
            {
                return false;
            }

            sorted.Add(new XmlSortedAttribute(attribute, namespaceIri, XmlCanonicalWriting.LocalNameOf(attribute.Name)));
        }

        output.WriteSortedAttributes(sorted);

        return true;
    }

    /// <summary>The namespace IRI sort key of an attribute: empty for an unprefixed attribute (which is in no namespace), the XML namespace for an <c>xml:</c> attribute, otherwise the prefix's bound IRI.</summary>
    /// <param name="qualified">The attribute's qualified name as written.</param>
    /// <param name="scope">The in-scope namespace declarations, innermost last.</param>
    /// <param name="namespaceIri">The sort key, on success.</param>
    /// <returns><see langword="true"/> when the prefix resolved.</returns>
    private static bool TryAttributeNamespace(ReadOnlySpan<byte> qualified, List<XmlNamespaceBinding> scope, out Utf8String namespaceIri)
    {
        namespaceIri = default;

        ReadOnlySpan<byte> prefix = XmlCanonicalWriting.PrefixOf(qualified);
        if(prefix.IsEmpty)
        {
            return true;
        }

        if(prefix.SequenceEqual(XmlCanonicalWriting.XmlPrefix.Span))
        {
            namespaceIri = XmlCanonicalWriting.XmlNamespaceIri;

            return true;
        }

        int index = LastBinding(scope, prefix);
        if(index < 0)
        {
            return false;
        }

        namespaceIri = scope[index].NamespaceIri;

        return true;
    }

    /// <summary>The index of the innermost binding for a prefix, or <c>-1</c> when none is in the list.</summary>
    /// <param name="bindings">The bindings, innermost last.</param>
    /// <param name="prefix">The prefix to resolve.</param>
    /// <returns>The index, or <c>-1</c>.</returns>
    private static int LastBinding(List<XmlNamespaceBinding> bindings, ReadOnlySpan<byte> prefix)
    {
        for(int index = bindings.Count - 1; index >= 0; index--)
        {
            if(bindings[index].Prefix.Span.SequenceEqual(prefix))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Drops the tail of a binding list beyond a recorded mark, restoring the state an element inherited.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <param name="mark">The count to restore to.</param>
    private static void Truncate(List<XmlNamespaceBinding> bindings, int mark)
    {
        bindings.RemoveRange(mark, bindings.Count - mark);
    }

    /// <summary>Whether the bytes at an offset begin with a sequence.</summary>
    /// <param name="content">The bytes to test.</param>
    /// <param name="start">The offset to test at.</param>
    /// <param name="prefix">The sequence to match.</param>
    /// <returns><see langword="true"/> when the bytes at the offset begin with the sequence.</returns>
    private static bool StartsWith(ReadOnlySpan<byte> content, int start, ReadOnlySpan<byte> prefix)
    {
        return start + prefix.Length <= content.Length && content.Slice(start, prefix.Length).SequenceEqual(prefix);
    }

    /// <summary>Finds a byte sequence at or after an offset.</summary>
    /// <param name="content">The bytes to search.</param>
    /// <param name="start">The offset to search from.</param>
    /// <param name="sequence">The sequence to find.</param>
    /// <returns>The offset of the sequence, or <c>-1</c>.</returns>
    private static int IndexOf(ReadOnlySpan<byte> content, int start, ReadOnlySpan<byte> sequence)
    {
        if(start < 0 || start > content.Length)
        {
            return -1;
        }

        int found = content.Slice(start).IndexOf(sequence);

        return found < 0 ? -1 : start + found;
    }

    /// <summary>One open element during the walk: its qualified name and the declaration-list lengths its content inherited.</summary>
    /// <param name="Name">The element's qualified name as written.</param>
    /// <param name="ScopeMark">The in-scope declaration count to restore when the element closes.</param>
    /// <param name="RenderedMark">The rendered declaration count to restore when the element closes.</param>
    private readonly record struct ElementFrame(Utf8String Name, int ScopeMark, int RenderedMark);

    /// <summary>
    /// A growable UTF-8 sink over pool-rented memory holding one literal's
    /// canonical form for the byte comparison. A write beyond the rental doubles
    /// it and copies; disposal returns the rental to the pool.
    /// </summary>
    private sealed class CanonicalFormBuffer: IBufferWriter<byte>, IDisposable
    {
        /// <summary>The active pool rental holding the bytes written so far.</summary>
        private IMemoryOwner<byte> Rental { get; set; }

        /// <summary>The number of bytes written into the rental.</summary>
        private int Written { get; set; }

        /// <summary>Rents the initial buffer from the shared pool.</summary>
        public CanonicalFormBuffer()
        {
            Rental = VeritasMemoryPool<byte>.Shared.Rent(InitialFormCapacity);
            Written = 0;
        }

        /// <summary>The canonical-form bytes written so far.</summary>
        public ReadOnlySpan<byte> WrittenSpan => Rental.Memory.Span.Slice(0, Written);

        /// <summary>Counts bytes a writer placed into the tail the last request handed out.</summary>
        /// <param name="count">The number of bytes written.</param>
        public void Advance(int count)
        {
            Written += count;
        }

        /// <summary>The unwritten tail of the rental as memory, grown first to hold at least the requested count.</summary>
        /// <param name="sizeHint">The least number of free bytes wanted; <c>0</c> asks for at least one.</param>
        /// <returns>The writable tail, never empty.</returns>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Grow(Written + Math.Max(1, sizeHint));

            return Rental.Memory.Slice(Written);
        }

        /// <summary>The unwritten tail of the rental as a span, grown first to hold at least the requested count.</summary>
        /// <param name="sizeHint">The least number of free bytes wanted; <c>0</c> asks for at least one.</param>
        /// <returns>The writable tail, never empty.</returns>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Grow(Written + Math.Max(1, sizeHint));

            return Rental.Memory.Span.Slice(Written);
        }

        /// <summary>Returns the rental to the pool.</summary>
        public void Dispose()
        {
            Rental.Dispose();
        }

        /// <summary>Replaces the rental with a doubled one holding the same bytes when the required length exceeds it.</summary>
        /// <param name="required">The byte count the rental must hold.</param>
        private void Grow(int required)
        {
            int capacity = Rental.Memory.Length;
            if(required <= capacity)
            {
                return;
            }

            while(capacity < required)
            {
                capacity *= 2;
            }

            IMemoryOwner<byte> grown = VeritasMemoryPool<byte>.Shared.Rent(capacity);
            Rental.Memory.Span.Slice(0, Written).CopyTo(grown.Memory.Span);
            Rental.Dispose();
            Rental = grown;
        }
    }
}
