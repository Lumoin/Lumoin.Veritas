using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="SyntacticLocalityModule"/>: the ⊥-locality fixpoint
/// pulls in what the seed signature depends on (superclass chains, domains
/// and ranges, relevance-connected assertions, signature declarations) and
/// leaves the disconnected and the below-signature out — and the reasoning
/// rendezvous hands the widened module to the description-logic seam.
/// </summary>
[TestClass]
internal sealed class SyntacticLocalityModuleTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>Superclass chains of signature classes join round by round; subclasses below the signature and disconnected axioms stay out.</summary>
    [TestMethod]
    public void SuperclassChainJoinsTheModule()
    {
        OwlSubClassOfAxiom seed = SubClassOf("A", "B");
        OwlSubClassOfAxiom bUnderC = SubClassOf("B", "C");
        OwlSubClassOfAxiom cUnderD = SubClassOf("C", "D");
        OwlSubClassOfAxiom eUnderA = SubClassOf("E", "A");
        OwlSubClassOfAxiom disconnected = SubClassOf("F", "G");

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, bUnderC, cUnderD, eUnderA, disconnected),
            [seed]);

        Assert.Contains(seed, module);
        Assert.Contains(bUnderC, module, "B is in the seed signature, so B ⊑ C is no tautology under the ⊥-replacement.");
        Assert.Contains(cUnderD, module, "C joined with B ⊑ C; the chain follows round by round.");
        Assert.DoesNotContain(eUnderA, module, "E is below the signature: reading E as empty makes E ⊑ A a tautology.");
        Assert.DoesNotContain(disconnected, module);
    }

    /// <summary>A domain axiom follows its property into the module and brings its domain class along.</summary>
    [TestMethod]
    public void DomainFollowsTheProperty()
    {
        OwlSubClassOfAxiom seed = new(Reference("A"), new OwlObjectSomeValuesFrom(Property("r"), Reference("B")))
        {
            Origin = Origin("seed"),
        };
        OwlObjectPropertyDomainAxiom domain = new(Property("r"), Reference("H")) { Origin = Origin("domain") };
        OwlSubClassOfAxiom hUnderI = SubClassOf("H", "I");
        OwlObjectPropertyDomainAxiom unrelated = new(Property("s"), Reference("K")) { Origin = Origin("unrelated") };

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, domain, hUnderI, unrelated),
            [seed]);

        Assert.Contains(domain, module, "r is in the seed signature; its domain constraint is content, not tautology.");
        Assert.Contains(hUnderI, module, "H joined with the domain axiom.");
        Assert.DoesNotContain(unrelated, module, "s is outside the signature: the empty role satisfies any domain.");
    }

    /// <summary>Positive assertions join by signature relevance and chain through their individuals; unrelated assertions stay out.</summary>
    [TestMethod]
    public void AssertionsJoinByRelevance()
    {
        OwlSubClassOfAxiom seed = SubClassOf("A", "B");
        OwlClassAssertionAxiom aInstance = new(Reference("A"), Individual("a")) { Origin = Origin("aInstance") };
        OwlObjectPropertyAssertionAxiom link = new(Individual("a"), Named("r"), Individual("b")) { Origin = Origin("link") };
        OwlClassAssertionAxiom connected = new(Reference("F"), Individual("b")) { Origin = Origin("connected") };
        OwlClassAssertionAxiom unrelated = new(Reference("G"), Individual("x")) { Origin = Origin("unrelated") };

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, aInstance, link, connected, unrelated),
            [seed]);

        Assert.Contains(aInstance, module, "A is in the signature.");
        Assert.Contains(link, module, "The individual a joined with its class assertion.");
        Assert.Contains(connected, module, "The individual b joined through the property assertion.");
        Assert.DoesNotContain(unrelated, module, "Nothing of G(x) touches the signature.");
    }

    /// <summary>Declarations of module-signature entities are appended for self-containment; others stay out.</summary>
    [TestMethod]
    public void SignatureDeclarationsAreAppended()
    {
        OwlSubClassOfAxiom seed = SubClassOf("A", "B");
        OwlDeclarationAxiom aDeclaration = new(OwlEntityKind.Class, Named("A")) { Origin = Origin("aDecl") };
        OwlDeclarationAxiom fDeclaration = new(OwlEntityKind.Class, Named("F")) { Origin = Origin("fDecl") };

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, aDeclaration, fDeclaration),
            [seed]);

        Assert.Contains(aDeclaration, module);
        Assert.DoesNotContain(fDeclaration, module);
    }

    /// <summary>A reflexivity axiom is never local — the empty role is not reflexive — so it joins every module.</summary>
    [TestMethod]
    public void ReflexivityIsNeverLocal()
    {
        OwlSubClassOfAxiom seed = SubClassOf("A", "B");
        OwlObjectPropertyCharacteristicAxiom reflexive = new(OwlPropertyCharacteristic.Reflexive, Property("t")) { Origin = Origin("reflexive") };
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("t")) { Origin = Origin("transitive") };

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, reflexive, transitive),
            [seed]);

        Assert.Contains(reflexive, module, "No replacement makes a reflexivity axiom a tautology.");
        Assert.Contains(transitive, module, "t joined the signature with the reflexivity axiom; its transitivity is content now.");
    }

    /// <summary>A transitivity axiom over an out-of-signature property is local on its own — the empty role is vacuously transitive.</summary>
    [TestMethod]
    public void EmptyRoleCharacteristicsAreLocal()
    {
        OwlSubClassOfAxiom seed = SubClassOf("A", "B");
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("t")) { Origin = Origin("transitive") };

        IReadOnlyList<OwlAxiom> module = SyntacticLocalityModule.Extract(
            Document(seed, transitive),
            [seed]);

        Assert.DoesNotContain(transitive, module);
    }

    /// <summary>The rendezvous's beyond-ceiling module is the widened locality module, not just the flagged axioms.</summary>
    [TestMethod]
    public async Task RendezvousModuleIsTheLocalityModule()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId unionOf = Mint(dictionary, "http://www.w3.org/2002/07/owl#unionOf");
        TermId first = Mint(dictionary, RdfVocabulary.Rdf.First.ToString());
        TermId rest = Mint(dictionary, RdfVocabulary.Rdf.Rest.ToString());
        TermId nil = Mint(dictionary, RdfVocabulary.Rdf.Nil.ToString());
        TermId c1 = Mint(dictionary, Example + "c1");
        TermId a = Mint(dictionary, Example + "a");
        TermId b = Mint(dictionary, Example + "b");
        TermId parent = Mint(dictionary, Example + "parent");
        TermId stranger = Mint(dictionary, Example + "stranger");
        TermId strangerParent = Mint(dictionary, Example + "strangerParent");
        TermId union = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("union")));
        TermId list1 = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("list1")));
        TermId list2 = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("list2")));

        //c1 ⊑ (a ∪ b) is the beyond-RL flag; a ⊑ parent rides the flagged
        //signature into the module; stranger ⊑ strangerParent does not.
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(c1, type, owlClass),
                Triple(a, type, owlClass),
                Triple(b, type, owlClass),
                Triple(parent, type, owlClass),
                Triple(stranger, type, owlClass),
                Triple(strangerParent, type, owlClass),
                Triple(c1, subClassOf, union),
                Triple(union, unionOf, list1),
                Triple(list1, first, a),
                Triple(list1, rest, list2),
                Triple(list2, first, b),
                Triple(list2, rest, nil),
                Triple(a, subClassOf, parent),
                Triple(stranger, subClassOf, strangerParent),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(result.Module);
        Assert.Contains(
            axiom => axiom is OwlSubClassOfAxiom
            {
                SubClass: OwlClassReference { Class.Iri: var sub },
                SuperClass: OwlClassReference { Class.Iri: var super },
            } && sub.ToString() == Example + "a" && super.ToString() == Example + "parent",
            result.Module.Axioms,
            "a ⊑ parent depends on the flagged signature and joins the module.");
        Assert.DoesNotContain(
            axiom => axiom is OwlSubClassOfAxiom
            {
                SubClass: OwlClassReference { Class.Iri: var sub },
            } && sub.ToString() == Example + "stranger",
            result.Module.Axioms,
            "stranger ⊑ strangerParent is local: reading stranger as empty makes it a tautology.");
    }

    /// <summary>Builds a document over the given axioms with empty declaration indexes.</summary>
    /// <param name="axioms">The document's axioms.</param>
    /// <returns>The document.</returns>
    private static OwlOntologyDocument Document(params OwlAxiom[] axioms)
    {
        return new OwlOntologyDocument(
            [.. axioms],
            ontologyIri: null,
            new DiagnosticBag(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>());
    }

    /// <summary>A subclass axiom between two named classes.</summary>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(string sub, string super)
    {
        return new OwlSubClassOfAxiom(Reference(sub), Reference(super))
        {
            Origin = Origin(sub + super),
        };
    }

    /// <summary>A named-class reference for the local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The class reference.</returns>
    private static OwlClassReference Reference(string local)
    {
        return new OwlClassReference(Named(local));
    }

    /// <summary>A named object property expression for the local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(Named(local));
    }

    /// <summary>A named individual term for the local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return Named(local);
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(Named(marker), Named("p"), Named("o"), Graph: null);
    }

    /// <summary>Mints an IRI into the dictionary.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    /// <summary>An encoded triple from term identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="object">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }
}
