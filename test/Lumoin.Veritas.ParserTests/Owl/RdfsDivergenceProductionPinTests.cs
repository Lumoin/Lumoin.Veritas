using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The D-RDFS divergence pin and the Advance carry/invalidate production pin. The
/// first certifies what the reasoned MUTABLE lane serves for an RDFS-shaped input:
/// the maintained lane routes RDFS-shaped content through the RL closure (there is
/// no incremental RDFS pass), so it serves a KNOWN semantic superset of what an
/// immutable RDFS open would derive. The pin computes both derived sets over one
/// hand-built RDFS-shaped corpus (verified RDFS-shaped through the rendezvous
/// floor), asserts the RL set is a superset, and asserts the difference is EXACTLY
/// the enumerated families — the datatype-hierarchy seeds, the <c>scm-cls</c>
/// reflexive/<c>owl:Thing</c>/<c>owl:Nothing</c> rows plus the <c>scm-sco</c> and
/// <c>cax-sco</c> owl:Thing rows they cascade, the <c>eq-ref</c> reflexive
/// equalities of the corpus terms, and the <c>scm-op</c> self-subsumption pair of
/// the declared object property — with any residue outside them failing loudly. It then opens a reasoned mutable engine over the same corpus and
/// confirms the served store answers exactly the RL closure, strictly more than the
/// RDFS materialization. The second pin drives the first production exercise of the
/// floor carry/invalidate logic: an assertion-only maintained commit leaves the
/// detected profiles unchanged (the floor carried, no re-detection), and a
/// schema-touching commit refreshes them beyond RL (a non-empty module, the RL
/// membership withdrawn).
/// </summary>
[TestClass]
internal sealed class RdfsDivergenceProductionPinTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example-namespace prefix the corpus IRIs share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdfs:subClassOf</c> IRI.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The <c>owl:Class</c> IRI.</summary>
    private const string OwlClass = "http://www.w3.org/2002/07/owl#Class";

    /// <summary>The <c>owl:ObjectProperty</c> IRI.</summary>
    private const string OwlObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";

    /// <summary>The <c>owl:unionOf</c> IRI.</summary>
    private const string OwlUnionOf = "http://www.w3.org/2002/07/owl#unionOf";

    /// <summary>The <c>rdf:first</c> IRI.</summary>
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>The <c>rdf:rest</c> IRI.</summary>
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The <c>rdf:nil</c> IRI.</summary>
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>
    /// The RL closure the mutable maintained lane serves over an RDFS-shaped corpus
    /// diverges from the immutable RDFS materialization by exactly the enumerated
    /// families. The corpus is a two-class hierarchy with declared classes, an
    /// object property with a domain and range on an UNdeclared class, and an
    /// instance — every axiom is inside the RDFS streaming pass's shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Corpus: <c>Dog a owl:Class</c>, <c>Animal a owl:Class</c>,
    /// <c>Dog ⊑ Animal</c>, <c>locatedIn a owl:ObjectProperty</c>,
    /// <c>locatedIn domain Place</c>, <c>locatedIn range Place</c>,
    /// <c>rex a Dog</c>, <c>rex locatedIn berlin</c>. <c>Place</c> is used as a
    /// class through the domain/range but is NOT declared <c>owl:Class</c>, so
    /// <c>scm-cls</c> never mints its rows and <c>scm-dom1</c>/<c>scm-rng1</c> find
    /// no super to propagate through: the domain/range axioms are present yet add no
    /// RL-only edge, keeping the difference exactly derivable.
    /// </para>
    /// <para>
    /// Immutable RDFS derives (rdfs2/3/9/11): <c>rex a Animal</c>,
    /// <c>rex a Place</c>, <c>berlin a Place</c> — no reflexives, no owl:Thing,
    /// no datatype seeds.
    /// </para>
    /// <para>
    /// The RL closure adds, beyond RDFS and beyond the base:
    /// </para>
    /// <list type="bullet">
    /// <item>the datatype-hierarchy SEEDS — the built-in <c>xsd</c> subclass edges
    /// and <c>rdfs:Datatype</c> typings entailed by the empty graph, which the RDFS
    /// pass never seeds (isolated here as <c>OwlRlClosure.Compute([]).Derived</c>);</item>
    /// <item>the <c>scm-cls</c> rows for each declared class <c>Dog</c>, <c>Animal</c>:
    /// <c>C ⊑ C</c>, <c>C ≡ C</c>, <c>C ⊑ owl:Thing</c>, <c>owl:Nothing ⊑ C</c> —
    /// eight rows;</item>
    /// <item>the <c>scm-sco</c> row <c>owl:Nothing ⊑ owl:Thing</c> (from
    /// <c>owl:Nothing ⊑ Dog</c> composed with <c>Dog ⊑ owl:Thing</c>);</item>
    /// <item>the <c>cax-sco</c> row <c>rex a owl:Thing</c> (from <c>rex a Dog</c>
    /// composed with the <c>scm-cls</c> edge <c>Dog ⊑ owl:Thing</c>);</item>
    /// <item>the <c>eq-ref</c> reflexive <c>owl:sameAs</c> of each corpus term —
    /// <c>Dog</c>, <c>Animal</c>, <c>Place</c>, <c>locatedIn</c>, <c>rex</c>,
    /// <c>berlin</c> — six rows, the vocabulary terms' own reflexives sitting in
    /// the seed class;</item>
    /// <item>the <c>scm-op</c> self-subsumption pair of the declared object
    /// property: <c>locatedIn ⊑ locatedIn</c> under <c>rdfs:subPropertyOf</c> and
    /// <c>locatedIn ≡ locatedIn</c> under <c>owl:equivalentProperty</c>.</item>
    /// </list>
    /// <para>
    /// So the non-seed difference is exactly seventeen rows. The
    /// <c>scm-sco</c>/<c>scm-eqc</c> mutual-subclass equivalents and the non-reflexive
    /// <c>eq-*</c> family stay empty: the strict hierarchy has no cycle and the corpus
    /// no asserted <c>owl:sameAs</c>, and the <c>cax-sco</c> coverage otherwise
    /// coincides with RDFS (every non-Thing typing RL derives, RDFS derives too). Any
    /// row outside these families is residue the
    /// <see cref="Assert.IsTrue(bool, string)"/> below fails on, per D-RDFS.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task RdfsShapedCorpusRlServesExactlyTheEnumeratedDivergenceOverRdfs()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);

        TermId dog = OwlRlBatteryHelpers.Mint(dictionary, "Dog");
        TermId animal = OwlRlBatteryHelpers.Mint(dictionary, "Animal");
        TermId place = OwlRlBatteryHelpers.Mint(dictionary, "Place");
        TermId locatedIn = OwlRlBatteryHelpers.Mint(dictionary, "locatedIn");
        TermId rex = OwlRlBatteryHelpers.Mint(dictionary, "rex");
        TermId berlin = OwlRlBatteryHelpers.Mint(dictionary, "berlin");
        TermId objectProperty = OwlRlBatteryHelpers.Named(dictionary, OwlObjectProperty);

        List<EncodedTriple> baseTriples =
        [
            OwlRlBatteryHelpers.Triple(dog, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(animal, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(dog, terms.SubClassOf, animal),
            OwlRlBatteryHelpers.Triple(locatedIn, terms.Type, objectProperty),
            OwlRlBatteryHelpers.Triple(locatedIn, terms.Domain, place),
            OwlRlBatteryHelpers.Triple(locatedIn, terms.Range, place),
            OwlRlBatteryHelpers.Triple(rex, terms.Type, dog),
            OwlRlBatteryHelpers.Triple(rex, locatedIn, berlin),
        ];

        //The corpus is RDFS-shaped: the rendezvous floor confirms it, so an immutable
        //open would take the streaming pass this pin's RL serving diverges from.
        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(baseTriples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        await rendezvous.MaterializeAsync(store, dictionary, cancellationToken: cancellationToken).ConfigureAwait(false);
        ReasoningFloor? floor = rendezvous.FloorFor(store);
        Assert.IsNotNull(floor, "The rendezvous detects the floor once per generation.");
        Assert.IsTrue(floor.IsRdfsShaped, "The corpus must be RDFS-shaped, so an immutable open would take the RDFS streaming pass.");

        RdfsVocabularyTerms rdfsTerms = new(terms.Type, terms.SubClassOf, terms.SubPropertyOf, terms.Domain, terms.Range);
        HashSet<EncodedTriple> rlDerived = [.. OwlRlClosure.Compute(baseTriples, terms, oracle, cancellationToken: cancellationToken).Derived];
        HashSet<EncodedTriple> rdfsDerived = [.. RdfsMaterialization.MaterializeToFixpoint(baseTriples, rdfsTerms, cancellationToken: cancellationToken)];
        HashSet<EncodedTriple> seeds = [.. OwlRlClosure.Compute([], terms, oracle, cancellationToken: cancellationToken).Derived];

        //RL is a superset on the shared fragment: every RDFS consequence is an RL one.
        Assert.IsTrue(rlDerived.IsSupersetOf(rdfsDerived), "The RL closure must derive every RDFS consequence.");

        //The seed set is entirely RL-only versus RDFS — the RDFS pass never seeds the axiomatic table.
        HashSet<EncodedTriple> seedsInRdfs = [.. seeds];
        seedsInRdfs.IntersectWith(rdfsDerived);
        Assert.IsEmpty(seedsInRdfs, "The RDFS materialization never derives an axiomatic seed.");

        HashSet<EncodedTriple> difference = [.. rlDerived];
        difference.ExceptWith(rdfsDerived);
        Assert.IsTrue(difference.IsSupersetOf(seeds), "Every axiomatic seed is part of the RL-minus-RDFS divergence.");

        HashSet<EncodedTriple> nonSeedDifference = [.. difference];
        nonSeedDifference.ExceptWith(seeds);

        HashSet<EncodedTriple> expected =
        [
            OwlRlBatteryHelpers.Triple(dog, terms.SubClassOf, dog),
            OwlRlBatteryHelpers.Triple(dog, terms.EquivalentClass, dog),
            OwlRlBatteryHelpers.Triple(dog, terms.SubClassOf, terms.Thing),
            OwlRlBatteryHelpers.Triple(terms.Nothing, terms.SubClassOf, dog),
            OwlRlBatteryHelpers.Triple(animal, terms.SubClassOf, animal),
            OwlRlBatteryHelpers.Triple(animal, terms.EquivalentClass, animal),
            OwlRlBatteryHelpers.Triple(animal, terms.SubClassOf, terms.Thing),
            OwlRlBatteryHelpers.Triple(terms.Nothing, terms.SubClassOf, animal),

            //owl:Nothing under owl:Thing is NOT enumerated here: with the
            //axiomatic vocabulary seeds typing both as owl:Class, the row
            //derives from the empty graph and sits in the seed class.
            OwlRlBatteryHelpers.Triple(rex, terms.Type, terms.Thing),

            OwlRlBatteryHelpers.Triple(dog, terms.SameAs, dog),
            OwlRlBatteryHelpers.Triple(animal, terms.SameAs, animal),
            OwlRlBatteryHelpers.Triple(place, terms.SameAs, place),
            OwlRlBatteryHelpers.Triple(locatedIn, terms.SameAs, locatedIn),
            OwlRlBatteryHelpers.Triple(rex, terms.SameAs, rex),
            OwlRlBatteryHelpers.Triple(berlin, terms.SameAs, berlin),

            OwlRlBatteryHelpers.Triple(locatedIn, terms.SubPropertyOf, locatedIn),
            OwlRlBatteryHelpers.Triple(locatedIn, terms.EquivalentProperty, locatedIn),
        ];

        Assert.IsTrue(
            nonSeedDifference.SetEquals(expected),
            $"The non-seed RL-minus-RDFS divergence must be exactly the enumerated families. {Describe(expected, nonSeedDifference)}");

        //Every non-seed divergence row is classifiable into an enumerated family; a
        //row that is none of them is residue that must fail the pin, per D-RDFS.
        foreach(EncodedTriple triple in nonSeedDifference)
        {
            Assert.IsTrue(
                IsEnumeratedFamily(triple, terms),
                $"A divergence row is outside the enumerated families: ({triple.Subject.Encoded} {triple.Predicate.Encoded} {triple.Object.Encoded}).");
        }

        //The reasoned mutable engine serves exactly the RL closure over this RDFS-shaped
        //corpus — strictly more than the RDFS materialization — the observable form of D-RDFS.
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(Decode(dictionary, baseTriples), cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> served = await ProductionPathServedReader
            .ReadServedAsync(database, cancellationToken).ConfigureAwait(false);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> servedPortable = ProductionPathServedReader.PortablePortion(served);

        HashSet<(RdfTerm, RdfTerm, RdfTerm)> rlServed = DecodeToTermSet(dictionary, baseTriples, rlDerived);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> rdfsServed = DecodeToTermSet(dictionary, baseTriples, rdfsDerived);

        Assert.AreEqual(0, ProductionPathServedReader.DictionaryScopedCount(served), "The corpus has no transitive property, so the served closure carries no dictionary-scoped nodes.");
        Assert.IsTrue(servedPortable.SetEquals(rlServed), "The mutable lane serves exactly the RL closure over the RDFS-shaped corpus.");
        Assert.IsTrue(servedPortable.IsSupersetOf(rdfsServed), "The served RL closure is a superset of the RDFS materialization.");
        Assert.IsFalse(rdfsServed.SetEquals(servedPortable), "The served RL closure strictly exceeds the RDFS materialization — the divergence is observable.");
    }

    /// <summary>
    /// The first production exercise of the floor carry/invalidate logic through a
    /// reasoned mutable engine. An assertion-only commit carries the floor — the
    /// detected profiles are unchanged generation-to-generation, no re-detection — and
    /// a schema-touching commit that introduces a union on the superclass side
    /// refreshes them: the RL membership withdraws and a beyond-RL module is extracted.
    /// </summary>
    [TestMethod]
    public async Task AssertionOnlyCommitCarriesFloorAndSchemaCommitRefreshesIt()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlDeclarationsGraph(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance opened = database.ReasoningProvenance!;
        Assert.IsTrue(opened.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The declarations-and-hierarchy open is within RL.");
        Assert.AreEqual(0, opened.ModuleAxiomCount, "A within-RL floor extracts no beyond-ceiling module.");
        OwlProfiles openedProfiles = opened.DetectedProfiles;

        //An assertion-only commit does not move the floor — a plain individual typing is
        //within every profile grammar — so the detected profiles carry unchanged.
        await database
            .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        ReasoningProvenance carried = database.ReasoningProvenance!;
        Assert.AreEqual(openedProfiles, carried.DetectedProfiles, "The assertion-only commit carries the floor: the detected profiles are unchanged.");
        Assert.IsTrue(carried.IsConsistent, "The assertion-only commit stays consistent.");
        Assert.AreEqual(0, carried.ModuleAxiomCount, "No re-detection ran, so no module is extracted.");

        //A schema-touching commit introducing the union structure c1 ⊑ (a ∪ b) refreshes
        //the floor: it is beyond the RL grammar, so the RL membership withdraws and the
        //re-detected floor extracts a beyond-RL module.
        await database
            .UpdateAsync(Utf8Strings.From(UnionStructureUpdate()), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        ReasoningProvenance refreshed = database.ReasoningProvenance!;
        Assert.IsFalse(refreshed.DetectedProfiles.HasFlag(OwlProfiles.Rl), "The union structure withdraws the RL membership — the floor refreshed, not merely invalidated a cache.");
        Assert.IsGreaterThan(0, refreshed.ModuleAxiomCount, "The re-detected beyond-RL floor extracts a non-empty module.");
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, refreshed.Reason, "The beyond-RL floor delegates to the description-logic seam.");
    }

    /// <summary>Whether a divergence row falls into one of the D-RDFS enumerated families — the seeds are excluded before this classifier runs.</summary>
    /// <param name="triple">The non-seed divergence row.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns><see langword="true"/> when the row is a recognised <c>scm-cls</c>/<c>scm-sco</c>/<c>cax-sco</c> owl:Thing/owl:Nothing, <c>eq-ref</c> reflexive-equality, or <c>scm-op</c> self-subsumption family member.</returns>
    private static bool IsEnumeratedFamily(EncodedTriple triple, OwlRlTerms terms)
    {
        bool subClassReflexive = triple.Predicate == terms.SubClassOf && triple.Subject == triple.Object;
        bool equivClassReflexive = triple.Predicate == terms.EquivalentClass && triple.Subject == triple.Object;
        bool subClassOfThing = triple.Predicate == terms.SubClassOf && triple.Object == terms.Thing;
        bool nothingSubClass = triple.Predicate == terms.SubClassOf && triple.Subject == terms.Nothing;
        bool typedThing = triple.Predicate == terms.Type && triple.Object == terms.Thing;
        bool sameAsReflexive = triple.Predicate == terms.SameAs && triple.Subject == triple.Object;
        bool subPropertyReflexive = triple.Predicate == terms.SubPropertyOf && triple.Subject == triple.Object;
        bool equivPropertyReflexive = triple.Predicate == terms.EquivalentProperty && triple.Subject == triple.Object;

        return subClassReflexive || equivClassReflexive || subClassOfThing || nothingSubClass || typedThing
            || sameAsReflexive || subPropertyReflexive || equivPropertyReflexive;
    }

    /// <summary>The within-RL open base: two declared classes in a hierarchy plus three more declared classes an unwired union later joins, and one instance.</summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> WithinRlDeclarationsGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "Animal"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
            new DataTriple(Iri(Ex + "a"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "b"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "c1"), Iri(RdfType), Iri(OwlClass)),
        ];
    }

    /// <summary>The <c>INSERT DATA</c> that adds the beyond-RL union structure <c>c1 ⊑ (a ∪ b)</c> with its blank-node rdf list.</summary>
    /// <returns>The update text.</returns>
    private static string UnionStructureUpdate()
    {
        return "INSERT DATA { " +
            $"<{Ex}c1> <{RdfsSubClassOf}> _:u . " +
            $"_:u <{OwlUnionOf}> _:l1 . " +
            $"_:l1 <{RdfFirst}> <{Ex}a> . " +
            $"_:l1 <{RdfRest}> _:l2 . " +
            $"_:l2 <{RdfFirst}> <{Ex}b> . " +
            $"_:l2 <{RdfRest}> <{RdfNil}> }}";
    }

    /// <summary>Decodes encoded triples to data triples for a mutable-engine open.</summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="triples">The encoded triples.</param>
    /// <returns>The decoded data triples.</returns>
    private static List<DataTriple> Decode(TermDictionary dictionary, IEnumerable<EncodedTriple> triples)
    {
        List<DataTriple> decoded = [];
        foreach(EncodedTriple triple in triples)
        {
            decoded.Add(new DataTriple(
                dictionary.Resolve(triple.Subject.Encoded),
                dictionary.Resolve(triple.Predicate.Encoded),
                dictionary.Resolve(triple.Object.Encoded)));
        }

        return decoded;
    }

    /// <summary>Decodes an encoded base and derived set to one value-equatable RDF-term triple set.</summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="baseTriples">The asserted base.</param>
    /// <param name="derived">The derived set.</param>
    /// <returns>The union of base and derived as decoded term tuples.</returns>
    private static HashSet<(RdfTerm, RdfTerm, RdfTerm)> DecodeToTermSet(
        TermDictionary dictionary,
        IEnumerable<EncodedTriple> baseTriples,
        IEnumerable<EncodedTriple> derived)
    {
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> set = [];
        foreach(EncodedTriple triple in baseTriples)
        {
            set.Add(DecodeTuple(dictionary, triple));
        }

        foreach(EncodedTriple triple in derived)
        {
            set.Add(DecodeTuple(dictionary, triple));
        }

        return set;
    }

    /// <summary>Decodes one encoded triple to an RDF-term tuple.</summary>
    /// <param name="dictionary">The dictionary the triple encodes with.</param>
    /// <param name="triple">The encoded triple.</param>
    /// <returns>The decoded term tuple.</returns>
    private static (RdfTerm, RdfTerm, RdfTerm) DecodeTuple(TermDictionary dictionary, EncodedTriple triple)
    {
        return (
            dictionary.Resolve(triple.Subject.Encoded),
            dictionary.Resolve(triple.Predicate.Encoded),
            dictionary.Resolve(triple.Object.Encoded));
    }

    /// <summary>Describes the symmetric difference between an expected and an actual triple set for an assertion message.</summary>
    /// <param name="expected">The expected set.</param>
    /// <param name="actual">The actual set.</param>
    /// <returns>The missing and extra triple counts and encoded rows.</returns>
    private static string Describe(HashSet<EncodedTriple> expected, HashSet<EncodedTriple> actual)
    {
        HashSet<EncodedTriple> missing = [.. expected];
        missing.ExceptWith(actual);
        HashSet<EncodedTriple> extra = [.. actual];
        extra.ExceptWith(expected);

        List<string> extraRows = [];
        foreach(EncodedTriple triple in extra)
        {
            extraRows.Add($"({triple.Subject.Encoded} {triple.Predicate.Encoded} {triple.Object.Encoded})");
        }

        return $"missing {missing.Count}, extra {extra.Count}: [{string.Join(", ", extraRows)}].";
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
