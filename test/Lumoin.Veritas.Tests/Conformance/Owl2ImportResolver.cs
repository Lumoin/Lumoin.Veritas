using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Conformance;

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

        Dictionary<string, Owl2ImportedOntology> byIri = new(StringComparer.Ordinal);
        foreach(Owl2ImportedOntology import in testCase.Imports)
        {
            byIri[import.Iri] = import;
        }

        List<Quad> closure = [.. documentQuads];
        HashSet<string> merged = [];
        Queue<string> pending = new();
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
                merged.Add(self.Iri.ToString());
            }
        }

        int documentIndex = 0;
        while(pending.Count > 0)
        {
            string iri = pending.Dequeue();
            if(!merged.Add(iri))
            {
                continue;
            }

            if(!byIri.TryGetValue(iri, out Owl2ImportedOntology? import))
            {
                throw new InvalidOperationException($"The document imports '{iri}', which the test does not supply.");
            }

            List<Quad> importedQuads = ParseImported(import, "import" + documentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            documentIndex++;
            closure.AddRange(importedQuads);
            CollectReferences(importedQuads, pending);
        }

        return closure;
    }

    private static void CollectReferences(List<Quad> quads, Queue<string> pending)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Predicate.Iri.Equals(OwlVocabulary.Imports) && quad.Object is NamedNode target)
            {
                pending.Enqueue(target.Iri.ToString());
            }
        }
    }

    private static List<Quad> ParseImported(Owl2ImportedOntology import, string blankPrefix)
    {
        List<Quad> quads;
        if(import.RdfXml is string rdfXml)
        {
            DiagnosticBag diagnostics = new();
            quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(rdfXml), diagnostics, baseIri: import.Iri)];
            if(diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Imported ontology '{import.Iri}' did not parse as RDF/XML.");
            }
        }
        else if(import.Functional is string functional)
        {
            OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(functional);
            if(document.Diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Imported ontology '{import.Iri}' did not parse as functional syntax.");
            }

            quads = OwlStructuralToRdf.ToQuads(document);
        }
        else
        {
            throw new InvalidOperationException($"Imported ontology '{import.Iri}' carries no document in a syntax the harness reads.");
        }

        return PrefixBlankLabels(quads, blankPrefix);
    }

    private static List<Quad> PrefixBlankLabels(List<Quad> quads, string prefix)
    {
        List<Quad> result = new(quads.Count);
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

    private static RdfTerm PrefixTerm(RdfTerm term, string prefix)
    {
        return term switch
        {
            BlankNode blank => new BlankNode(Utf8String.FromString(prefix + blank.Label.ToString())),
            _ => term
        };
    }
}
