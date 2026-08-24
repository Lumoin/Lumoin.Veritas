using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Certifies that reusing <see cref="Owl2ImportResolver"/>'s shared import parse cannot
/// corrupt an expansion: an expansion's output is decided by the supplied documents'
/// values, and no expansion leaves a mark on a shared parse that a later expansion reads
/// back.
/// </summary>
/// <remarks>
/// <para>
/// Every row runs against synthetic ontologies whose IRIs appear nowhere in the vendored
/// corpus, so no corpus arm shares the probed parse and no row observes another class's
/// warming. The assertions are structural value equality over the expansion's output —
/// never call counts, never array identity — because the shared parse is concurrently
/// warmed by the corpus arms under method-level parallelism.
/// </para>
/// <para>
/// That assertion vocabulary fixes what the suite certifies. The parse is deterministic,
/// so a shared entry and a fresh parse of the same document produce the same values:
/// value assertions cannot observe whether a parse was shared at all. These rows therefore
/// certify NON-CORRUPTION under reuse — no doubled prefix, no cross-document leakage, no
/// document served in place of another — and never deduplication.
/// </para>
/// </remarks>
[TestClass]
internal sealed class Owl2ImportResolverTests
{
    /// <summary>The importing probe document: an ontology header whose <c>owl:imports</c> names the probe import.</summary>
    private static ReadOnlySpan<byte> PremiseDocument => """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:owl="http://www.w3.org/2002/07/owl#">
          <owl:Ontology rdf:about="urn:veritas-cache-probe:document">
            <owl:imports rdf:resource="urn:veritas-cache-probe:imported"/>
          </owl:Ontology>
        </rdf:RDF>
        """u8;

    /// <summary>The imported probe document: an ontology header and a restriction, so the parse mints a blank-node label the merge must prefix.</summary>
    private static ReadOnlySpan<byte> ImportDocument => """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:rdfs="http://www.w3.org/2000/01/rdf-schema#" xmlns:owl="http://www.w3.org/2002/07/owl#">
          <owl:Ontology rdf:about="urn:veritas-cache-probe:imported"/>
          <owl:Class rdf:about="urn:veritas-cache-probe:A">
            <rdfs:subClassOf>
              <owl:Restriction>
                <owl:onProperty rdf:resource="urn:veritas-cache-probe:p"/>
                <owl:someValuesFrom rdf:resource="urn:veritas-cache-probe:B"/>
              </owl:Restriction>
            </rdfs:subClassOf>
          </owl:Class>
        </rdf:RDF>
        """u8;

    /// <summary>A second imported probe document under the same ontology IRI, distinguished by the marker class only it declares.</summary>
    private static ReadOnlySpan<byte> VariantImportDocument => """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:owl="http://www.w3.org/2002/07/owl#">
          <owl:Ontology rdf:about="urn:veritas-cache-probe:imported"/>
          <owl:Class rdf:about="urn:veritas-cache-probe:VariantMarker"/>
        </rdf:RDF>
        """u8;

    /// <summary>The probe document's own ontology IRI, which is also its parse base.</summary>
    private static ReadOnlySpan<byte> DocumentIri => "urn:veritas-cache-probe:document"u8;

    /// <summary>The probe import's ontology IRI, the IRI the document's <c>owl:imports</c> resolves by.</summary>
    private static ReadOnlySpan<byte> ImportedIri => "urn:veritas-cache-probe:imported"u8;

    /// <summary>An ontology IRI the probe document never imports, for the unsupplied-reference row.</summary>
    private static ReadOnlySpan<byte> UnrelatedIri => "urn:veritas-cache-probe:unrelated"u8;

    /// <summary>The class IRI only the variant import document declares.</summary>
    private static ReadOnlySpan<byte> VariantMarkerIri => "urn:veritas-cache-probe:VariantMarker"u8;

    /// <summary>The blank-label prefix the first merged import of an expansion carries.</summary>
    private static ReadOnlySpan<byte> FirstMergePrefix => "import0."u8;

    /// <summary>A document that references no import is handed back unchanged: the resolver's documented fast path returns the caller's own list.</summary>
    [TestMethod]
    public void DocumentWithoutImportsReturnsTheInputList()
    {
        Owl2TestCase testCase = CaseWithoutImports();
        List<Quad> documentQuads = ParsePremise();

        Assert.AreSame(documentQuads, Owl2ImportResolver.Expand(testCase, documentQuads), "A test case supplying no imports expands to its own document list.");
    }

    /// <summary>Two independently constructed, value-equal test cases expand to quad-for-quad identical closures: whatever an expansion does to the quads it copies out, it leaves the shared parse as it found it, so a second expansion of the same values reads the same closure.</summary>
    [TestMethod]
    public void ValueEqualImportsExpandToIdenticalQuads()
    {
        List<Quad> first = Owl2ImportResolver.Expand(CaseImporting(Mint(ImportDocument)), ParsePremise());
        List<Quad> second = Owl2ImportResolver.Expand(CaseImporting(Mint(ImportDocument)), ParsePremise());

        Assert.HasCount(first.Count, second, "Two expansions of value-equal test cases produce the same number of quads.");
        for(int index = 0; index < first.Count; index++)
        {
            Assert.AreEqual(first[index], second[index], $"Quad {index} of the two expansions differs.");
        }
    }

    /// <summary>Every blank node the closure carries comes from the merged import and carries that merge's prefix, so a shared unprefixed parse never leaks unprefixed labels into an expansion.</summary>
    [TestMethod]
    public void MergedImportBlankLabelsCarryTheImportPrefix()
    {
        List<Quad> expanded = Owl2ImportResolver.Expand(CaseImporting(Mint(ImportDocument)), ParsePremise());

        int blankCount = 0;
        int prefixedCount = 0;
        foreach(Quad quad in expanded)
        {
            blankCount += IsBlank(quad.Subject) ? 1 : 0;
            blankCount += IsBlank(quad.Object) ? 1 : 0;
            prefixedCount += IsPrefixedBlank(quad.Subject) ? 1 : 0;
            prefixedCount += IsPrefixedBlank(quad.Object) ? 1 : 0;
        }

        Assert.IsGreaterThan(0, blankCount, "The imported probe document mints blank-node labels the merge must prefix.");
        Assert.AreEqual(blankCount, prefixedCount, "Every blank node in the closure comes from the merged import and carries its prefix.");
    }

    /// <summary>Two documents supplied under the same ontology IRI expand to their own quads, so an expansion reads the document it was handed rather than another document sharing its IRI.</summary>
    [TestMethod]
    public void SameIriDifferentDocumentsExpandToTheirOwnQuads()
    {
        List<Quad> plain = Owl2ImportResolver.Expand(CaseImporting(Mint(ImportDocument)), ParsePremise());
        List<Quad> variant = Owl2ImportResolver.Expand(CaseImporting(Mint(VariantImportDocument)), ParsePremise());

        Assert.IsFalse(ContainsSubject(plain, VariantMarkerIri), "The plain import document declares no variant marker class.");
        Assert.IsTrue(ContainsSubject(variant, VariantMarkerIri), "The variant import document's own marker class reaches the closure.");
    }

    /// <summary>An <c>owl:imports</c> reference the test supplies no ontology for is an invariant violation of the corpus setup and fails loudly.</summary>
    [TestMethod]
    public void UnsuppliedImportReferenceThrows()
    {
        Assert.ThrowsExactly<InvalidOperationException>(static () => Owl2ImportResolver.Expand(CaseSupplyingUnrelatedImport(), ParsePremise()));
    }

    /// <summary>Parses the importing probe document against its own ontology IRI.</summary>
    /// <returns>The parsed document quads.</returns>
    private static List<Quad> ParsePremise()
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Mint(PremiseDocument).Memory, diagnostics, baseIri: Mint(DocumentIri))];
        Assert.IsFalse(diagnostics.HasErrors, "The importing probe document is a precondition of the rows and must parse.");

        return quads;
    }

    /// <summary>Builds a probe case supplying one import under the probe ontology IRI.</summary>
    /// <param name="importDocument">The inline RDF/XML the import supplies.</param>
    /// <returns>The probe case.</returns>
    private static Owl2TestCase CaseImporting(Utf8String importDocument)
    {
        return ProbeCase([new Owl2ImportedOntology(Mint(ImportedIri), RdfXml: importDocument, Functional: null)]);
    }

    /// <summary>Builds a probe case supplying no imports at all.</summary>
    /// <returns>The probe case.</returns>
    private static Owl2TestCase CaseWithoutImports()
    {
        return ProbeCase([]);
    }

    /// <summary>Builds a probe case whose only supplied import carries an ontology IRI the probe document never references.</summary>
    /// <returns>The probe case.</returns>
    private static Owl2TestCase CaseSupplyingUnrelatedImport()
    {
        return ProbeCase([new Owl2ImportedOntology(Mint(UnrelatedIri), RdfXml: Mint(ImportDocument), Functional: null)]);
    }

    /// <summary>Builds a probe case carrying the given supplied imports and no documents of its own.</summary>
    /// <param name="imports">The supplied imported ontologies.</param>
    /// <returns>The probe case.</returns>
    private static Owl2TestCase ProbeCase(IReadOnlyList<Owl2ImportedOntology> imports)
    {
        return new Owl2TestCase(
            new Uri("urn:veritas-cache-probe:document"),
            Identifier: "veritas-cache-probe",
            Description: string.Empty,
            Kinds: new HashSet<string>(),
            Profiles: new HashSet<string>(),
            Species: new HashSet<string>(),
            Semantics: new HashSet<string>(),
            RdfXmlPremise: null,
            RdfXmlConclusion: null,
            RdfXmlNonConclusion: null,
            RdfXmlInput: null,
            FunctionalPremise: null,
            FunctionalConclusion: null,
            FunctionalNonConclusion: null,
            Imports: imports);
    }

    /// <summary>Copies bytes into a freshly allocated <see cref="Utf8String"/>, so rows comparing independently constructed but value-equal inputs get distinct backing buffers.</summary>
    /// <param name="bytes">The bytes to copy.</param>
    /// <returns>The fresh UTF-8 string.</returns>
    private static Utf8String Mint(ReadOnlySpan<byte> bytes)
    {
        return new Utf8String(bytes.ToArray());
    }

    /// <summary>Reports whether a term is a blank node.</summary>
    /// <param name="term">The term to classify.</param>
    /// <returns><see langword="true"/> when the term is a blank node.</returns>
    private static bool IsBlank(RdfTerm term)
    {
        return term is BlankNode;
    }

    /// <summary>Reports whether a term is a blank node whose label carries the first merge's prefix.</summary>
    /// <param name="term">The term to classify.</param>
    /// <returns><see langword="true"/> when the term is a prefixed blank node.</returns>
    private static bool IsPrefixedBlank(RdfTerm term)
    {
        return term is BlankNode blank && blank.Label.Span.StartsWith(FirstMergePrefix);
    }

    /// <summary>Reports whether any quad's subject is the named node with the given IRI.</summary>
    /// <param name="quads">The quads to scan.</param>
    /// <param name="iri">The IRI as UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when a quad carries the named subject.</returns>
    private static bool ContainsSubject(List<Quad> quads, ReadOnlySpan<byte> iri)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Subject is NamedNode named && named.Iri.Span.SequenceEqual(iri))
            {
                return true;
            }
        }

        return false;
    }
}
