using System;
using System.Buffers;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Xml;

/// <summary>
/// The Canonical XML writing mechanics a byte-native serializer emits through: the escaping of
/// text content and of attribute values, the end-tag and namespace-declaration writes, the
/// attribute sort-and-write tail, and the name inspection a caller resolves prefixes with. Every
/// write goes into an <see cref="IBufferWriter{T}"/>, so a pooled rental, an
/// <see cref="ArrayBufferWriter{T}"/>, or a pipe all serve as the sink.
/// </summary>
/// <remarks>
/// The mechanics are shared; the walk that drives them is not. Which declarations reach the output,
/// in which order, and against which scope a prefix resolves are the serializer's own decisions —
/// this type holds no namespace scope and keeps no state between calls.
/// </remarks>
public static class XmlCanonicalWriting
{
    /// <summary>The implicit <c>xml</c> prefix: always in scope, and never carried by a declaration in the output.</summary>
    public static Utf8String XmlPrefix { get; } = new("xml"u8.ToArray());

    /// <summary>The namespace IRI the implicit <c>xml</c> prefix is bound to, which is the namespace sort key of an <c>xml:</c> attribute.</summary>
    public static Utf8String XmlNamespaceIri { get; } = new("http://www.w3.org/XML/1998/namespace"u8.ToArray());

    /// <summary>
    /// Writes a byte run with Canonical XML escaping, as text content or as an attribute value. A
    /// byte the mode does not escape passes through verbatim, so a multi-byte UTF-8 sequence is
    /// never split.
    /// </summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="value">The raw UTF-8 bytes; an empty run writes nothing.</param>
    /// <param name="attribute">Whether the value is an attribute value (escapes <c>"</c>, tab, and line feed) rather than text content (escapes <c>&gt;</c>); both escape <c>&amp;</c>, <c>&lt;</c>, and carriage return.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static void WriteEscaped(this IBufferWriter<byte> output, ReadOnlySpan<byte> value, bool attribute)
    {
        ArgumentNullException.ThrowIfNull(output);

        ReadOnlySpan<byte> special = attribute ? "&<\"\t\n\r"u8 : "&<>\r"u8;
        int i = 0;
        while(i < value.Length)
        {
            int next = value.Slice(i).IndexOfAny(special);
            if(next < 0)
            {
                output.Write(value.Slice(i));

                return;
            }

            if(next > 0)
            {
                output.Write(value.Slice(i, next));
            }

            i += next;
            output.Write(Replacement(value[i]));
            i++;
        }
    }

    /// <summary>Writes an element's end tag.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="name">The element's qualified name as written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static void WriteEndTag(this IBufferWriter<byte> output, ReadOnlySpan<byte> name)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.Write("</"u8);
        output.Write(name);
        output.Write(">"u8);
    }

    /// <summary>Writes a namespace declaration onto the tag being opened, with the IRI escaped as an attribute value.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="prefix">The prefix; empty for the default namespace, which is written as <c>xmlns</c>.</param>
    /// <param name="iri">The namespace IRI the prefix is bound to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static void WriteDeclaration(this IBufferWriter<byte> output, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> iri)
    {
        ArgumentNullException.ThrowIfNull(output);

        if(prefix.IsEmpty)
        {
            output.Write(" xmlns=\""u8);
        }
        else
        {
            output.Write(" xmlns:"u8);
            output.Write(prefix);
            output.Write("=\""u8);
        }

        output.WriteEscaped(iri, attribute: true);
        output.Write("\""u8);
    }

    /// <summary>
    /// Sorts an element's non-declaration attributes into Canonical XML attribute order —
    /// namespace IRI, then local name — and writes each as <c> qname="escaped-value"</c>. The list
    /// is sorted in place with an unstable comparison sort, so two entries carrying the same key
    /// keep no guaranteed relative order.
    /// </summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="attributes">The attributes with their resolved sort keys; sorted in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="attributes"/> is <see langword="null"/>.</exception>
    public static void WriteSortedAttributes(this IBufferWriter<byte> output, List<XmlSortedAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(attributes);

        attributes.Sort(static (left, right) =>
        {
            int byNamespace = left.NamespaceIri.Span.SequenceCompareTo(right.NamespaceIri.Span);

            return byNamespace != 0 ? byNamespace : left.LocalName.Span.SequenceCompareTo(right.LocalName.Span);
        });

        foreach(XmlSortedAttribute attribute in attributes)
        {
            output.Write(" "u8);
            output.Write(attribute.Attribute.Name.Span);
            output.Write("=\""u8);
            output.WriteEscaped(attribute.Attribute.Value.Span, attribute: true);
            output.Write("\""u8);
        }
    }

    /// <summary>Whether an attribute name is an <c>xmlns</c> or <c>xmlns:prefix</c> namespace declaration.</summary>
    /// <param name="name">The attribute's qualified name as written.</param>
    /// <returns><see langword="true"/> when the attribute declares a namespace.</returns>
    public static bool IsNamespaceDeclaration(ReadOnlySpan<byte> name)
    {
        return name.SequenceEqual("xmlns"u8) || name.StartsWith("xmlns:"u8);
    }

    /// <summary>
    /// Reads an attribute as a namespace declaration, reporting the binding it carries. The prefix
    /// is sliced out of the attribute name's own memory, so no copy is made. A bare <c>xmlns:</c>
    /// names no prefix and declares nothing.
    /// </summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="binding">The binding the attribute declares; the default-namespace prefix is the empty value.</param>
    /// <returns><see langword="true"/> when the attribute is an <c>xmlns</c> or <c>xmlns:prefix</c> declaration.</returns>
    public static bool TryReadDeclaration(XmlScanAttribute attribute, out XmlNamespaceBinding binding)
    {
        ReadOnlySpan<byte> name = attribute.Name.Span;
        if(name.SequenceEqual("xmlns"u8))
        {
            binding = new XmlNamespaceBinding(default, attribute.Value);

            return true;
        }

        if(name.Length > "xmlns:"u8.Length && name.StartsWith("xmlns:"u8))
        {
            binding = new XmlNamespaceBinding(new Utf8String(attribute.Name.Memory.Slice("xmlns:"u8.Length)), attribute.Value);

            return true;
        }

        binding = default;

        return false;
    }

    /// <summary>The prefix part of a qualified name; empty when the name carries none.</summary>
    /// <param name="qualified">The qualified name as written.</param>
    /// <returns>The prefix bytes.</returns>
    public static ReadOnlySpan<byte> PrefixOf(ReadOnlySpan<byte> qualified)
    {
        int colon = qualified.IndexOf((byte)':');

        return colon < 0 ? default : qualified.Slice(0, colon);
    }

    /// <summary>The local-name part of a qualified name (the part after any prefix), sliced out of the name's own memory.</summary>
    /// <param name="qualified">The qualified name as written.</param>
    /// <returns>The local name.</returns>
    public static Utf8String LocalNameOf(Utf8String qualified)
    {
        int colon = qualified.Span.IndexOf((byte)':');

        return colon < 0 ? qualified : new Utf8String(qualified.Memory.Slice(colon + 1));
    }

    /// <summary>The Canonical XML replacement bytes for an escaped character.</summary>
    /// <param name="character">The character byte to escape.</param>
    /// <returns>The replacement bytes.</returns>
    private static ReadOnlySpan<byte> Replacement(byte character)
    {
        return character switch
        {
            (byte)'&' => "&amp;"u8,
            (byte)'<' => "&lt;"u8,
            (byte)'>' => "&gt;"u8,
            (byte)'"' => "&quot;"u8,
            (byte)'\t' => "&#x9;"u8,
            (byte)'\n' => "&#xA;"u8,
            (byte)'\r' => "&#xD;"u8,
            _ => default
        };
    }
}
