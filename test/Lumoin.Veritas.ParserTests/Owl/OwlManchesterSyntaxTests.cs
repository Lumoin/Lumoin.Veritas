using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Manchester;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The Manchester syntax reader and writer against their oracle. No W3C
/// conformance suite exists for the syntax, so correctness rests on
/// cross-syntax round-trips: every functional-syntax document of the
/// conformance corpus converts to Manchester text whose rendering is its own
/// fixed point, and whose content — modulo the declarations Manchester frame
/// heads imply — matches the original through the functional-syntax writer.
/// </summary>
/// <remarks>
/// Documented expressiveness limits, recorded by the writer as warnings and
/// exercised here: a general class inclusion whose subclass is anonymous, an
/// axiom anchored only on inverse property expressions, an annotated
/// declaration, an annotation assertion on an IRI no frame declares, and
/// n-ary data quantifiers. A second inherent limit is typing: a restriction's
/// property reads as a data property only when a declaration names it, so
/// documents without declarations may re-type under round-trip — the corpus
/// sweep counts such documents instead of asserting on them.
/// </remarks>
[TestClass]
internal sealed class OwlManchesterSyntaxTests
{
    private const string Handwritten = """
        Prefix: : <http://example.org/>
        Prefix: ex: <http://example.org/other#>
        Ontology: <http://example.org/o>
        Annotations: ex:note "the ontology"@en

        Class: :Person
            Annotations: ex:note "a class"
            SubClassOf: Annotations: ex:why "stated" :hasParent some :Person
            SubClassOf: :hasAge exactly 1 and not (:Robot or :Ghost)
            EquivalentTo: :Human that :hasParent only :Person
            DisjointWith: :Rock
            HasKey: :hasId

        Class: :Adult
            SubClassOf: :hasAge some xsd:integer[>= 18]
            DisjointUnionOf: :YoungAdult, :OldAdult

        ObjectProperty: :hasParent
            Domain: :Person
            Range: :Person
            Characteristics: Transitive, Functional
            SubPropertyOf: :hasAncestor
            InverseOf: :hasChild
            SubPropertyChain: :hasParent o :hasParent

        DataProperty: :hasAge
            Domain: :Person
            Range: xsd:integer
            Characteristics: Functional

        DataProperty: :hasId

        AnnotationProperty: ex:note
            Range: xsd:string

        Datatype: :AdultAge
            EquivalentTo: xsd:integer[>= 18, <= 150]

        Individual: :alice
            Types: :Person, :hasPet value :rex
            Facts: :hasParent :bob, :hasAge "43"^^xsd:integer, not :hasParent :rex
            SameAs: ex:alice
            DifferentFrom: :bob

        Individual: _:anon
            Types: :Person

        EquivalentClasses: :Human, :Person
        DisjointClasses: :Person, :Rock, :Ghost
        SameIndividual: :alice, ex:alice
        DifferentIndividuals: :alice, :bob, :rex
        """;

    [TestMethod]
    public void HandwrittenDocumentReadsCleanlyAndCoversTheConstructs()
    {
        OwlOntologyDocument document = OwlManchesterSyntaxReader.Read(Handwritten);

        Assert.IsFalse(document.Diagnostics.HasErrors, Describe(document.Diagnostics));
        Assert.AreEqual("http://example.org/o", document.OntologyIri?.Iri.ToString());

        Assert.Contains(Utf8Strings.From("http://example.org/Person"), document.DeclaredClasses);
        Assert.Contains(Utf8Strings.From("http://example.org/hasAge"), document.DeclaredDataProperties);

        AssertHasAxiom<OwlSubClassOfAxiom>(document);
        AssertHasAxiom<OwlEquivalentClassesAxiom>(document);
        AssertHasAxiom<OwlDisjointClassesAxiom>(document);
        AssertHasAxiom<OwlDisjointUnionAxiom>(document);
        AssertHasAxiom<OwlHasKeyAxiom>(document);
        AssertHasAxiom<OwlObjectPropertyDomainAxiom>(document);
        AssertHasAxiom<OwlObjectPropertyRangeAxiom>(document);
        AssertHasAxiom<OwlObjectPropertyCharacteristicAxiom>(document);
        AssertHasAxiom<OwlSubObjectPropertyOfAxiom>(document);
        AssertHasAxiom<OwlInverseObjectPropertiesAxiom>(document);
        AssertHasAxiom<OwlPropertyChainAxiom>(document);
        AssertHasAxiom<OwlDataPropertyDomainAxiom>(document);
        AssertHasAxiom<OwlDataPropertyRangeAxiom>(document);
        AssertHasAxiom<OwlFunctionalDataPropertyAxiom>(document);
        AssertHasAxiom<OwlAnnotationPropertyRangeAxiom>(document);
        AssertHasAxiom<OwlDatatypeDefinitionAxiom>(document);
        AssertHasAxiom<OwlClassAssertionAxiom>(document);
        AssertHasAxiom<OwlObjectPropertyAssertionAxiom>(document);
        AssertHasAxiom<OwlDataPropertyAssertionAxiom>(document);
        AssertHasAxiom<OwlNegativeObjectPropertyAssertionAxiom>(document);
        AssertHasAxiom<OwlSameIndividualAxiom>(document);
        AssertHasAxiom<OwlDifferentIndividualsAxiom>(document);
        AssertHasAxiom<OwlAnnotationAssertionAxiom>(document);

        //The annotated subclass axiom carries its annotation.
        OwlSubClassOfAxiom annotated = document.Axioms.OfType<OwlSubClassOfAxiom>().First(a => !a.Annotations.IsDefaultOrEmpty);
        Assert.AreEqual("http://example.org/other#why", annotated.Annotations[0].Property.Iri.ToString());
    }

    [TestMethod]
    public void HandwrittenDocumentSurvivesTheTextRoundTrip()
    {
        OwlOntologyDocument document = OwlManchesterSyntaxReader.Read(Handwritten);
        DiagnosticBag writeBag = new();
        string first = OwlManchesterSyntaxWriter.Write(document, writeBag);

        Assert.IsFalse(writeBag.HasErrors);
        Assert.IsEmpty(writeBag.Diagnostics, Describe(writeBag));

        OwlOntologyDocument reread = OwlManchesterSyntaxReader.Read(first);
        Assert.IsFalse(reread.Diagnostics.HasErrors, $"{Describe(reread.Diagnostics)}\n{first}");

        DiagnosticBag secondBag = new();
        Assert.AreEqual(first, OwlManchesterSyntaxWriter.Write(reread, secondBag), "the rendering is not a fixed point");

        //Content equality through the functional-syntax writer, modulo the
        //declarations Manchester frame heads imply.
        Assert.AreSequenceEqual(
            NonDeclarationLines(document).ToList(),
            NonDeclarationLines(reread).ToList());
    }

    [TestMethod]
    public void CorpusDocumentsRoundTripThroughManchester()
    {
        int clean = 0;
        int gapped = 0;
        int retyped = 0;

        foreach(string status in (string[])["approved", "proposed"])
        {
            foreach(Owl2TestCase testCase in Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", status, "all.rdf")))
            {
                foreach(string? text in (string?[])[testCase.FunctionalPremise, testCase.FunctionalConclusion, testCase.FunctionalNonConclusion])
                {
                    if(text is null)
                    {
                        continue;
                    }

                    OwlOntologyDocument original = OwlFunctionalSyntaxReader.Read(text);
                    if(original.Diagnostics.HasErrors)
                    {
                        continue;
                    }

                    DiagnosticBag writeBag = new();
                    string manchester = OwlManchesterSyntaxWriter.Write(original, writeBag);

                    OwlOntologyDocument reread = OwlManchesterSyntaxReader.Read(manchester);
                    Assert.IsFalse(reread.Diagnostics.HasErrors, $"{testCase.Identifier}: the Manchester rendering did not read back cleanly.\n{Describe(reread.Diagnostics)}\n{manchester}");

                    //The rendering is its own fixed point, expressibility
                    //gaps or not: what survived the first write is stable.
                    DiagnosticBag secondBag = new();
                    string second = OwlManchesterSyntaxWriter.Write(reread, secondBag);
                    Assert.AreEqual(manchester, second, $"{testCase.Identifier}: the rendering is not a fixed point.");

                    if(writeBag.Diagnostics.Count > 0)
                    {
                        gapped++;

                        continue;
                    }

                    //Full content equality holds when nothing was skipped and
                    //no property re-typed for lack of a declaration.
                    List<string> expected = NonDeclarationLines(original).ToList();
                    List<string> actual = NonDeclarationLines(reread).ToList();
                    if(expected.SequenceEqual(actual))
                    {
                        clean++;
                    }
                    else
                    {
                        retyped++;
                    }
                }
            }
        }

        Assert.IsGreaterThan(50, clean, $"clean={clean} gapped={gapped} retyped={retyped}");

        //The corpus is dominated by documents Manchester expresses fully; the
        //gap and re-typing tails stay small and observed.
        Assert.IsGreaterThan(gapped + retyped, clean, $"clean={clean} gapped={gapped} retyped={retyped}");
    }

    [TestMethod]
    public void InexpressibleAxiomsAreRecordedNotThrown()
    {
        //A general class inclusion: both sides anonymous.
        const string Document = """
            Prefix( : = <http://example.org/> )
            Ontology( <http://example.org/o>
              SubClassOf( ObjectIntersectionOf( :A :B ) ObjectUnionOf( :C :D ) )
            )
            """;

        OwlOntologyDocument structural = OwlFunctionalSyntaxReader.Read(Document);
        Assert.IsFalse(structural.Diagnostics.HasErrors);

        DiagnosticBag bag = new();
        string manchester = OwlManchesterSyntaxWriter.Write(structural, bag);

        Assert.HasCount(1, bag.Diagnostics);
        Assert.IsFalse(bag.HasErrors, "inexpressibility is a warning, not an error");
        Assert.IsFalse(manchester.Contains("SubClassOf:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StatusFollowsTheEditorContract()
    {
        OwlManchesterSyntaxIncrementalReader reader = new();

        //A name at the buffer end may still grow, so a feed ends at a
        //delimiter wherever the boundary status is asserted.
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed("Prefix: : <http://example.org/> Ontology: <http://example.org/o>"u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(" Class: "u8));
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(":Person "u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("SubClassOf: "u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(":hasParent some "u8));
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(":Person "u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("and "u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("("u8));
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("not :Rock"u8));
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(")"u8));

        Assert.IsFalse(reader.Diagnostics.HasErrors, "an unfinished tail must never surface as a diagnostic");

        OwlOntologyDocument document = reader.Complete();
        Assert.IsFalse(document.Diagnostics.HasErrors, Describe(document.Diagnostics));
    }

    [TestMethod]
    public void EveryCutPointResumesToTheWholeBufferResult()
    {
        OwlOntologyDocument whole = OwlManchesterSyntaxReader.Read(Handwritten);
        DiagnosticBag wholeBag = new();
        string rendering = OwlManchesterSyntaxWriter.Write(whole, wholeBag);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Handwritten);
        for(int cut = 0; cut <= bytes.Length; cut += 3)
        {
            OwlManchesterSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"cut {cut}: a clean prefix reported an error");

            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: the resumed parse reported an error");
            Assert.HasCount(whole.Axioms.Length, resumed.Axioms, $"cut {cut}: axiom count differs");

            DiagnosticBag bag = new();
            Assert.AreEqual(rendering, OwlManchesterSyntaxWriter.Write(resumed, bag), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void CompleteOnTruncatedInputReportsTruncationErrors()
    {
        const string Truncated = "Prefix: : <http://example.org/> Ontology: Class: :A SubClassOf: :hasParent some (:B and";

        OwlManchesterSyntaxIncrementalReader reader = new();
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(System.Text.Encoding.UTF8.GetBytes(Truncated)));
        Assert.IsFalse(reader.Diagnostics.HasErrors, "incompleteness squiggled before Complete");

        OwlOntologyDocument document = reader.Complete();

        Assert.IsTrue(document.Diagnostics.HasErrors, "Complete did not turn the unbalanced group into an error");
    }

    [TestMethod]
    public void MidTokenTailsSuspendWithoutDiagnostics()
    {
        foreach(string tail in (string[])
        [
            "Ontology: <http://example.org/unterminated",
            "Ontology: Class: :A SubClassOf: \"unterminated",
            "Ontology: Class: :A SubClassOf: :hasAge exactly",
            "Ontology: Class: :A SubClassOf: :p some :B and",
            "Ontology: Individual: :a Facts: :hasAge 4",
            "Ontology: Individual: :a Facts: :hasAge 4.",
            "Ontolog"
        ])
        {
            OwlManchesterSyntaxIncrementalReader reader = new();

            Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(System.Text.Encoding.UTF8.GetBytes(tail)), tail);
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"'{tail}' squiggled a merely-unfinished tail");
        }
    }

    [TestMethod]
    public void SpansCountUtf8BytesAcrossChunkBoundaries()
    {
        //The two-byte character in the comment shifts byte offsets ahead of
        //character offsets; the offending character sits on the second line.
        const string Document = "#ä\nOntology: %";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        int percent = System.Array.IndexOf(bytes, (byte)'%');
        int split = percent - 1;

        OwlManchesterSyntaxIncrementalReader reader = new();
        reader.Feed(bytes.AsSpan(0, split));
        reader.Feed(bytes.AsSpan(split));
        OwlOntologyDocument document = reader.Complete();

        Assert.IsTrue(document.Diagnostics.HasErrors);
        Diagnostic diagnostic = document.Diagnostics.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.AreEqual(1, diagnostic.Span.StartLine);
        Assert.AreEqual(percent, diagnostic.Span.StartByte);
    }

    private static void AssertHasAxiom<TAxiom>(OwlOntologyDocument document)
        where TAxiom : OwlAxiom
    {
        Assert.IsNotEmpty(document.Axioms.OfType<TAxiom>(), $"missing {typeof(TAxiom).Name}");
    }

    //The functional-syntax rendering's axiom lines, declarations excluded and
    //sorted: axiom content equality independent of frame regrouping.
    private static IEnumerable<string> NonDeclarationLines(OwlOntologyDocument document)
    {
        return OwlFunctionalSyntaxWriter.Write(document)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("Declaration(", StringComparison.Ordinal)
                && !line.StartsWith("Ontology(", StringComparison.Ordinal) && line != ")")
            .OrderBy(line => line, StringComparer.Ordinal);
    }

    private static string Describe(DiagnosticBag bag)
    {
        return string.Join("\n", bag.Diagnostics.Select(d => $"{d.Severity}: {d.Message}"));
    }
}
