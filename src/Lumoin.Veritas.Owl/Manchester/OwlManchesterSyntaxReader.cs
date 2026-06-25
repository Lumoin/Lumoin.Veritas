using System;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// Reads an OWL 2 Manchester-syntax document
/// (<see href="https://www.w3.org/TR/owl2-manchester-syntax/">OWL 2 Manchester Syntax</see>)
/// into the same structural model the functional-syntax and RDF mappings
/// produce — a third front-end for <see cref="OwlOntologyDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole-buffer facade over
/// <see cref="OwlManchesterSyntaxIncrementalReader"/>: the full text is fed
/// in one chunk and the input declared final, so truncation — an unterminated
/// token or an unbalanced group at the end of the document — is an error.
/// Editors feeding a document as it is typed drive the incremental reader
/// directly, where an unfinished tail is a
/// <see cref="Lumoin.Veritas.Core.Parsing.IncrementalParseStatus.NeedMore"/> status instead.
/// </para>
/// <para>
/// The reader is value-based: malformedness is recorded in the document's
/// diagnostics and reading continues where structure permits. Frames convert
/// in two passes — entity frames declare their subjects first, then sections
/// become axioms — so a restriction's property reads as a data property
/// exactly when a declaration names it, the typing rule the Manchester note
/// delegates to declarations. The built-in <c>rdf:</c>, <c>rdfs:</c>,
/// <c>xsd:</c>, and <c>owl:</c> prefixes are always available; a bare name
/// resolves through the default prefix.
/// </para>
/// </remarks>
public static class OwlManchesterSyntaxReader
{
    /// <summary>
    /// Reads a Manchester-syntax ontology document from its UTF-8 bytes.
    /// </summary>
    /// <param name="utf8Text">The document's UTF-8 bytes.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public static OwlOntologyDocument Read(ReadOnlySpan<byte> utf8Text)
    {
        OwlManchesterSyntaxIncrementalReader reader = new();
        reader.Feed(utf8Text);

        return reader.Complete();
    }

    /// <summary>
    /// Reads a Manchester-syntax ontology document, encoding the text to UTF-8 once at the boundary.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static OwlOntologyDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Read(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
