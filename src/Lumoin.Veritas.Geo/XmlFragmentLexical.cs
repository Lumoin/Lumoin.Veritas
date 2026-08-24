using System;

namespace Lumoin.Veritas.Geo;

/// <summary>What the format layer makes of one element the scanner has opened.</summary>
internal enum XmlContentDisposition : byte
{
    /// <summary>
    /// The element is outside the format layer's content model. The scanner keeps enforcing
    /// well-formedness inside the subtree while nothing in it is certified, and the fragment answers
    /// <see cref="GeometryLexicalRecognition.Unrecognized"/> unless a provable breakage overrides it.
    /// This is the zero value, so a defaulted classification claims nothing.
    /// </summary>
    Suppressed = 0,

    /// <summary>The element's children are modeled, so each child is classified in turn.</summary>
    Model,

    /// <summary>
    /// The element carries certified token content: its raw character data goes to the format layer's
    /// token validator whenever no markup splits it.
    /// </summary>
    Token,

    /// <summary>The element is provably outside the format's grammar.</summary>
    Malformed
}

/// <summary>How many certified children one modeled element admits.</summary>
internal enum XmlChildMultiplicity : byte
{
    /// <summary>Any number of certified children, including none.</summary>
    Any = 0,

    /// <summary>
    /// One certified child at most: a second one is a provable violation of the wrapper's multiplicity.
    /// An absent child is the by-reference form of a property element and is never rejected.
    /// </summary>
    SingleMember
}

/// <summary>
/// The format layer's verdict on one element: how the scanner treats the element's content, which
/// content-model kind the element carries for the classification of its own children, and how many
/// certified children it admits.
/// </summary>
internal readonly struct XmlContentClassification
{
    /// <summary>Builds one classification.</summary>
    /// <param name="disposition">How the scanner treats the element's content.</param>
    /// <param name="kind">The format layer's content-model kind for the element.</param>
    /// <param name="multiplicity">How many certified children the element admits.</param>
    private XmlContentClassification(XmlContentDisposition disposition, byte kind, XmlChildMultiplicity multiplicity)
    {
        Disposition = disposition;
        Kind = kind;
        Multiplicity = multiplicity;
    }

    /// <summary>How the scanner treats the element's content.</summary>
    public XmlContentDisposition Disposition { get; }

    /// <summary>
    /// The format layer's content-model kind for the element, handed back as the parent kind when the
    /// element's own children are classified.
    /// </summary>
    public byte Kind { get; }

    /// <summary>How many certified children the element admits.</summary>
    public XmlChildMultiplicity Multiplicity { get; }

    /// <summary>An element whose children are modeled, in any number.</summary>
    /// <param name="kind">The format layer's content-model kind for the element.</param>
    /// <returns>The classification.</returns>
    public static XmlContentClassification Model(byte kind)
    {
        return new XmlContentClassification(XmlContentDisposition.Model, kind, XmlChildMultiplicity.Any);
    }

    /// <summary>An element whose children are modeled and which admits one certified child at most.</summary>
    /// <param name="kind">The format layer's content-model kind for the element.</param>
    /// <returns>The classification.</returns>
    public static XmlContentClassification SingleMemberModel(byte kind)
    {
        return new XmlContentClassification(XmlContentDisposition.Model, kind, XmlChildMultiplicity.SingleMember);
    }

    /// <summary>An element whose raw character data the format layer certifies as a token grammar.</summary>
    /// <param name="kind">The format layer's token kind for the element.</param>
    /// <returns>The classification.</returns>
    public static XmlContentClassification Token(byte kind)
    {
        return new XmlContentClassification(XmlContentDisposition.Token, kind, XmlChildMultiplicity.Any);
    }

    /// <summary>An element the format layer does not model, whose subtree stays well-formedness-checked only.</summary>
    public static XmlContentClassification Suppressed
    {
        get { return new XmlContentClassification(XmlContentDisposition.Suppressed, 0, XmlChildMultiplicity.Any); }
    }

    /// <summary>An element that is provably outside the format's grammar.</summary>
    public static XmlContentClassification Malformed
    {
        get { return new XmlContentClassification(XmlContentDisposition.Malformed, 0, XmlChildMultiplicity.Any); }
    }
}

/// <summary>One half-open byte range of the scanned fragment.</summary>
internal struct XmlNameSpan
{
    /// <summary>The index of the first byte.</summary>
    public int Start;

    /// <summary>The index one past the last byte.</summary>
    public int End;
}

/// <summary>One namespace declaration read from the root element.</summary>
internal struct XmlNamespaceBinding
{
    /// <summary>The declared prefix, empty for the default namespace declaration.</summary>
    public XmlNameSpan Prefix;

    /// <summary>The namespace URI the prefix is bound to.</summary>
    public XmlNameSpan Uri;
}

/// <summary>One open element of the fragment being scanned.</summary>
internal struct XmlFragmentFrame
{
    /// <summary>The index of the first byte of the open tag's qualified name.</summary>
    public int NameStart;

    /// <summary>The index one past the last byte of the open tag's qualified name.</summary>
    public int NameEnd;

    /// <summary>The index of the first content byte, one past the open tag's closing angle bracket.</summary>
    public int ContentStart;

    /// <summary>How many certified children the element has taken so far.</summary>
    public int CertifiedChildCount;

    /// <summary>How the scanner treats this element's content.</summary>
    public XmlContentDisposition Disposition;

    /// <summary>How many certified children this element admits.</summary>
    public XmlChildMultiplicity Multiplicity;

    /// <summary>The format layer's content-model kind for this element.</summary>
    public byte Kind;

    /// <summary>
    /// Whether markup or an entity reference has appeared inside token content, which leaves the token
    /// grammar uncertified for this element because token content is never spliced across markup.
    /// </summary>
    public bool TokenContentSpliced;
}

/// <summary>The outcome of scanning one element's attribute list.</summary>
internal struct XmlAttributeScanResult
{
    /// <summary>Whether the element ended with a self-closing tag and therefore has no content.</summary>
    public bool SelfClosing;

    /// <summary>Whether the element carries a namespace declaration of its own.</summary>
    public bool DeclaresNamespace;

    /// <summary>
    /// Whether the attribute list outgrew a scanner table, which leaves duplicate detection or namespace
    /// resolution incomplete for the element and forces abstention.
    /// </summary>
    public bool Uncertified;
}

/// <summary>
/// Classifies the root element of an XML fragment from its resolved namespace URI and local name.
/// </summary>
/// <param name="namespaceUri">The namespace URI the root's prefix or default declaration resolves to, empty when the root is in no namespace.</param>
/// <param name="localName">The root's local name.</param>
/// <returns>The format layer's classification of the root.</returns>
internal delegate XmlContentClassification XmlRootClassifier(ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName);

/// <summary>
/// Classifies one child of a modeled element that resolves to the format's own namespace.
/// </summary>
/// <param name="parentKind">The content-model kind the format layer assigned to the parent element.</param>
/// <param name="localName">The child's local name.</param>
/// <returns>The format layer's classification of the child.</returns>
internal delegate XmlContentClassification XmlChildClassifier(byte parentKind, ReadOnlySpan<byte> localName);

/// <summary>
/// Certifies the raw character data of a token-content element against the format's token grammar.
/// </summary>
/// <param name="kind">The token kind the format layer assigned to the element.</param>
/// <param name="content">The element's raw character data, unsplit by markup.</param>
/// <returns><see langword="true"/> when the content fits the token grammar; <see langword="false"/> makes the fragment malformed.</returns>
internal delegate bool XmlTokenContentValidator(byte kind, ReadOnlySpan<byte> content);

/// <summary>
/// A structural scanner for a standalone XML fragment, shared by the GML and KML geometry recognizers.
/// The scan is one forward pass over UTF-8 bytes with an explicit frame stack, no recursion and no
/// runtime regular expressions; the format layer supplies the content model through a root classifier, a
/// child classifier and a token-content validator.
/// </summary>
/// <remarks>
/// <para>
/// The scanner certifies XML well-formedness over the subset a geometry literal can occupy: an optional
/// leading XML declaration, then exactly one root element, then end of input past trailing whitespace,
/// comments and processing instructions. Element names match byte-exactly between start and end tag,
/// attribute values are quoted, a duplicate attribute name within one element is malformed, and the five
/// predefined entities plus decimal and hexadecimal character references are the only references that
/// stand without a document type declaration. A processing instruction whose target is <c>xml</c> in any
/// casing anywhere other than the single leading declaration is malformed. A document type declaration
/// anywhere leaves the fragment uncertified, because a declared entity can carry anything, and from that
/// point an unknown entity name is no longer provably undeclared.
/// </para>
/// <para>
/// The fragment is standalone, so the root element's own declarations are the whole namespace scope.
/// Child elements are compared by resolved namespace URI, never by prefix bytes; a child carrying any
/// namespace declaration of its own opens an unmodeled subtree, and a child whose prefix resolves to no
/// root binding is provably namespace-ill-formed and therefore malformed.
/// </para>
/// <para>
/// One frame stack and one depth counter carry both structural nesting and content-model nesting, and the
/// format recognizer allocates that stack so its cap is the recognizer's own. A suppressed frame marks an
/// unmodeled subtree: well-formedness keeps being enforced inside it while nothing is certified, and
/// entering one sets the uncertified flag that stays set for the rest of the scan. The verdict is
/// malformed on any provable breakage, otherwise the depth cap when it fired, otherwise unrecognized when
/// the uncertified flag is set, otherwise well-formed.
/// </para>
/// </remarks>
internal static class XmlFragmentLexical
{
    /// <summary>
    /// How many attribute names of one element are held for duplicate detection. An element with more
    /// attributes than this leaves the fragment uncertified rather than unchecked.
    /// </summary>
    private const int MaximumAttributesPerElement = 32;

    /// <summary>
    /// How many namespace declarations of the root element are held. A root with more declarations than
    /// this leaves the fragment uncertified, because resolution would be incomplete.
    /// </summary>
    private const int MaximumNamespaceBindings = 16;

    /// <summary>The namespace URI the reserved <c>xml</c> prefix is bound to without any declaration.</summary>
    private static ReadOnlySpan<byte> ReservedXmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    /// <summary>Scans one XML fragment against a format layer's content model.</summary>
    /// <param name="body">The candidate fragment as UTF-8 bytes; a caller answers an empty body itself before scanning.</param>
    /// <param name="certifiedNamespace">The namespace URI whose children the child classifier decides; children resolving elsewhere are unmodeled.</param>
    /// <param name="rootClassifier">Classifies the root element.</param>
    /// <param name="childClassifier">Classifies a child of a modeled element.</param>
    /// <param name="tokenContentValidator">Certifies the character data of a token-content element.</param>
    /// <param name="frames">The frame stack, whose length is the recognizer's nesting cap.</param>
    /// <returns>The recognition outcome.</returns>
    internal static GeometryLexicalRecognition Recognize(
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> certifiedNamespace,
        XmlRootClassifier rootClassifier,
        XmlChildClassifier childClassifier,
        XmlTokenContentValidator tokenContentValidator,
        Span<XmlFragmentFrame> frames)
    {
        Span<XmlNameSpan> attributeNames = stackalloc XmlNameSpan[MaximumAttributesPerElement];
        Span<XmlNamespaceBinding> bindings = stackalloc XmlNamespaceBinding[MaximumNamespaceBindings];
        int bindingCount = 0;
        int index = 0;
        int depth = 0;
        bool uncertified = false;
        bool doctypeSeen = false;

        if(!TryScanProlog(body, ref index, ref doctypeSeen, ref uncertified))
        {
            return GeometryLexicalRecognition.Malformed;
        }

        int rootNameStart = index + 1;
        index = rootNameStart;
        ReadOnlySpan<byte> rootName = ReadName(body, ref index);
        if(rootName.IsEmpty)
        {
            return GeometryLexicalRecognition.Malformed;
        }

        if(!TryScanAttributes(body, ref index, attributeNames, bindings, ref bindingCount, collectBindings: true, doctypeSeen, out XmlAttributeScanResult rootAttributes))
        {
            return GeometryLexicalRecognition.Malformed;
        }

        XmlContentClassification rootClassification;
        if(rootAttributes.Uncertified)
        {
            uncertified = true;
            rootClassification = XmlContentClassification.Suppressed;
        }
        else
        {
            SplitQualifiedName(rootName, out ReadOnlySpan<byte> rootPrefix, out ReadOnlySpan<byte> rootLocalName);
            if(!TryResolveNamespace(body, bindings, bindingCount, rootPrefix, out ReadOnlySpan<byte> rootNamespace))
            {
                return GeometryLexicalRecognition.Malformed;
            }

            rootClassification = rootClassifier(rootNamespace, rootLocalName);
            if(rootClassification.Disposition == XmlContentDisposition.Malformed)
            {
                return GeometryLexicalRecognition.Malformed;
            }

            if(rootClassification.Disposition == XmlContentDisposition.Suppressed)
            {
                uncertified = true;
            }
        }

        if(rootAttributes.SelfClosing)
        {
            if(rootClassification.Disposition == XmlContentDisposition.Token && !tokenContentValidator(rootClassification.Kind, default))
            {
                return GeometryLexicalRecognition.Malformed;
            }
        }
        else
        {
            if(frames.Length == 0)
            {
                return GeometryLexicalRecognition.DepthExceeded;
            }

            frames[0] = NewFrame(rootNameStart, rootNameStart + rootName.Length, index, rootClassification);
            depth = 1;
        }

        while(depth > 0)
        {
            if(index == body.Length)
            {
                return GeometryLexicalRecognition.Malformed;
            }

            byte current = body[index];
            if(current == (byte)'&')
            {
                if(!TryScanReference(body, ref index, doctypeSeen))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                MarkTokenContentSpliced(ref frames[depth - 1], ref uncertified);
                continue;
            }

            if(current != (byte)'<')
            {
                if(current == (byte)']' && StartsWith(body, index, "]]>"u8))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                index++;
                continue;
            }

            if(StartsWith(body, index, "</"u8))
            {
                int cursor = index + 2;
                ReadOnlySpan<byte> endName = ReadName(body, ref cursor);
                ref XmlFragmentFrame closing = ref frames[depth - 1];
                if(!endName.SequenceEqual(body[closing.NameStart..closing.NameEnd]))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                SkipWhitespace(body, ref cursor);
                if(cursor == body.Length || body[cursor] != (byte)'>')
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                if(closing.Disposition == XmlContentDisposition.Token
                    && !closing.TokenContentSpliced
                    && !tokenContentValidator(closing.Kind, body[closing.ContentStart..index]))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                index = cursor + 1;
                depth--;
                continue;
            }

            if(StartsWith(body, index, "<!--"u8))
            {
                if(!TryScanComment(body, ref index))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                MarkTokenContentSpliced(ref frames[depth - 1], ref uncertified);
                continue;
            }

            if(StartsWith(body, index, "<![CDATA["u8))
            {
                if(!TryScanCdata(body, ref index))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                MarkTokenContentSpliced(ref frames[depth - 1], ref uncertified);
                continue;
            }

            if(StartsWith(body, index, "<!DOCTYPE"u8))
            {
                if(!TryScanDoctype(body, ref index))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                doctypeSeen = true;
                uncertified = true;
                MarkTokenContentSpliced(ref frames[depth - 1], ref uncertified);
                continue;
            }

            if(StartsWith(body, index, "<?"u8))
            {
                if(!TryScanProcessingInstruction(body, ref index, declarationAllowed: false))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                MarkTokenContentSpliced(ref frames[depth - 1], ref uncertified);
                continue;
            }

            if(index + 1 == body.Length || !IsNameStart(body[index + 1]))
            {
                return GeometryLexicalRecognition.Malformed;
            }

            int childNameStart = index + 1;
            index = childNameStart;
            ReadOnlySpan<byte> childName = ReadName(body, ref index);
            if(!TryScanAttributes(body, ref index, attributeNames, bindings, ref bindingCount, collectBindings: false, doctypeSeen, out XmlAttributeScanResult childAttributes))
            {
                return GeometryLexicalRecognition.Malformed;
            }

            ref XmlFragmentFrame parent = ref frames[depth - 1];
            bool parentModels = parent.Disposition == XmlContentDisposition.Model;
            XmlContentClassification classification;
            if(!parentModels || childAttributes.DeclaresNamespace || childAttributes.Uncertified)
            {
                classification = XmlContentClassification.Suppressed;
                uncertified = true;
            }
            else
            {
                SplitQualifiedName(childName, out ReadOnlySpan<byte> childPrefix, out ReadOnlySpan<byte> childLocalName);
                if(!TryResolveNamespace(body, bindings, bindingCount, childPrefix, out ReadOnlySpan<byte> childNamespace))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                classification = childNamespace.SequenceEqual(certifiedNamespace)
                    ? childClassifier(parent.Kind, childLocalName)
                    : XmlContentClassification.Suppressed;

                if(classification.Disposition == XmlContentDisposition.Malformed)
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                if(classification.Disposition == XmlContentDisposition.Suppressed)
                {
                    uncertified = true;
                }
                else
                {
                    parent.CertifiedChildCount++;
                    if(parent.Multiplicity == XmlChildMultiplicity.SingleMember && parent.CertifiedChildCount > 1)
                    {
                        return GeometryLexicalRecognition.Malformed;
                    }
                }
            }

            MarkTokenContentSpliced(ref parent, ref uncertified);

            if(childAttributes.SelfClosing)
            {
                if(classification.Disposition == XmlContentDisposition.Token && !tokenContentValidator(classification.Kind, default))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                continue;
            }

            if(depth == frames.Length)
            {
                return GeometryLexicalRecognition.DepthExceeded;
            }

            frames[depth] = NewFrame(childNameStart, childNameStart + childName.Length, index, classification);
            depth++;
        }

        if(!TryScanEpilog(body, ref index))
        {
            return GeometryLexicalRecognition.Malformed;
        }

        return uncertified ? GeometryLexicalRecognition.Unrecognized : GeometryLexicalRecognition.WellFormed;
    }

    /// <summary>Whether the byte is XML whitespace: space, tab, carriage return, or line feed.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a whitespace byte.</returns>
    internal static bool IsWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Advances the index past any whitespace.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, advanced past whitespace.</param>
    internal static void SkipWhitespace(ReadOnlySpan<byte> body, ref int index)
    {
        while(index < body.Length && IsWhitespace(body[index]))
        {
            index++;
        }
    }

    /// <summary>
    /// Reads one number of a token grammar: an optional sign, digits with an optional decimal point, and
    /// an optional exponent. The delimiter that follows is the caller's to certify.
    /// </summary>
    /// <param name="content">The bytes being scanned.</param>
    /// <param name="index">The scan position, advanced past the number when it is valid.</param>
    /// <returns><see langword="true"/> when a number was read.</returns>
    internal static bool TryReadNumericToken(ReadOnlySpan<byte> content, ref int index)
    {
        int cursor = index;
        if(cursor < content.Length && content[cursor] is (byte)'+' or (byte)'-')
        {
            cursor++;
        }

        int digits = 0;
        while(cursor < content.Length && IsAsciiDigit(content[cursor]))
        {
            cursor++;
            digits++;
        }

        if(cursor < content.Length && content[cursor] == (byte)'.')
        {
            cursor++;
            while(cursor < content.Length && IsAsciiDigit(content[cursor]))
            {
                cursor++;
                digits++;
            }
        }

        if(digits == 0)
        {
            return false;
        }

        if(cursor < content.Length && (content[cursor] | 0x20) == (byte)'e')
        {
            cursor++;
            if(cursor < content.Length && content[cursor] is (byte)'+' or (byte)'-')
            {
                cursor++;
            }

            int exponentDigits = 0;
            while(cursor < content.Length && IsAsciiDigit(content[cursor]))
            {
                cursor++;
                exponentDigits++;
            }

            if(exponentDigits == 0)
            {
                return false;
            }
        }

        index = cursor;

        return true;
    }

    /// <summary>Builds one frame from a classification.</summary>
    /// <param name="nameStart">The index of the first byte of the open tag's qualified name.</param>
    /// <param name="nameEnd">The index one past the last byte of the open tag's qualified name.</param>
    /// <param name="contentStart">The index of the element's first content byte.</param>
    /// <param name="classification">The format layer's classification of the element.</param>
    /// <returns>The frame to push.</returns>
    private static XmlFragmentFrame NewFrame(int nameStart, int nameEnd, int contentStart, XmlContentClassification classification)
    {
        return new XmlFragmentFrame
        {
            NameStart = nameStart,
            NameEnd = nameEnd,
            ContentStart = contentStart,
            CertifiedChildCount = 0,
            Disposition = classification.Disposition,
            Multiplicity = classification.Multiplicity,
            Kind = classification.Kind,
            TokenContentSpliced = false
        };
    }

    /// <summary>
    /// Records that markup or an entity reference appeared inside token content, which leaves that
    /// element's token grammar uncertified because content is never spliced across markup.
    /// </summary>
    /// <param name="frame">The frame the construct appeared in.</param>
    /// <param name="uncertified">The scan's sticky uncertified flag.</param>
    private static void MarkTokenContentSpliced(ref XmlFragmentFrame frame, ref bool uncertified)
    {
        if(frame.Disposition == XmlContentDisposition.Token)
        {
            frame.TokenContentSpliced = true;
            uncertified = true;
        }
    }

    /// <summary>
    /// Scans everything ahead of the root element: whitespace, one optional XML declaration, comments,
    /// processing instructions and a document type declaration.
    /// </summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left at the root element's opening angle bracket.</param>
    /// <param name="doctypeSeen">Set when a document type declaration was scanned.</param>
    /// <param name="uncertified">The scan's sticky uncertified flag.</param>
    /// <returns><see langword="false"/> when the prolog is provably broken or no root element follows.</returns>
    private static bool TryScanProlog(ReadOnlySpan<byte> body, ref int index, ref bool doctypeSeen, ref bool uncertified)
    {
        //An XML entity may open with a UTF-8 byte-order mark ahead of everything else.
        if(body.Length - index >= 3 && body[index] == 0xEF && body[index + 1] == 0xBB && body[index + 2] == 0xBF)
        {
            index += 3;
        }

        bool declarationAllowed = true;
        while(true)
        {
            SkipWhitespace(body, ref index);
            if(index == body.Length || body[index] != (byte)'<')
            {
                return false;
            }

            if(StartsWith(body, index, "<!--"u8))
            {
                if(!TryScanComment(body, ref index))
                {
                    return false;
                }

                declarationAllowed = false;
                continue;
            }

            if(StartsWith(body, index, "<!DOCTYPE"u8))
            {
                if(!TryScanDoctype(body, ref index))
                {
                    return false;
                }

                doctypeSeen = true;
                uncertified = true;
                declarationAllowed = false;
                continue;
            }

            if(StartsWith(body, index, "<?"u8))
            {
                if(!TryScanProcessingInstruction(body, ref index, declarationAllowed))
                {
                    return false;
                }

                declarationAllowed = false;
                continue;
            }

            return index + 1 < body.Length && IsNameStart(body[index + 1]);
        }
    }

    /// <summary>Scans everything past the root element: whitespace, comments and processing instructions.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left at the end of the fragment.</param>
    /// <returns><see langword="false"/> when anything else follows the root element.</returns>
    private static bool TryScanEpilog(ReadOnlySpan<byte> body, ref int index)
    {
        while(true)
        {
            SkipWhitespace(body, ref index);
            if(index == body.Length)
            {
                return true;
            }

            if(StartsWith(body, index, "<!--"u8))
            {
                if(!TryScanComment(body, ref index))
                {
                    return false;
                }

                continue;
            }

            if(StartsWith(body, index, "<?"u8))
            {
                if(!TryScanProcessingInstruction(body, ref index, declarationAllowed: false))
                {
                    return false;
                }

                continue;
            }

            return false;
        }
    }

    /// <summary>
    /// Scans one element's attribute list up to and including the tag's closing angle bracket, checking
    /// quoting, references and duplicate names in place, and collecting the root's namespace declarations.
    /// </summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the tag.</param>
    /// <param name="attributeNames">The table holding this element's attribute names for duplicate detection.</param>
    /// <param name="bindings">The namespace binding table.</param>
    /// <param name="bindingCount">How many bindings the table holds.</param>
    /// <param name="collectBindings">Whether namespace declarations enter the binding table, which only the root's do.</param>
    /// <param name="doctypeSeen">Whether a document type declaration has been scanned.</param>
    /// <param name="result">What the attribute list turned out to be.</param>
    /// <returns><see langword="false"/> when the attribute list is provably broken.</returns>
    private static bool TryScanAttributes(
        ReadOnlySpan<byte> body,
        ref int index,
        Span<XmlNameSpan> attributeNames,
        Span<XmlNamespaceBinding> bindings,
        ref int bindingCount,
        bool collectBindings,
        bool doctypeSeen,
        out XmlAttributeScanResult result)
    {
        result = default;
        int nameCount = 0;
        while(true)
        {
            SkipWhitespace(body, ref index);
            if(index == body.Length)
            {
                return false;
            }

            byte current = body[index];
            if(current == (byte)'>')
            {
                index++;

                return true;
            }

            if(current == (byte)'/')
            {
                if(index + 1 == body.Length || body[index + 1] != (byte)'>')
                {
                    return false;
                }

                index += 2;
                result.SelfClosing = true;

                return true;
            }

            if(!IsNameStart(current))
            {
                return false;
            }

            int nameStart = index;
            ReadOnlySpan<byte> name = ReadName(body, ref index);
            for(int i = 0; i < nameCount; i++)
            {
                if(name.SequenceEqual(body[attributeNames[i].Start..attributeNames[i].End]))
                {
                    return false;
                }
            }

            if(nameCount < attributeNames.Length)
            {
                attributeNames[nameCount] = new XmlNameSpan { Start = nameStart, End = index };
                nameCount++;
            }
            else
            {
                result.Uncertified = true;
            }

            SkipWhitespace(body, ref index);
            if(index == body.Length || body[index] != (byte)'=')
            {
                return false;
            }

            index++;
            SkipWhitespace(body, ref index);
            if(index == body.Length)
            {
                return false;
            }

            byte quote = body[index];
            if(quote is not ((byte)'"' or (byte)'\''))
            {
                return false;
            }

            index++;
            int valueStart = index;
            while(true)
            {
                if(index == body.Length)
                {
                    return false;
                }

                byte value = body[index];
                if(value == quote)
                {
                    break;
                }

                if(value == (byte)'<')
                {
                    return false;
                }

                if(value == (byte)'&')
                {
                    if(!TryScanReference(body, ref index, doctypeSeen))
                    {
                        return false;
                    }

                    continue;
                }

                index++;
            }

            int valueEnd = index;
            index++;

            bool declaresDefault = name.SequenceEqual("xmlns"u8);
            bool declaresPrefix = name.Length > 6 && name.StartsWith("xmlns:"u8);
            if(!declaresDefault && !declaresPrefix)
            {
                continue;
            }

            result.DeclaresNamespace = true;
            if(!collectBindings)
            {
                continue;
            }

            if(bindingCount == bindings.Length)
            {
                result.Uncertified = true;
                continue;
            }

            XmlNameSpan prefix = declaresDefault
                ? new XmlNameSpan { Start = nameStart, End = nameStart }
                : new XmlNameSpan { Start = nameStart + 6, End = nameStart + name.Length };

            bindings[bindingCount] = new XmlNamespaceBinding
            {
                Prefix = prefix,
                Uri = new XmlNameSpan { Start = valueStart, End = valueEnd }
            };

            bindingCount++;
        }
    }

    /// <summary>Resolves a prefix through the root element's declarations.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="bindings">The namespace binding table.</param>
    /// <param name="bindingCount">How many bindings the table holds.</param>
    /// <param name="prefix">The prefix to resolve, empty for an unprefixed name.</param>
    /// <param name="namespaceUri">The namespace URI, empty when an unprefixed name has no default declaration.</param>
    /// <returns><see langword="false"/> when a prefix resolves to nothing, which is provably namespace-ill-formed.</returns>
    private static bool TryResolveNamespace(
        ReadOnlySpan<byte> body,
        ReadOnlySpan<XmlNamespaceBinding> bindings,
        int bindingCount,
        ReadOnlySpan<byte> prefix,
        out ReadOnlySpan<byte> namespaceUri)
    {
        for(int i = bindingCount - 1; i >= 0; i--)
        {
            XmlNamespaceBinding binding = bindings[i];
            if(prefix.SequenceEqual(body[binding.Prefix.Start..binding.Prefix.End]))
            {
                namespaceUri = body[binding.Uri.Start..binding.Uri.End];

                return true;
            }
        }

        if(prefix.IsEmpty)
        {
            namespaceUri = default;

            return true;
        }

        if(prefix.SequenceEqual("xml"u8))
        {
            namespaceUri = ReservedXmlNamespace;

            return true;
        }

        namespaceUri = default;

        return false;
    }

    /// <summary>Splits a qualified name at its first colon.</summary>
    /// <param name="name">The qualified name.</param>
    /// <param name="prefix">The prefix, empty when the name carries none.</param>
    /// <param name="localName">The local name.</param>
    private static void SplitQualifiedName(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> localName)
    {
        int colon = name.IndexOf((byte)':');
        if(colon < 0)
        {
            prefix = default;
            localName = name;

            return;
        }

        prefix = name[..colon];
        localName = name[(colon + 1)..];
    }

    /// <summary>Scans one comment.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the comment.</param>
    /// <returns><see langword="false"/> when the comment is unterminated.</returns>
    private static bool TryScanComment(ReadOnlySpan<byte> body, ref int index)
    {
        int cursor = index + 4;
        int end = body[cursor..].IndexOf("-->"u8);
        if(end < 0)
        {
            return false;
        }

        index = cursor + end + 3;

        return true;
    }

    /// <summary>Scans one character data section.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the section.</param>
    /// <returns><see langword="false"/> when the section is unterminated.</returns>
    private static bool TryScanCdata(ReadOnlySpan<byte> body, ref int index)
    {
        int cursor = index + 9;
        int end = body[cursor..].IndexOf("]]>"u8);
        if(end < 0)
        {
            return false;
        }

        index = cursor + end + 3;

        return true;
    }

    /// <summary>
    /// Scans one processing instruction. The target <c>xml</c> in any casing is reserved: it stands only
    /// as the single leading declaration, spelled in lower case.
    /// </summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the instruction.</param>
    /// <param name="declarationAllowed">Whether the position may still carry the XML declaration.</param>
    /// <returns><see langword="false"/> when the instruction is unterminated or claims the reserved target.</returns>
    private static bool TryScanProcessingInstruction(ReadOnlySpan<byte> body, ref int index, bool declarationAllowed)
    {
        int cursor = index + 2;
        ReadOnlySpan<byte> target = ReadName(body, ref cursor);
        if(target.IsEmpty)
        {
            return false;
        }

        if(MatchesAsciiCaseInsensitive(target, "xml"u8))
        {
            bool isDeclaration = declarationAllowed && target.SequenceEqual("xml"u8);
            if(!isDeclaration)
            {
                return false;
            }
        }

        int end = body[cursor..].IndexOf("?>"u8);
        if(end < 0)
        {
            return false;
        }

        index = cursor + end + 2;

        return true;
    }

    /// <summary>Scans one document type declaration, including a quoted identifier or an internal subset.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the declaration.</param>
    /// <returns><see langword="false"/> when the declaration is unterminated.</returns>
    private static bool TryScanDoctype(ReadOnlySpan<byte> body, ref int index)
    {
        int cursor = index + 9;
        bool insideInternalSubset = false;
        while(cursor < body.Length)
        {
            byte current = body[cursor];
            if(current is (byte)'"' or (byte)'\'')
            {
                cursor++;
                while(cursor < body.Length && body[cursor] != current)
                {
                    cursor++;
                }

                if(cursor == body.Length)
                {
                    return false;
                }

                cursor++;
                continue;
            }

            if(current == (byte)'[')
            {
                insideInternalSubset = true;
                cursor++;
                continue;
            }

            if(current == (byte)']')
            {
                insideInternalSubset = false;
                cursor++;
                continue;
            }

            if(current == (byte)'>' && !insideInternalSubset)
            {
                index = cursor + 1;

                return true;
            }

            cursor++;
        }

        return false;
    }

    /// <summary>
    /// Scans one reference. A character reference and the five predefined entities stand on their own;
    /// any other entity name stands only once a document type declaration could have declared it.
    /// </summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, left one past the reference.</param>
    /// <param name="doctypeSeen">Whether a document type declaration has been scanned.</param>
    /// <returns><see langword="false"/> when the reference is provably ill-formed.</returns>
    private static bool TryScanReference(ReadOnlySpan<byte> body, ref int index, bool doctypeSeen)
    {
        int cursor = index + 1;
        if(cursor < body.Length && body[cursor] == (byte)'#')
        {
            cursor++;
            bool hexadecimal = cursor < body.Length && (body[cursor] | 0x20) == (byte)'x';
            if(hexadecimal)
            {
                cursor++;
            }

            int digits = 0;
            while(cursor < body.Length && (hexadecimal ? IsHexadecimalDigit(body[cursor]) : IsAsciiDigit(body[cursor])))
            {
                cursor++;
                digits++;
            }

            if(digits == 0 || cursor == body.Length || body[cursor] != (byte)';')
            {
                return false;
            }

            index = cursor + 1;

            return true;
        }

        ReadOnlySpan<byte> name = ReadName(body, ref cursor);
        if(name.IsEmpty || cursor == body.Length || body[cursor] != (byte)';')
        {
            return false;
        }

        if(!doctypeSeen && !IsPredefinedEntity(name))
        {
            return false;
        }

        index = cursor + 1;

        return true;
    }

    /// <summary>Whether the name is one of the five entities XML predefines.</summary>
    /// <param name="name">The entity name.</param>
    /// <returns><see langword="true"/> for a predefined entity.</returns>
    private static bool IsPredefinedEntity(ReadOnlySpan<byte> name)
    {
        return name.SequenceEqual("amp"u8)
            || name.SequenceEqual("lt"u8)
            || name.SequenceEqual("gt"u8)
            || name.SequenceEqual("quot"u8)
            || name.SequenceEqual("apos"u8);
    }

    /// <summary>Reads a maximal run of name bytes.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The scan position, advanced past the name.</param>
    /// <returns>The name bytes; empty when the position does not start a name.</returns>
    private static ReadOnlySpan<byte> ReadName(ReadOnlySpan<byte> body, ref int index)
    {
        int start = index;
        if(index < body.Length && IsNameStart(body[index]))
        {
            index++;
            while(index < body.Length && IsNameChar(body[index]))
            {
                index++;
            }
        }

        return body[start..index];
    }

    /// <summary>Whether the bytes at the index are the given literal.</summary>
    /// <param name="body">The bytes being scanned.</param>
    /// <param name="index">The position to test at.</param>
    /// <param name="value">The literal to test for.</param>
    /// <returns><see langword="true"/> when the literal is present.</returns>
    private static bool StartsWith(ReadOnlySpan<byte> body, int index, ReadOnlySpan<byte> value)
    {
        return body.Length - index >= value.Length && body.Slice(index, value.Length).SequenceEqual(value);
    }

    /// <summary>Compares a name against a lower-case literal, ASCII case-insensitively.</summary>
    /// <param name="name">The name read from the fragment.</param>
    /// <param name="lowerCaseValue">The literal in lower-case bytes.</param>
    /// <returns><see langword="true"/> when the name is the literal in any casing.</returns>
    private static bool MatchesAsciiCaseInsensitive(ReadOnlySpan<byte> name, ReadOnlySpan<byte> lowerCaseValue)
    {
        if(name.Length != lowerCaseValue.Length)
        {
            return false;
        }

        for(int i = 0; i < name.Length; i++)
        {
            if((name[i] | 0x20) != lowerCaseValue[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the byte can start an XML name. A colon cannot: a qualified name's prefix and local part each start with a name-start byte, so a leading colon is namespace-ill-formed.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a letter, an underscore, or a non-ASCII byte.</returns>
    private static bool IsNameStart(byte value)
    {
        return IsAsciiLetter(value) || value == (byte)'_' || value >= 0x80;
    }

    /// <summary>Whether the byte can continue an XML name. A colon can: it joins a prefix to a local name inside a qualified name, though it can never lead one.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a name-start byte, a digit, a colon, a hyphen, or a full stop.</returns>
    private static bool IsNameChar(byte value)
    {
        return IsNameStart(value) || IsAsciiDigit(value) || value is (byte)':' or (byte)'-' or (byte)'.';
    }

    /// <summary>Whether the byte is an ASCII letter.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>A</c>-<c>Z</c> or <c>a</c>-<c>z</c>.</returns>
    private static bool IsAsciiLetter(byte value)
    {
        return (uint)((value | 0x20) - (byte)'a') <= 'z' - 'a';
    }

    /// <summary>Whether the byte is an ASCII digit.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>0</c>-<c>9</c>.</returns>
    private static bool IsAsciiDigit(byte value)
    {
        return (uint)(value - (byte)'0') <= 9;
    }

    /// <summary>Whether the byte is a hexadecimal digit.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>0</c>-<c>9</c>, <c>A</c>-<c>F</c> or <c>a</c>-<c>f</c>.</returns>
    private static bool IsHexadecimalDigit(byte value)
    {
        return IsAsciiDigit(value) || (uint)((value | 0x20) - (byte)'a') <= 'f' - 'a';
    }
}
