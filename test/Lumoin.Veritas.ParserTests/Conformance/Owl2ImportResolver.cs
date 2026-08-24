using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Computes the imports closure of an OWL 2 conformance test document: the
/// document's quads plus, transitively, the quads of every supplied imported
/// ontology an <c>owl:imports</c> triple actually references. A test that
/// supplies imports the document never references contributes nothing —
/// that distinction is itself under test (<c>WebOnt-imports-002</c> entails
/// less than the conjunction of the two documents precisely because the
/// premise does not import the second).
/// </summary>
internal static class Owl2ImportResolver
{
    /// <summary>The maximum byte length of a rendered merged-import blank-label prefix: the <c>import</c> marker, the widest 32-bit index rendering, and the trailing period.</summary>
    private const int MaxImportPrefixLength = 18;

    /// <summary>
    /// The parsed, unprefixed quads of each distinct supplied import document, keyed by
    /// the supplied ontology's value identity (its IRI together with its document text).
    /// Instances built by separate manifest loads carrying the same document therefore
    /// share one parse. The entries are immutable arrays: every use copies out through
    /// <see cref="PrefixBlankLabels"/> and no use can write into a shared entry.
    /// </summary>
    private static ConcurrentDictionary<Owl2ImportedOntology, ImmutableArray<Quad>> ParsedImports { get; } = new();

    /// <summary>
    /// Expands <paramref name="documentQuads"/> with the transitive
    /// <c>owl:imports</c> closure over the test's supplied ontologies.
    /// Blank-node labels of each merged document are prefixed so labels
    /// minted independently per parse cannot collide across documents.
    /// </summary>
    /// <param name="testCase">The test case whose supplied imports resolve the references.</param>
    /// <param name="documentQuads">The parsed document to expand; returned as-is when it references nothing.</param>
    /// <returns>The quads of the imports closure.</returns>
    /// <exception cref="InvalidOperationException">A referenced ontology is not among the supplied imports, or a supplied document fails to parse.</exception>
    public static List<Quad> Expand(Owl2TestCase testCase, List<Quad> documentQuads)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(documentQuads);

        if(testCase.Imports.Count == 0)
        {
            return documentQuads;
        }

        Dictionary<Utf8String, Owl2ImportedOntology> byIri = [];
        foreach(Owl2ImportedOntology import in testCase.Imports)
        {
            byIri[import.Iri] = import;
        }

        List<Quad> closure = [.. documentQuads];
        HashSet<Utf8String> merged = [];
        Queue<Utf8String> pending = new();
        CollectReferences(documentQuads, pending);

        //Mutual imports cycle back to the starting document itself
        //(the corpus's wine and food ontologies import each other), so the
        //document's own ontology headers count as already merged.
        foreach(Quad quad in documentQuads)
        {
            if(quad.Subject is NamedNode self
                && quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Object is NamedNode type
                && type.Iri.Equals(OwlVocabulary.Ontology))
            {
                merged.Add(self.Iri);
            }
        }

        int documentIndex = 0;
        while(pending.Count > 0)
        {
            Utf8String iri = pending.Dequeue();
            if(!merged.Add(iri))
            {
                continue;
            }

            if(!byIri.TryGetValue(iri, out Owl2ImportedOntology? import))
            {
                throw new InvalidOperationException($"The document imports '{iri}', which the test does not supply.");
            }

            List<Quad> importedQuads = PrefixBlankLabels(ParsedImports.GetOrAdd(import, ParseImported), documentIndex);
            documentIndex++;
            closure.AddRange(importedQuads);
            CollectReferences(importedQuads, pending);
        }

        return closure;
    }

    /// <summary>Enqueues the ontology IRI of every <c>owl:imports</c> reference the quads carry.</summary>
    /// <param name="quads">The quads to scan.</param>
    /// <param name="pending">The queue receiving the referenced ontology IRIs.</param>
    private static void CollectReferences(List<Quad> quads, Queue<Utf8String> pending)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Predicate.Iri.Equals(OwlVocabulary.Imports) && quad.Object is NamedNode target)
            {
                pending.Enqueue(target.Iri);
            }
        }
    }

    /// <summary>
    /// Parses one supplied import document into its unprefixed quads. This is the
    /// <see cref="ParsedImports"/> factory, so it runs at most once per distinct supplied
    /// document and takes its input solely from the key.
    /// </summary>
    /// <param name="import">The supplied import document.</param>
    /// <returns>The parsed quads, with the blank-node labels the parse minted.</returns>
    /// <exception cref="InvalidOperationException">The document fails to parse, or carries no syntax the harness reads.</exception>
    private static ImmutableArray<Quad> ParseImported(Owl2ImportedOntology import)
    {
        if(import.RdfXml is { } rdfXml)
        {
            DiagnosticBag diagnostics = new();
            ImmutableArray<Quad> quads = [.. RdfXmlReader.Read(rdfXml.Memory, diagnostics, baseIri: import.Iri)];
            if(diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Imported ontology '{import.Iri}' did not parse as RDF/XML.");
            }

            return quads;
        }

        if(import.Functional is string functional)
        {
            OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(functional);
            if(document.Diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Imported ontology '{import.Iri}' did not parse as functional syntax.");
            }

            return [.. OwlStructuralToRdf.ToQuads(document)];
        }

        throw new InvalidOperationException($"Imported ontology '{import.Iri}' carries no document in a syntax the harness reads.");
    }

    /// <summary>
    /// Copies one merged import's quads out of the shared parse with every blank-node
    /// label carrying the merge's own prefix, so labels minted independently per parse
    /// cannot collide across merged documents.
    /// </summary>
    /// <param name="quads">The shared unprefixed parse.</param>
    /// <param name="documentIndex">The merge's zero-based position in this expansion.</param>
    /// <returns>The prefixed copy.</returns>
    private static List<Quad> PrefixBlankLabels(ImmutableArray<Quad> quads, int documentIndex)
    {
        Span<byte> prefixBuffer = stackalloc byte[MaxImportPrefixLength];
        ReadOnlySpan<byte> prefix = RenderImportPrefix(documentIndex, prefixBuffer);

        List<Quad> result = new(quads.Length);
        foreach(Quad quad in quads)
        {
            result.Add(quad with
            {
                Subject = PrefixTerm(quad.Subject, prefix),
                Object = PrefixTerm(quad.Object, prefix)
            });
        }

        return result;
    }

    /// <summary>Renders one merge's blank-label prefix — <c>import</c>, the merge index, and a period — as UTF-8 bytes.</summary>
    /// <param name="documentIndex">The merge's zero-based position in this expansion.</param>
    /// <param name="destination">The buffer to render into; at least <see cref="MaxImportPrefixLength"/> bytes.</param>
    /// <returns>The rendered prefix.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="destination"/> is too small for the rendered prefix.</exception>
    private static ReadOnlySpan<byte> RenderImportPrefix(int documentIndex, Span<byte> destination)
    {
        ReadOnlySpan<byte> marker = "import"u8;
        if(destination.Length < MaxImportPrefixLength)
        {
            throw new InvalidOperationException("The merged-import prefix buffer is too small for the rendered prefix.");
        }

        marker.CopyTo(destination);
        int written = marker.Length;
        if(!documentIndex.TryFormat(destination[written..], out int digits, format: default, provider: CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("The merged-import prefix buffer is too small for the merge index.");
        }

        written += digits;
        destination[written] = (byte)'.';
        written++;

        return destination[..written];
    }

    /// <summary>Returns the term with a blank-node label prefixed, or the term itself when it carries no label.</summary>
    /// <param name="term">The term to prefix.</param>
    /// <param name="prefix">The merge's rendered prefix bytes.</param>
    /// <returns>The prefixed term.</returns>
    private static RdfTerm PrefixTerm(RdfTerm term, ReadOnlySpan<byte> prefix)
    {
        return term switch
        {
            BlankNode blank => new BlankNode(PrefixLabel(prefix, blank.Label)),
            _ => term
        };
    }

    /// <summary>
    /// Builds one prefixed blank-node label as a single UTF-8 buffer over the prefix and
    /// the minted label. The label is document-local, so it is built directly rather than
    /// through the shared vocabulary interner.
    /// </summary>
    /// <param name="prefix">The merge's rendered prefix bytes.</param>
    /// <param name="label">The label the parse minted.</param>
    /// <returns>The prefixed label.</returns>
    private static Utf8String PrefixLabel(ReadOnlySpan<byte> prefix, Utf8String label)
    {
        ReadOnlySpan<byte> labelBytes = label.Span;
        byte[] prefixed = new byte[prefix.Length + labelBytes.Length];
        prefix.CopyTo(prefixed);
        labelBytes.CopyTo(prefixed.AsSpan(prefix.Length));

        return new Utf8String(prefixed);
    }
}
