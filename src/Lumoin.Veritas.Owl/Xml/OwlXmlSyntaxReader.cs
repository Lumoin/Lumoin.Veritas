using System;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>
/// Reads an OWL 2 XML serialization document
/// (<see href="https://www.w3.org/TR/owl2-xml-serialization/">OWL 2 XML Serialization</see>)
/// into the same structural model the RDF mapping and the other OWL text
/// front-ends produce — a front-end for <see cref="OwlOntologyDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole-buffer facade over
/// <see cref="OwlXmlSyntaxIncrementalReader"/>: the full text is fed in one chunk
/// and the input declared final, so truncation — an unterminated tag or an
/// unclosed element at the end of the document — is handled at completion.
/// Editors feeding a document as it is typed drive the incremental reader
/// directly, where an unfinished tail is a
/// <see cref="Lumoin.Veritas.Core.Parsing.IncrementalParseStatus.NeedMore"/> status instead.
/// </para>
/// <para>
/// The reader is value-based: malformedness is recorded in the document's
/// diagnostics and reading continues where structure permits. The OWL/XML
/// element vocabulary mirrors the structural specification one-to-one, so the
/// converter folds each element directly into its axiom or expression without
/// an intervening RDF graph.
/// </para>
/// </remarks>
public static class OwlXmlSyntaxReader
{
    /// <summary>Reads an OWL/XML ontology document from its UTF-8 bytes.</summary>
    /// <param name="utf8Text">The document's UTF-8 bytes.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public static OwlOntologyDocument Read(ReadOnlySpan<byte> utf8Text)
    {
        OwlXmlSyntaxIncrementalReader reader = new();
        reader.Feed(utf8Text);

        return reader.Complete();
    }

    /// <summary>Reads an OWL/XML ontology document, encoding the text to UTF-8 once at the boundary.</summary>
    /// <param name="text">The document text.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static OwlOntologyDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Read(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
