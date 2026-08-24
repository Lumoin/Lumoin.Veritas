using System;
using System.Collections.Immutable;
using System.IO;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Validates <see cref="Owl2ManifestLoader"/> against the vendored corpus
/// census recorded in <c>Material/Owl2/ATTRIBUTION.md</c> — the loader must
/// surface every test case, kind marker, profile marker, and inline document
/// the snapshot carries — and against the corpus contract the loader enforces
/// loudly: a test-case subject is a named IRI and an inline document's bytes
/// are well-formed UTF-8. The contract rows drive synthetic manifests written
/// as raw bytes, because the violations they probe cannot be spelled in the
/// vendored corpus.
/// </summary>
[TestClass]
internal sealed class Owl2ManifestLoaderTests
{
    /// <summary>The approved arm carries the census the attribution records.</summary>
    [TestMethod]
    public void ApprovedArmMatchesCensus()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        Assert.HasCount(355, tests);

        int profileIdentification = 0;
        int consistency = 0;
        int inconsistency = 0;
        int positiveEntailment = 0;
        int negativeEntailment = 0;
        int el = 0;
        int ql = 0;
        int rl = 0;
        int rdfXmlPremise = 0;
        int functionalPremise = 0;

        foreach(Owl2TestCase test in tests)
        {
            profileIdentification += test.Kinds.Contains("ProfileIdentificationTest") ? 1 : 0;
            consistency += test.Kinds.Contains("ConsistencyTest") ? 1 : 0;
            inconsistency += test.Kinds.Contains("InconsistencyTest") ? 1 : 0;
            positiveEntailment += test.Kinds.Contains("PositiveEntailmentTest") ? 1 : 0;
            negativeEntailment += test.Kinds.Contains("NegativeEntailmentTest") ? 1 : 0;
            el += test.Profiles.Contains("EL") ? 1 : 0;
            ql += test.Profiles.Contains("QL") ? 1 : 0;
            rl += test.Profiles.Contains("RL") ? 1 : 0;
            rdfXmlPremise += test.RdfXmlPremise is not null ? 1 : 0;
            functionalPremise += test.FunctionalPremise is not null ? 1 : 0;
        }

        Assert.AreEqual(355, profileIdentification, "Every approved test doubles as a profile-identification test.");
        Assert.AreEqual(237, consistency);
        Assert.AreEqual(118, inconsistency);
        Assert.AreEqual(143, positiveEntailment);
        Assert.AreEqual(9, negativeEntailment);
        Assert.AreEqual(67, el);
        Assert.AreEqual(45, ql);
        Assert.AreEqual(70, rl);
        Assert.AreEqual(335, rdfXmlPremise);
        Assert.AreEqual(60, functionalPremise);
    }

    /// <summary>The proposed arm loads and carries its census count.</summary>
    [TestMethod]
    public void ProposedArmLoads()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "proposed", "all.rdf"));

        Assert.HasCount(86, tests);
    }

    /// <summary>The inline ontology documents are unescaped, parseable XML text, not entity-encoded residue. A document may still contain escaped text of its own — the marker of correct unescaping is the literal root element.</summary>
    [TestMethod]
    public void InlineDocumentsAreUnescaped()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        int withDocument = 0;
        foreach(Owl2TestCase test in tests)
        {
            if(test.RdfXmlPremise is { } premise)
            {
                withDocument++;

                //A document may open with an XML declaration, so the root element is
                //probed anywhere in the text rather than at its start.
                Assert.IsGreaterThanOrEqualTo(0, premise.Span.IndexOf("<rdf:RDF"u8), $"{test.Identifier}: the premise document is RDF/XML text.");
            }
        }

        Assert.AreEqual(335, withDocument);
    }

    /// <summary>Every loaded value that a downstream dictionary or set keys on — the inline documents and the supplied imports' ontology IRIs — carries a precomputed content hash, so a keyed probe never rehashes a whole document.</summary>
    [TestMethod]
    public void KeyedLoadedValuesCarryAPrecomputedHash()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        int documentCount = 0;
        int importCount = 0;
        foreach(Owl2TestCase test in tests)
        {
            if(test.RdfXmlPremise is { } premise)
            {
                documentCount++;

                Assert.IsTrue(premise.HasPrecomputedHash, $"{test.Identifier}: the premise document is hashed once at load, not once per keyed probe.");
            }

            foreach(Owl2ImportedOntology import in test.Imports)
            {
                importCount++;

                Assert.IsTrue(import.Iri.HasPrecomputedHash, $"{test.Identifier}: a supplied import's ontology IRI is hashed once at load, not once per keyed probe.");
                if(import.RdfXml is { } document)
                {
                    Assert.IsTrue(document.HasPrecomputedHash, $"{test.Identifier}: a supplied import's document is hashed once at load, not once per keyed probe.");
                }
            }
        }

        Assert.AreEqual(335, documentCount);
        Assert.IsGreaterThan(0, importCount, "The approved arm supplies imported ontologies, whose record identity keys the import parse cache.");
    }

    /// <summary>An inline document whose bytes are not well-formed UTF-8 refuses loudly instead of folding the ill-formed sequence to a replacement character.</summary>
    [TestMethod]
    public void IllFormedDocumentBytesRefuseLoudly()
    {
        Assert.ThrowsExactly<InvalidOperationException>(LoadIllFormedDocumentProbe, "An inline document carrying ill-formed UTF-8 is a manifest the loader must refuse.");
    }

    /// <summary>A blank-node subject typed <c>test:TestCase</c> refuses loudly: a test-case subject is a named IRI, and the row order sorts on that IRI.</summary>
    [TestMethod]
    public void BlankTestCaseSubjectsRefuseLoudly()
    {
        Assert.ThrowsExactly<InvalidOperationException>(LoadBlankTestCaseSubjectProbe, "A blank-node test-case subject is a manifest the loader must refuse.");
    }

    /// <summary>The bytes preceding the ill-formed sequence in the ill-formed-document probe manifest: a single test case up to its open premise-document element.</summary>
    private static ReadOnlySpan<byte> IllFormedProbeHead => """<?xml version="1.0" encoding="UTF-8"?><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:test="http://www.w3.org/2007/OWL/testOntology#"><test:TestCase rdf:about="urn:veritas-manifest-probe:ill-formed"><test:identifier>veritas-manifest-probe-ill-formed</test:identifier><test:rdfXmlPremiseOntology>"""u8;

    /// <summary>The bytes following the ill-formed sequence in the ill-formed-document probe manifest.</summary>
    private static ReadOnlySpan<byte> IllFormedProbeTail => """</test:rdfXmlPremiseOntology></test:TestCase></rdf:RDF>"""u8;

    /// <summary>The blank-subject probe manifest: one <c>test:TestCase</c> node element with no <c>rdf:about</c>, so the parse mints a blank-node subject for it.</summary>
    private static ReadOnlySpan<byte> BlankTestCaseSubjectProbe => """<?xml version="1.0" encoding="UTF-8"?><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:test="http://www.w3.org/2007/OWL/testOntology#"><test:TestCase><test:identifier>veritas-manifest-probe-blank</test:identifier></test:TestCase></rdf:RDF>"""u8;

    /// <summary>Loads the ill-formed-document probe manifest from its own temporary file, removing the file whether or not the load refuses.</summary>
    private static void LoadIllFormedDocumentProbe()
    {
        LoadProbeManifest("ill-formed-document.rdf", IllFormedDocumentProbe());
    }

    /// <summary>Loads the blank-subject probe manifest from its own temporary file, removing the file whether or not the load refuses.</summary>
    private static void LoadBlankTestCaseSubjectProbe()
    {
        LoadProbeManifest("blank-test-case-subject.rdf", BlankTestCaseSubjectProbe.ToArray());
    }

    /// <summary>
    /// Builds the ill-formed-document probe manifest: a well-formed RDF/XML document whose
    /// premise-document literal carries a lead byte announcing a two-byte sequence followed
    /// by a byte that is not a continuation byte, the shortest sequence no UTF-8 decoding
    /// accepts.
    /// </summary>
    /// <returns>The manifest bytes.</returns>
    private static byte[] IllFormedDocumentProbe()
    {
        ReadOnlySpan<byte> illFormed = [0xC3, 0x28];
        byte[] manifest = new byte[IllFormedProbeHead.Length + illFormed.Length + IllFormedProbeTail.Length];
        IllFormedProbeHead.CopyTo(manifest);
        illFormed.CopyTo(manifest.AsSpan(IllFormedProbeHead.Length));
        IllFormedProbeTail.CopyTo(manifest.AsSpan(IllFormedProbeHead.Length + illFormed.Length));

        return manifest;
    }

    /// <summary>Writes a probe manifest to its own file under a probe directory and loads it, removing the file whether or not the load refuses.</summary>
    /// <param name="fileName">The probe's own file name; each row uses its own so concurrent rows never share a file.</param>
    /// <param name="content">The manifest bytes.</param>
    private static void LoadProbeManifest(string fileName, ReadOnlySpan<byte> content)
    {
        string directory = Path.Combine(Path.GetTempPath(), "veritas-owl2-manifest-probes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, content);
        try
        {
            Owl2ManifestLoader.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
