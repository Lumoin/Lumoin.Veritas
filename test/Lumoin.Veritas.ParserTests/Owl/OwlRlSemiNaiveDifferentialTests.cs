using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The semi-naive <see cref="OwlRlClosure.Compute(System.Collections.Generic.IEnumerable{EncodedTriple}, OwlRlTerms, OwlRlDatatypeOracle, Lumoin.Veritas.Core.Diagnostics.TraceHandler{Lumoin.Veritas.Core.Diagnostics.InferenceTraceEvent}, System.TimeProvider, System.Guid, System.Threading.CancellationToken)"/>
/// (delta firing) is verdict-preserving against the naive oracle
/// <see cref="OwlRlClosure.ComputeNaive"/>: on every input the two engines
/// derive the same triple set and agree on consistency, and on an
/// inconsistent input both report a falsity. The battery pins that
/// per-round delta restriction never drops nor invents a derivation — the
/// contract of the Phase 0b design record — across seeded random rule
/// families and hand-built multi-round cases that force a rule's second
/// body atom to arrive in a later round than the first.
/// </summary>
/// <remarks>
/// <para>
/// The differential property is sound here because the naive fixpoint is
/// definitionally ground truth for a monotone-Horn least model — unlike a
/// fragment-blind oracle, agreement proves materialization equality. The
/// seeds are pinned, so any failure replays deterministically.
/// </para>
/// <para>
/// Debugging affordance on a differential failure: do not shrink by hand.
/// Replay <see cref="OwlRlClosure.ComputeNaive"/> with a trace handler,
/// find the dropped conclusion's (or the falsity's) derivation event, and
/// walk its premises backward through earlier events to the base leaves.
/// That projection is the minimal input support of the divergence — a
/// candidate minimal repro confirmed by one re-run of both engines on the
/// projected subset. (Dropping a derivation is not monotone in the input,
/// so the confirmation run is required; a non-reproducing projection means
/// the failure is context-dependent, which is itself diagnostic.)
/// </para>
/// </remarks>
[TestClass]
internal sealed class OwlRlSemiNaiveDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace every minted term shares.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The <c>xsd:integer</c> datatype IRI numeric literals carry.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The <c>xsd:nonNegativeInteger</c> datatype IRI cardinality bounds carry.</summary>
    private const string XsdNonNegativeInteger = "http://www.w3.org/2001/XMLSchema#nonNegativeInteger";

    /// <summary>The <c>xsd:int</c> datatype IRI the seeded range case confines a property to.</summary>
    private const string XsdInt = "http://www.w3.org/2001/XMLSchema#int";

    /// <summary>The number of seeds each randomized family exercises.</summary>
    private const int SeedCount = 5;

    /// <summary>Random schema closures — subClassOf/subPropertyOf/domain/range and their equivalences, cycles included — derive identically under delta firing.</summary>
    [TestMethod]
    public void SchemaClosuresMatchNaive()
    {
        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(1000 + seed);

            TermId[] classes = MintRange(dictionary, "sc", 20);
            TermId[] properties = MintRange(dictionary, "sp", 20);
            TermId[] individuals = MintRange(dictionary, "si", 12);
            List<EncodedTriple> triples = [];

            //A schema of subsumptions, domains and ranges, plus equivalences
            //that close 2-cycles (equivalentClass A B / B A) and subClassOf
            //cycles the schema rules must saturate without diverging.
            for(int i = 0; i < 24; i++)
            {
                TermId a = classes[Next(random,classes.Length)];
                TermId b = classes[Next(random,classes.Length)];
                triples.Add(Triple(a, terms.SubClassOf, b));
                if(Next(random,4) == 0)
                {
                    triples.Add(Triple(b, terms.SubClassOf, a));
                }
            }

            for(int i = 0; i < 8; i++)
            {
                TermId a = classes[Next(random,classes.Length)];
                TermId b = classes[Next(random,classes.Length)];
                triples.Add(Triple(a, terms.EquivalentClass, b));
                triples.Add(Triple(b, terms.EquivalentClass, a));
            }

            for(int i = 0; i < 16; i++)
            {
                TermId p1 = properties[Next(random,properties.Length)];
                TermId p2 = properties[Next(random,properties.Length)];
                triples.Add(Triple(p1, terms.SubPropertyOf, p2));
                if(Next(random,4) == 0)
                {
                    triples.Add(Triple(p2, terms.EquivalentProperty, p1));
                }
            }

            for(int i = 0; i < 12; i++)
            {
                TermId p = properties[Next(random,properties.Length)];
                triples.Add(Triple(p, terms.Domain, classes[Next(random,classes.Length)]));
                triples.Add(Triple(p, terms.Range, classes[Next(random,classes.Length)]));
            }

            //Declared classes drive scm-cls; asserted edges and typings give
            //the schema closures instances to propagate.
            foreach(TermId c in classes)
            {
                triples.Add(Triple(c, terms.Type, terms.ClassTerm));
            }

            for(int i = 0; i < 20; i++)
            {
                triples.Add(Triple(individuals[Next(random,individuals.Length)], properties[Next(random,properties.Length)], individuals[Next(random,individuals.Length)]));
                triples.Add(Triple(individuals[Next(random,individuals.Length)], terms.Type, classes[Next(random,classes.Length)]));
            }

            AssertDifferential(triples, terms, default);
        }
    }

    /// <summary>Random property characteristics over random edge graphs — some inconsistent — derive or contradict identically under delta firing.</summary>
    [TestMethod]
    public void CharacteristicsMatchNaive()
    {
        TermId[] characteristics(OwlRlTerms terms) =>
        [
            terms.FunctionalProperty,
            terms.InverseFunctionalProperty,
            terms.SymmetricProperty,
            terms.AsymmetricProperty,
            terms.IrreflexiveProperty,
            terms.TransitiveProperty,
            terms.ReflexiveProperty,
        ];

        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(2000 + seed);

            TermId[] nodes = MintRange(dictionary, "cn", 15);
            TermId[] properties = MintRange(dictionary, "cp", 6);
            TermId[] chars = characteristics(terms);
            List<EncodedTriple> triples = [];

            //Each property gets one or two random characteristic typings —
            //some combinations (asymmetric over a two-cycle, irreflexive over
            //a self-loop) go inconsistent, which the helper tolerates.
            foreach(TermId p in properties)
            {
                triples.Add(Triple(p, terms.Type, chars[Next(random,chars.Length)]));
                if(Next(random,2) == 0)
                {
                    triples.Add(Triple(p, terms.Type, chars[Next(random,chars.Length)]));
                }
            }

            //Some named individuals so the reflexive rule instantiates.
            for(int i = 0; i < nodes.Length; i += 3)
            {
                triples.Add(Triple(nodes[i], terms.Type, terms.NamedIndividual));
            }

            for(int i = 0; i < 30; i++)
            {
                TermId s = nodes[Next(random,nodes.Length)];
                TermId o = nodes[Next(random,nodes.Length)];
                triples.Add(Triple(s, properties[Next(random,properties.Length)], o));
            }

            AssertDifferential(triples, terms, default);
        }
    }

    /// <summary>Random inverses, property chains (2- and 3-link, including p∘p⊑p) and equivalences over random edges derive identically under delta firing.</summary>
    [TestMethod]
    public void InversesAndChainsMatchNaive()
    {
        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(3000 + seed);

            TermId[] nodes = MintRange(dictionary, "in", 12);
            TermId[] properties = MintRange(dictionary, "ip", 8);
            List<EncodedTriple> triples = [];

            for(int i = 0; i < 4; i++)
            {
                TermId p1 = properties[Next(random,properties.Length)];
                TermId p2 = properties[Next(random,properties.Length)];
                triples.Add(Triple(p1, terms.InverseOf, p2));
                if(Next(random,3) == 0)
                {
                    triples.Add(Triple(p1, terms.EquivalentProperty, p2));
                }

                if(Next(random,3) == 0)
                {
                    triples.Add(Triple(p1, terms.SubPropertyOf, p2));
                }
            }

            //A 2-link chain, a 3-link chain, and the transitivity chain
            //p∘p⊑p, each with a hand-built rdf:first/rest list.
            TermId super2 = properties[Next(random,properties.Length)];
            AddChainAxiom(triples, dictionary, terms, super2, [properties[0], properties[1]], "chain2-" + seed);

            TermId super3 = properties[Next(random,properties.Length)];
            AddChainAxiom(triples, dictionary, terms, super3, [properties[2], properties[3], properties[4]], "chain3-" + seed);

            TermId loop = properties[5];
            AddChainAxiom(triples, dictionary, terms, loop, [loop, loop], "chainpp-" + seed);

            for(int i = 0; i < 28; i++)
            {
                TermId s = nodes[Next(random,nodes.Length)];
                TermId o = nodes[Next(random,nodes.Length)];
                triples.Add(Triple(s, properties[Next(random,properties.Length)], o));
            }

            AssertDifferential(triples, terms, default);
        }
    }

    /// <summary>Random restrictions — someValuesFrom (owl:Thing filler included), allValuesFrom, hasValue, max(qualified)cardinality 0/1 — derive or contradict identically under delta firing.</summary>
    [TestMethod]
    public void RestrictionsMatchNaive()
    {
        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(4000 + seed);

            TermId[] restrictions = MintRange(dictionary, "rr", 8);
            TermId[] properties = MintRange(dictionary, "rp", 5);
            TermId[] fillers = MintRange(dictionary, "rf", 5);
            TermId[] individuals = MintRange(dictionary, "ri", 12);
            TermId zero = Literal(dictionary, "0", XsdNonNegativeInteger);
            TermId one = Literal(dictionary, "1", XsdNonNegativeInteger);
            List<EncodedTriple> triples = [];

            foreach(TermId r in restrictions)
            {
                TermId p = properties[Next(random,properties.Length)];
                triples.Add(Triple(r, terms.OnProperty, p));

                //One restriction flavour per node, so onProperty reads a
                //single first object as the naive rule requires.
                switch(Next(random,6))
                {
                    case 0:
                        triples.Add(Triple(r, terms.SomeValuesFrom, Next(random,3) == 0 ? terms.Thing : fillers[Next(random,fillers.Length)]));
                        break;
                    case 1:
                        triples.Add(Triple(r, terms.AllValuesFrom, fillers[Next(random,fillers.Length)]));
                        break;
                    case 2:
                        triples.Add(Triple(r, terms.HasValue, individuals[Next(random,individuals.Length)]));
                        break;
                    case 3:
                        triples.Add(Triple(r, terms.MaxCardinality, Next(random,2) == 0 ? zero : one));
                        break;
                    case 4:
                        triples.Add(Triple(r, terms.MaxQualifiedCardinality, Next(random,2) == 0 ? zero : one));
                        triples.Add(Triple(r, terms.OnClass, fillers[Next(random,fillers.Length)]));
                        break;
                    default:
                        triples.Add(Triple(r, terms.SomeValuesFrom, fillers[Next(random,fillers.Length)]));
                        break;
                }
            }

            //Typed instances of the restrictions and filler classes, plus
            //edges over the restricted properties, so every restriction rule
            //has data to consume.
            for(int i = 0; i < 24; i++)
            {
                triples.Add(Triple(individuals[Next(random,individuals.Length)], terms.Type, restrictions[Next(random,restrictions.Length)]));
                triples.Add(Triple(individuals[Next(random,individuals.Length)], terms.Type, fillers[Next(random,fillers.Length)]));
                triples.Add(Triple(individuals[Next(random,individuals.Length)], properties[Next(random,properties.Length)], individuals[Next(random,individuals.Length)]));
            }

            AssertDifferential(triples, terms, default);
        }
    }

    /// <summary>Random class expressions — intersection/union/oneOf/complement, disjointness lists and hasKey — derive or contradict identically under delta firing.</summary>
    [TestMethod]
    public void ClassExpressionsMatchNaive()
    {
        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(5000 + seed);

            TermId[] classes = MintRange(dictionary, "xc", 12);
            TermId[] properties = MintRange(dictionary, "xp", 4);
            TermId[] individuals = MintRange(dictionary, "xi", 12);
            List<EncodedTriple> triples = [];
            int listCounter = 0;

            //Intersection and union expressions over hand-built lists.
            for(int i = 0; i < 3; i++)
            {
                TermId head = AddList(triples, dictionary, terms, [classes[Next(random,classes.Length)], classes[Next(random,classes.Length)]], $"int-{seed}-{listCounter++}");
                triples.Add(Triple(classes[Next(random,classes.Length)], terms.IntersectionOf, head));
            }

            for(int i = 0; i < 3; i++)
            {
                TermId head = AddList(triples, dictionary, terms, [classes[Next(random,classes.Length)], classes[Next(random,classes.Length)]], $"uni-{seed}-{listCounter++}");
                triples.Add(Triple(classes[Next(random,classes.Length)], terms.UnionOf, head));
            }

            //oneOf and complement.
            TermId oneOfHead = AddList(triples, dictionary, terms, [individuals[0], individuals[1], individuals[2]], $"oo-{seed}-{listCounter++}");
            triples.Add(Triple(classes[0], terms.OneOf, oneOfHead));
            triples.Add(Triple(classes[1], terms.ComplementOf, classes[2]));

            //Disjointness: a pairwise disjointWith plus reified list forms.
            triples.Add(Triple(classes[3], terms.DisjointWith, classes[4]));

            TermId adcNode = Mint(dictionary, $"adc-{seed}");
            TermId adcHead = AddList(triples, dictionary, terms, [classes[5], classes[6], classes[7]], $"adc-list-{seed}-{listCounter++}");
            triples.Add(Triple(adcNode, terms.Type, terms.AllDisjointClasses));
            triples.Add(Triple(adcNode, terms.Members, adcHead));

            TermId addNode = Mint(dictionary, $"add-{seed}");
            TermId addHead = AddList(triples, dictionary, terms, [individuals[3], individuals[4], individuals[5]], $"add-list-{seed}-{listCounter++}");
            triples.Add(Triple(addNode, terms.Type, terms.AllDifferent));
            triples.Add(Triple(addNode, terms.DistinctMembers, addHead));

            //hasKey with a 1-property and a 2-property key.
            TermId key1Head = AddList(triples, dictionary, terms, [properties[0]], $"key1-{seed}-{listCounter++}");
            triples.Add(Triple(classes[8], terms.HasKey, key1Head));
            TermId key2Head = AddList(triples, dictionary, terms, [properties[1], properties[2]], $"key2-{seed}-{listCounter++}");
            triples.Add(Triple(classes[9], terms.HasKey, key2Head));

            //Typed instances and key-property edges so the rules fire and
            //some seeds force a merge or a disjointness contradiction.
            for(int i = 0; i < 22; i++)
            {
                triples.Add(Triple(individuals[Next(random,individuals.Length)], terms.Type, classes[Next(random,classes.Length)]));
                triples.Add(Triple(individuals[Next(random,individuals.Length)], properties[Next(random,properties.Length)], individuals[Next(random,individuals.Length)]));
            }

            AssertDifferential(triples, terms, default);
        }
    }

    /// <summary>sameAs chains and cliques with per-entity data, plus differentFrom pairs, functional-property merges and numeric literals under the dictionary oracle — merges and dt-diff — resolve identically under delta firing.</summary>
    [TestMethod]
    public void EqualityChurnMatchesNaive()
    {
        for(int seed = 0; seed < SeedCount; seed++)
        {
            TermDictionary dictionary = new();
            OwlRlTerms terms = new(dictionary);
            OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
            RandomSourceDelegate random = RandomSources.FromSeed(6000 + seed);

            TermId[] entities = MintRange(dictionary, "eq", 30);
            TermId[] properties = MintRange(dictionary, "ep", 4);
            List<EncodedTriple> triples = [];

            //sameAs chains and 4-member cliques threaded through the entity
            //array; the closure must substitute across the whole clique.
            for(int start = 0; start + 3 < entities.Length; start += 5)
            {
                triples.Add(Triple(entities[start], terms.SameAs, entities[start + 1]));
                triples.Add(Triple(entities[start + 1], terms.SameAs, entities[start + 2]));
                triples.Add(Triple(entities[start + 2], terms.SameAs, entities[start + 3]));
            }

            //Per-entity data that must propagate across every merge.
            for(int i = 0; i < entities.Length; i++)
            {
                triples.Add(Triple(entities[i], properties[i % properties.Length], entities[(i + 7) % entities.Length]));
            }

            //The first seed asserts differentFrom on a merged pair
            //(inconsistent); later seeds keep the churn consistent so the
            //derived-set equality branch is exercised on the merge shape.
            if(seed == 0)
            {
                triples.Add(Triple(entities[0], terms.DifferentFrom, entities[3]));
            }

            //A functional property forcing a fresh merge.
            TermId functional = properties[0];
            triples.Add(Triple(functional, terms.Type, terms.FunctionalProperty));
            triples.Add(Triple(entities[10], functional, entities[11]));
            triples.Add(Triple(entities[10], functional, entities[12]));

            //Numeric literals so dt-diff can fire on one seed: two distinct
            //integers bridged into the same clique.
            if(seed == 1)
            {
                TermId oneValue = Literal(dictionary, "1", XsdInteger);
                TermId twoValue = Literal(dictionary, "2", XsdInteger);
                triples.Add(Triple(oneValue, terms.SameAs, entities[20]));
                triples.Add(Triple(entities[20], terms.SameAs, twoValue));
            }

            AssertDifferential(triples, terms, oracle);
        }
    }

    /// <summary>scm-dom2 derives domain(p1,C) a round after p1's edges are asserted; every edge must still reach type C.</summary>
    [TestMethod]
    public void DomainDerivedLate()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p1 = Mint(dictionary, "p1");
        TermId p2 = Mint(dictionary, "p2");
        TermId c = Mint(dictionary, "C");
        TermId a = Mint(dictionary, "a");
        TermId b = Mint(dictionary, "b");

        //domain(p2,C) + subPropertyOf(p1,p2) → scm-dom2 derives
        //domain(p1,C) in a later round; the p1 edges precede it, so the
        //delta engine must revisit them once the domain axiom arrives.
        List<EncodedTriple> triples =
        [
            Triple(p2, terms.Domain, c),
            Triple(p1, terms.SubPropertyOf, p2),
            Triple(a, p1, b),
            Triple(b, p1, a),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>propertyChainAxiom(p,[p,p]) derives Transitive(p) via chain-trans; prp-trp must then close a chain of edges asserted from the start.</summary>
    [TestMethod]
    public void TransitivityDerivedByChain()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Mint(dictionary, "p");
        TermId a = Mint(dictionary, "a");
        TermId b = Mint(dictionary, "b");
        TermId c = Mint(dictionary, "c");
        TermId d = Mint(dictionary, "d");

        //The chain axiom p∘p⊑p makes p transitive in a later round; the
        //four-edge chain a→b→c→d is present from round 0, so prp-trp must
        //saturate it once the derived typing lands.
        List<EncodedTriple> triples =
        [
            Triple(a, p, b),
            Triple(b, p, c),
            Triple(c, p, d),
        ];
        AddChainAxiom(triples, dictionary, terms, p, [p, p], "trans-chain");

        AssertDifferential(triples, terms, default);
    }

    /// <summary>equivalentClass(A,B) derives sco(A,B) late via scm-eqc1, and deep sco chains via scm-sco; an instance of A must reach type B and every superclass.</summary>
    [TestMethod]
    public void SubClassDerivedLate()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = Mint(dictionary, "A");
        TermId b = Mint(dictionary, "B");
        TermId d = Mint(dictionary, "D");
        TermId e = Mint(dictionary, "E");
        TermId x = Mint(dictionary, "x");

        //equivalentClass(A,B) → scm-eqc1 derives sco(A,B); a deep chain
        //B⊑D⊑E closes via scm-sco. The instance x:A is asserted from the
        //start and must reach B, D and E through late-derived edges.
        List<EncodedTriple> triples =
        [
            Triple(a, terms.EquivalentClass, b),
            Triple(b, terms.SubClassOf, d),
            Triple(d, terms.SubClassOf, e),
            Triple(x, terms.Type, a),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>disjointWith(C,D) plus an sco chain deriving type(x,D) at depth ≥2 while type(x,C) is asserted — the falsity's last premise is derived — is inconsistent in both engines.</summary>
    [TestMethod]
    public void FalsityLastPremiseDerived()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c = Mint(dictionary, "C");
        TermId d = Mint(dictionary, "D");
        TermId e = Mint(dictionary, "E");
        TermId f = Mint(dictionary, "F");
        TermId x = Mint(dictionary, "x");

        //type(x,C) asserted; type(x,D) derived at depth 2 through E⊑F⊑D
        //while x:E is asserted. The cax-dw falsity therefore fires only
        //once its second type premise has been derived.
        List<EncodedTriple> triples =
        [
            Triple(c, terms.DisjointWith, d),
            Triple(x, terms.Type, c),
            Triple(x, terms.Type, e),
            Triple(e, terms.SubClassOf, f),
            Triple(f, terms.SubClassOf, d),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>propertyDisjointWith(p,q) with subPropertyOf(r,q) and an r-edge so the q-edge is derived — the disjointness falsity's second edge is derived — is inconsistent in both engines.</summary>
    [TestMethod]
    public void PdwDerivedEdge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");
        TermId r = Mint(dictionary, "r");
        TermId a = Mint(dictionary, "a");
        TermId b = Mint(dictionary, "b");

        //p and q are disjoint; a-p-b is asserted, and a-q-b is derived
        //because r⊑q and a-r-b holds. The prp-pdw falsity fires on the
        //asserted p-edge and the derived q-edge.
        List<EncodedTriple> triples =
        [
            Triple(p, terms.PropertyDisjointWith, q),
            Triple(r, terms.SubPropertyOf, q),
            Triple(a, p, b),
            Triple(a, r, b),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>intersectionOf(C,[A,B]) with type(x,A) asserted and type(x,B) derived via cax-sco — the intersection completes in a later round — fires cls-int1 concluding type(x,C).</summary>
    [TestMethod]
    public void IntersectionCompletedLate()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = Mint(dictionary, "A");
        TermId b = Mint(dictionary, "B");
        TermId g = Mint(dictionary, "G");
        TermId intersection = Mint(dictionary, "C");
        TermId x = Mint(dictionary, "x");

        //type(x,A) asserted, type(x,B) derived through G⊑B while x:G holds.
        //cls-int1 fires only after the second member typing arrives.
        List<EncodedTriple> triples =
        [
            Triple(x, terms.Type, a),
            Triple(x, terms.Type, g),
            Triple(g, terms.SubClassOf, b),
        ];
        TermId head = AddList(triples, dictionary, terms, [a, b], "int-late");
        triples.Add(Triple(intersection, terms.IntersectionOf, head));

        AssertDifferential(triples, terms, default);
    }

    /// <summary>A someValuesFrom restriction whose filler F is typed onto the edge object only through a late sco derivation still fires cls-svf1 concluding the subject's restriction membership.</summary>
    [TestMethod]
    public void SvfFillerTypedLate()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = Mint(dictionary, "R");
        TermId p = Mint(dictionary, "p");
        TermId f = Mint(dictionary, "F");
        TermId g = Mint(dictionary, "G");
        TermId u = Mint(dictionary, "u");
        TermId v = Mint(dictionary, "v");

        //R = ∃p.F; edge (u,p,v) asserted; type(v,F) derived from G⊑F while
        //v:G holds. cls-svf1 must revisit the edge once v is typed F.
        List<EncodedTriple> triples =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.SomeValuesFrom, f),
            Triple(u, p, v),
            Triple(v, terms.Type, g),
            Triple(g, terms.SubClassOf, f),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>maxQualifiedCardinality 1 with onClass F over two p-edges whose second object is typed F only via derivation — both objects equate — resolves identically in both engines.</summary>
    [TestMethod]
    public void MaxQcFillerTypedLate()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = Mint(dictionary, "R");
        TermId p = Mint(dictionary, "p");
        TermId f = Mint(dictionary, "F");
        TermId g = Mint(dictionary, "G");
        TermId u = Mint(dictionary, "u");
        TermId v1 = Mint(dictionary, "v1");
        TermId v2 = Mint(dictionary, "v2");
        TermId one = Literal(dictionary, "1", XsdNonNegativeInteger);

        //u has two p-edges; v1 is typed F directly, v2 only after G⊑F fires.
        //Once both are qualified, max-qualified-cardinality-1 equates them.
        List<EncodedTriple> triples =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.MaxQualifiedCardinality, one),
            Triple(restriction, terms.OnClass, f),
            Triple(u, terms.Type, restriction),
            Triple(u, p, v1),
            Triple(u, p, v2),
            Triple(v1, terms.Type, f),
            Triple(v2, terms.Type, g),
            Triple(g, terms.SubClassOf, f),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>A sameAs chain a-b-c-d with data on each member — the clique grows across rounds — substitutes data across the whole derived clique identically in both engines.</summary>
    [TestMethod]
    public void EqRepAcrossGrowingClique()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a = Mint(dictionary, "a");
        TermId b = Mint(dictionary, "b");
        TermId c = Mint(dictionary, "c");
        TermId d = Mint(dictionary, "d");
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");
        TermId target = Mint(dictionary, "t");

        //Each member carries a distinct data triple; eq-trans/eq-sym grow
        //the clique round by round and eq-rep must replay every member's
        //data across the whole thing.
        List<EncodedTriple> triples =
        [
            Triple(a, terms.SameAs, b),
            Triple(b, terms.SameAs, c),
            Triple(c, terms.SameAs, d),
            Triple(a, p, target),
            Triple(target, q, b),
            Triple(c, p, target),
            Triple(target, q, d),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>A restriction node with two someValuesFrom triples — the mirror hazard — must agree, since only the canonical (minimum-id) object drives derivation; the assertion is differential equality, not a specific derivation.</summary>
    [TestMethod]
    public void TwoSomeValuesFromObjects()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = Mint(dictionary, "R");
        TermId p = Mint(dictionary, "p");
        TermId f1 = Mint(dictionary, "F1");
        TermId f2 = Mint(dictionary, "F2");
        TermId u = Mint(dictionary, "u");
        TermId v = Mint(dictionary, "v");

        //Two someValuesFrom objects on one restriction node. The naive rule
        //reads the canonical object only; the delta engine must not fire on
        //the other filler reached through a reverse index. Both engines
        //therefore produce the same closure regardless of which filler v is
        //typed.
        List<EncodedTriple> triples =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.SomeValuesFrom, f1),
            Triple(restriction, terms.SomeValuesFrom, f2),
            Triple(u, p, v),
            Triple(v, terms.Type, f1),
            Triple(v, terms.Type, f2),
        ];

        AssertDifferential(triples, terms, default);
    }

    /// <summary>
    /// Per-value firing is a function of the graph: every asserted
    /// someValuesFrom filler drives its own cls-svf1 instance, independent
    /// of the order the triples arrived in. A restriction carries two
    /// fillers; the edge object typed with either filler fires the
    /// membership, in both statement orders, and the two orders close to
    /// the same derived set. Hand-derivation: v : F1 satisfies the F1
    /// filler triple's rule instance; v : F2 alone satisfies the F2
    /// instance — each filler triple is an independent fact of the graph.
    /// </summary>
    [TestMethod]
    public void PerValueFiringIsOrderIndependent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = Mint(dictionary, "R");
        TermId p = Mint(dictionary, "p");
        TermId f1 = Mint(dictionary, "F1");
        TermId f2 = Mint(dictionary, "F2");
        TermId u = Mint(dictionary, "u");
        TermId v = Mint(dictionary, "v");

        EncodedTriple membership = Triple(u, terms.Type, restriction);

        //The smaller-id filler F1 is stated LAST: canonical wins over stated
        //order, in both statement orders.
        List<EncodedTriple> smallerLast =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.SomeValuesFrom, f2),
            Triple(restriction, terms.SomeValuesFrom, f1),
            Triple(u, p, v),
            Triple(v, terms.Type, f1),
        ];
        List<EncodedTriple> smallerFirst =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.SomeValuesFrom, f1),
            Triple(restriction, terms.SomeValuesFrom, f2),
            Triple(u, p, v),
            Triple(v, terms.Type, f1),
        ];

        OwlRlResult last = OwlRlClosure.Compute(smallerLast, terms, cancellationToken: TestContext.CancellationToken);
        OwlRlResult first = OwlRlClosure.Compute(smallerFirst, terms, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(last.IsConsistent);
        Assert.IsTrue(first.IsConsistent);
        Assert.Contains(membership, last.Derived, "The F1 filler's rule instance must fire when the filler is stated last.");
        Assert.Contains(membership, first.Derived, "The F1 filler's rule instance must fire when the filler is stated first.");
        Assert.IsTrue(new HashSet<EncodedTriple>(last.Derived).SetEquals(first.Derived), "Statement order must not change the closure.");

        //Typing only the larger-id filler fires that filler's own instance.
        List<EncodedTriple> secondFillerOnly =
        [
            Triple(restriction, terms.OnProperty, p),
            Triple(restriction, terms.SomeValuesFrom, f2),
            Triple(restriction, terms.SomeValuesFrom, f1),
            Triple(u, p, v),
            Triple(v, terms.Type, f2),
        ];

        OwlRlResult secondFiller = OwlRlClosure.Compute(secondFillerOnly, terms, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(secondFiller.IsConsistent);
        Assert.Contains(membership, secondFiller.Derived, "Every asserted filler drives its own cls-svf1 instance; the identifier order of the fillers carries no meaning.");
    }

    /// <summary>An old AllDisjointClasses node whose member class gains a colliding instance type in the same round a second node is first typed AllDisjointClasses — the node re-check is deferred to the new node, and the collision must still contradict through the materialized pairwise disjointness in both engines.</summary>
    [TestMethod]
    public void AdcMaskedTriggerStillContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = Mint(dictionary, "C1");
        TermId c2 = Mint(dictionary, "C2");
        TermId g = Mint(dictionary, "G");
        TermId g2 = Mint(dictionary, "G2");
        TermId h = Mint(dictionary, "H");
        TermId h2 = Mint(dictionary, "H2");
        TermId a = Mint(dictionary, "a");
        TermId n1 = Mint(dictionary, "N1");
        TermId n2 = Mint(dictionary, "N2");

        //N1's list materializes disjointWith(C1,C2) in round 0. type(a,C1)
        //arrives two derivation rounds later through G⊑G2⊑C1, in the same
        //round the depth-matched chain H⊑H2⊑owl:AllDisjointClasses first
        //types N2 — so the round both merges a member collision and a new
        //AllDisjointClasses typing. The verdict must be inconsistent either
        //way.
        List<EncodedTriple> triples =
        [
            Triple(n1, terms.Type, terms.AllDisjointClasses),
            Triple(a, terms.Type, c2),
            Triple(a, terms.Type, g),
            Triple(g, terms.SubClassOf, g2),
            Triple(g2, terms.SubClassOf, c1),
            Triple(n2, terms.Type, h),
            Triple(h, terms.SubClassOf, h2),
            Triple(h2, terms.SubClassOf, terms.AllDisjointClasses),
        ];
        TermId membersHead = AddList(triples, dictionary, terms, [c1, c2], "adc-masked");
        triples.Add(Triple(n1, terms.Members, membersHead));

        AssertDifferential(triples, terms, default);
    }

    /// <summary>An old AllDisjointProperties node whose member property gains a colliding edge in the same round a second node is first typed AllDisjointProperties — the analogous masked trigger — still contradicts through the materialized pairwise property disjointness in both engines.</summary>
    [TestMethod]
    public void AdpMaskedTriggerStillContradicts()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");
        TermId r = Mint(dictionary, "r");
        TermId r2 = Mint(dictionary, "r2");
        TermId h = Mint(dictionary, "H");
        TermId h2 = Mint(dictionary, "H2");
        TermId x = Mint(dictionary, "x");
        TermId y = Mint(dictionary, "y");
        TermId n1 = Mint(dictionary, "N1");
        TermId n2 = Mint(dictionary, "N2");

        //N1's list materializes propertyDisjointWith(p,q) in round 0. The
        //q-edge arrives two derivation rounds later through r⊑r2⊑q, in the
        //same round the depth-matched chain H⊑H2⊑owl:AllDisjointProperties
        //first types N2. The verdict must be inconsistent either way.
        List<EncodedTriple> triples =
        [
            Triple(n1, terms.Type, terms.AllDisjointProperties),
            Triple(x, p, y),
            Triple(x, r, y),
            Triple(r, terms.SubPropertyOf, r2),
            Triple(r2, terms.SubPropertyOf, q),
            Triple(n2, terms.Type, h),
            Triple(h, terms.SubClassOf, h2),
            Triple(h2, terms.SubClassOf, terms.AllDisjointProperties),
        ];
        TermId membersHead = AddList(triples, dictionary, terms, [p, q], "adp-masked");
        triples.Add(Triple(n1, terms.Members, membersHead));

        AssertDifferential(triples, terms, default);
    }

    /// <summary>A near-empty base with one range axiom on xsd:int derives identically, the axiomatic datatype-hierarchy seeds included.</summary>
    [TestMethod]
    public void SeededDatatypeHierarchy()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId p = Mint(dictionary, "p");
        TermId a = Mint(dictionary, "a");
        TermId intType = Named(dictionary, XsdInt);
        TermId value = Literal(dictionary, "3", XsdInt);

        //A single range(p, xsd:int) over one edge: the closure must fold in
        //the axiomatic datatype-hierarchy seeds identically under delta
        //firing, so scm-rng1 propagates up the datatype chain the same way.
        List<EncodedTriple> triples =
        [
            Triple(p, terms.Range, intType),
            Triple(a, p, value),
        ];

        AssertDifferential(triples, terms, oracle);
    }

    /// <summary>
    /// Runs both engines on the same input and asserts they agree: on
    /// consistent inputs the derived sets are equal in both directions and
    /// the consistency verdict matches; on inconsistent inputs both engines
    /// report inconsistency with a named falsity rule. Rule-name equality
    /// and premise identity are out of the battery's contract, since which
    /// witness a falsity reports may differ between the engines.
    /// </summary>
    /// <param name="triples">The base triples both engines close over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, or <see langword="default"/> to disable the dt-* falsities.</param>
    private void AssertDifferential(List<EncodedTriple> triples, OwlRlTerms terms, OwlRlDatatypeOracle oracle)
    {
        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, oracle, cancellationToken: TestContext.CancellationToken);

        if(semiNaive.IsConsistent && naive.IsConsistent)
        {
            HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
            HashSet<EncodedTriple> naiveDerived = [.. naive.Derived];

            Assert.IsTrue(
                semiNaiveDerived.SetEquals(naiveDerived),
                $"Semi-naive derived {semiNaiveDerived.Count} triples; naive derived {naiveDerived.Count}. Delta firing must derive exactly the naive set.");
            Assert.IsTrue(
                naiveDerived.SetEquals(semiNaiveDerived),
                $"Naive derived {naiveDerived.Count} triples; semi-naive derived {semiNaiveDerived.Count}. Delta firing must not invent a derivation.");
            Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);

            return;
        }

        Assert.IsFalse(semiNaive.IsConsistent, "Naive reported an inconsistency the semi-naive engine missed.");
        Assert.IsFalse(naive.IsConsistent, "Semi-naive reported an inconsistency the naive engine missed.");
        Assert.IsNotNull(semiNaive.InconsistencyRule, "The semi-naive engine reported no falsity rule for an inconsistent input.");
        Assert.IsNotNull(naive.InconsistencyRule, "The naive engine reported no falsity rule for an inconsistent input.");
    }

    /// <summary>Draws a bounded non-negative value from the sanctioned seeded source.</summary>
    /// <param name="random">The seeded source.</param>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>A value in [0, <paramref name="bound"/>).</returns>
    private static int Next(RandomSourceDelegate random, int bound)
    {
        return (int)(random() % (ulong)bound);
    }

    /// <summary>Mints a contiguous array of IRIs in the example namespace under one prefix.</summary>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <param name="prefix">The local-name prefix.</param>
    /// <param name="count">The number of terms to mint.</param>
    /// <returns>The minted identifiers, index 0 first.</returns>
    private static TermId[] MintRange(TermDictionary dictionary, string prefix, int count)
    {
        TermId[] terms = new TermId[count];
        for(int i = 0; i < count; i++)
        {
            terms[i] = Mint(dictionary, prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return terms;
    }

    /// <summary>Adds a propertyChainAxiom(super, list) with a hand-built rdf:first/rest list over the given properties.</summary>
    /// <param name="triples">The triple list the axiom and its list structure append to.</param>
    /// <param name="dictionary">The dictionary the list nodes mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="super">The chain's super-property.</param>
    /// <param name="chain">The chain's ordered property members.</param>
    /// <param name="label">A unique label distinguishing this list's blank nodes.</param>
    private static void AddChainAxiom(List<EncodedTriple> triples, TermDictionary dictionary, OwlRlTerms terms, TermId super, TermId[] chain, string label)
    {
        TermId head = AddList(triples, dictionary, terms, chain, label);
        triples.Add(Triple(super, terms.PropertyChainAxiom, head));
    }

    /// <summary>Builds an RDF collection over the given members and returns its head node.</summary>
    /// <param name="triples">The triple list the list structure appends to.</param>
    /// <param name="dictionary">The dictionary the list nodes mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="members">The list members, in order.</param>
    /// <param name="label">A unique label distinguishing this list's blank nodes.</param>
    /// <returns>The head node of the collection.</returns>
    private static TermId AddList(List<EncodedTriple> triples, TermDictionary dictionary, OwlRlTerms terms, TermId[] members, string label)
    {
        TermId head = terms.Nil;
        for(int i = members.Length - 1; i >= 0; i--)
        {
            TermId node = Blank(dictionary, $"list-{label}-{i}");
            triples.Add(Triple(node, terms.First, members[i]));
            triples.Add(Triple(node, terms.Rest, head));
            head = node;
        }

        return head;
    }

    /// <summary>Mints an IRI in the example namespace.</summary>
    /// <param name="dictionary">The dictionary the term enters.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>Mints an IRI by its full string.</summary>
    /// <param name="dictionary">The dictionary the term enters.</param>
    /// <param name="iri">The full IRI.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Named(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    /// <summary>Mints a blank node by label.</summary>
    /// <param name="dictionary">The dictionary the node enters.</param>
    /// <param name="label">The blank-node label.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Blank(TermDictionary dictionary, string label)
    {
        return dictionary.GetOrAdd(new BlankNode(Utf8Strings.From(label)));
    }

    /// <summary>Mints a typed literal.</summary>
    /// <param name="dictionary">The dictionary the literal enters.</param>
    /// <param name="lexical">The literal's lexical form.</param>
    /// <param name="datatype">The datatype IRI.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Literal(TermDictionary dictionary, string lexical, string datatype)
    {
        return dictionary.GetOrAdd((RdfTerm)new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From(datatype))));
    }

    /// <summary>An encoded triple from three term identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="object">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }
}
