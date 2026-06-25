using System;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Reads an OWL 2 functional-style syntax document
/// (<see href="https://www.w3.org/TR/owl2-syntax/">OWL 2 Structural Specification</see>)
/// into the same structural model the RDF mapping produces — a second
/// front-end for <see cref="OwlOntologyDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole-buffer facade over
/// <see cref="OwlFunctionalSyntaxIncrementalReader"/>: the full text is fed
/// in one chunk and the input declared final, so truncation — an unterminated
/// token or an unbalanced group at the end of the document — is an error.
/// Editors feeding a document as it is typed drive the incremental reader
/// directly, where an unfinished tail is a
/// <see cref="Lumoin.Veritas.Core.Parsing.IncrementalParseStatus.NeedMore"/> status instead.
/// </para>
/// <para>
/// The reader is value-based: malformedness is recorded in the document's
/// diagnostics and reading continues where structure permits. Parsing runs
/// in three explicit-stack passes — tokens, an S-expression tree, then a
/// post-order conversion of constructor groups to axioms and expressions —
/// so arbitrarily nested class expressions never touch the call stack.
/// </para>
/// <para>
/// Axiom-frame annotations attach to the axioms they annotate, nesting
/// included; ontology annotations surface as annotation assertions on the
/// ontology IRI. Anonymous individuals (<c>_:label</c>) map to blank
/// nodes. Diagnostics carry source spans: the offending token's extent
/// for lexical errors, the converting node's extent for structural ones.
/// </para>
/// </remarks>
public static class OwlFunctionalSyntaxReader
{
    /// <summary>
    /// Reads a functional-syntax ontology document from its UTF-8 bytes.
    /// </summary>
    /// <param name="utf8Text">The document's UTF-8 bytes.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public static OwlOntologyDocument Read(ReadOnlySpan<byte> utf8Text)
    {
        OwlFunctionalSyntaxIncrementalReader reader = new();
        reader.Feed(utf8Text);

        return reader.Complete();
    }

    /// <summary>
    /// Reads a functional-syntax ontology document, encoding the text to UTF-8 once at the boundary.
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
