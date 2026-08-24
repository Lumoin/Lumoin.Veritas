using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The deterministic Q1 corpus for <see cref="SharedSubpartDagSoak"/>: a
/// shared-subpart <c>partOf</c> DAG (transitive, the deep roll-up driver),
/// deep <c>supplies</c> transitive chains, <c>owl:sameAs</c>
/// re-resolution cliques, and a flat attribute payload. Beyond generation it
/// evolves under the soak's op stream: <see cref="AppendBatch"/> adds new
/// roots, attributes and links (the append-dominant bulk), and
/// <see cref="RetractBurst"/> retracts an attribute set and re-adds a corrected
/// one, and retracts a <c>sameAs</c> bridge and re-adds a different one.
/// </summary>
internal sealed class SharedSubpartDagCorpus
{
    /// <summary>The shared IRI namespace of every minted term.</summary>
    private const string Namespace = "http://example.org/dag/";

    /// <summary>The attribute triples per root node — the appended payload width.</summary>
    private const int CertificationsPerProduct = 3;

    /// <summary>The dictionary every term encodes into.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>The resolved RL vocabulary.</summary>
    private OwlRlTerms Terms { get; }

    /// <summary>The live base — every triple the closure currently holds over.</summary>
    private HashSet<EncodedTriple> BaseSet { get; }

    /// <summary>The <c>partOf</c> predicate (transitive).</summary>
    private TermId PartOf { get; }

    /// <summary>The <c>supplies</c> predicate (transitive).</summary>
    private TermId Supplies { get; }

    /// <summary>The <c>hasCertification</c> predicate carrying the flat attribute payload.</summary>
    private TermId HasCertification { get; }

    /// <summary>The <c>Product</c> class.</summary>
    private TermId ProductClass { get; }

    /// <summary>The subassembly nodes new appended products attach under.</summary>
    private List<TermId> SubassemblyNodes { get; }

    /// <summary>The organization nodes an entity re-resolution can re-bridge.</summary>
    private List<TermId> OrgNodes { get; }

    /// <summary>The attribute triples currently asserted per root node, so a burst can retract exactly them.</summary>
    private Dictionary<TermId, List<EncodedTriple>> ProductCertifications { get; }

    /// <summary>The next ordinal for a minted term, keeping generation deterministic.</summary>
    private int NextOrdinal { get; set; }

    /// <summary>The root nodes, the burst and append targets.</summary>
    public List<TermId> Products { get; }

    /// <summary>The asserted <c>owl:sameAs</c> re-resolution bridges, the re-resolution burst targets.</summary>
    public List<(TermId A, TermId B)> SameAsBridges { get; }

    /// <summary>The number of intermediate nodes in the DAG.</summary>
    public int Subassemblies => SubassemblyNodes.Count;

    /// <summary>The number of leaf nodes in the DAG.</summary>
    public int Leaves { get; }

    /// <summary>The number of organization nodes.</summary>
    public int Orgs => OrgNodes.Count;

    /// <summary>A read-only snapshot of the current base.</summary>
    /// <returns>The current base triples.</returns>
    public List<EncodedTriple> Snapshot()
    {
        return [.. BaseSet];
    }

    private SharedSubpartDagCorpus(
        TermDictionary dictionary,
        OwlRlTerms terms,
        HashSet<EncodedTriple> baseSet,
        TermId partOf,
        TermId supplies,
        TermId hasCertification,
        TermId productClass,
        List<TermId> products,
        List<TermId> subassemblyNodes,
        List<TermId> orgNodes,
        List<(TermId A, TermId B)> sameAsBridges,
        Dictionary<TermId, List<EncodedTriple>> productCertifications,
        int leaves,
        int nextOrdinal)
    {
        Dictionary = dictionary;
        Terms = terms;
        BaseSet = baseSet;
        PartOf = partOf;
        Supplies = supplies;
        HasCertification = hasCertification;
        ProductClass = productClass;
        Products = products;
        SubassemblyNodes = subassemblyNodes;
        OrgNodes = orgNodes;
        SameAsBridges = sameAsBridges;
        ProductCertifications = productCertifications;
        Leaves = leaves;
        NextOrdinal = nextOrdinal;
    }

    /// <summary>Generates the corpus at a headline scale.</summary>
    /// <param name="dictionary">The dictionary the terms encode into.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="entities">The headline entity scale; the DAG, chain and re-resolution counts derive from it.</param>
    /// <param name="seed">The deterministic generation seed.</param>
    /// <returns>The corpus.</returns>
    public static SharedSubpartDagCorpus Generate(TermDictionary dictionary, OwlRlTerms terms, int entities, int seed)
    {
        int ordinal = 0;
        TermId Mint(string prefix) => dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}{prefix}{ordinal++}")));

        HashSet<EncodedTriple> baseSet = [];
        void Add(TermId s, TermId p, TermId o) => baseSet.Add(EncodedTriple.FromEncoded(s.Encoded, p.Encoded, o.Encoded));

        TermId partOf = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}partOf")));
        TermId supplies = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}supplies")));
        TermId hasCertification = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}hasCertification")));
        TermId productClass = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}Product")));
        TermId artifactClass = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}Artifact")));

        //partOf and supplies are transitive: the deep roll-up and multi-hop
        //chain closures the maintenance cost is dominated by.
        Add(partOf, terms.Type, terms.TransitiveProperty);
        Add(supplies, terms.Type, terms.TransitiveProperty);
        Add(productClass, terms.SubClassOf, artifactClass);

        int leaves = Math.Max(entities / 2, 8);
        int subassemblyCount = Math.Max(entities / 8, 4);
        int productCount = Math.Max(entities / 40, 4);
        int orgCount = Math.Max(entities / 8, 16);

        List<TermId> subassemblyNodes = new(subassemblyCount);
        for(int i = 0; i < subassemblyCount; i++)
        {
            subassemblyNodes.Add(Mint("sub"));
        }

        List<TermId> productNodes = new(productCount);
        for(int i = 0; i < productCount; i++)
        {
            TermId product = Mint("prod");
            productNodes.Add(product);
            Add(product, terms.Type, productClass);
        }

        //Each subassembly is part of two shared products; the DAG's upper tier.
        for(int i = 0; i < subassemblyCount; i++)
        {
            Add(subassemblyNodes[i], partOf, productNodes[i % productCount]);
            Add(subassemblyNodes[i], partOf, productNodes[(i + 1) % productCount]);
        }

        //Each leaf component is part of two shared subassemblies; the DAG's
        //lower tier, where sharing makes the transitive roll-up fan out.
        for(int i = 0; i < leaves; i++)
        {
            TermId leaf = Mint("leaf");
            Add(leaf, partOf, subassemblyNodes[i % subassemblyCount]);
            Add(leaf, partOf, subassemblyNodes[(i + 2) % subassemblyCount]);
        }

        //Transitive chains: organization nodes chained deep enough for a multi-hop
        //transitive closure (tens of hops per chain).
        int chainDepth = Math.Min(24, Math.Max(4, orgCount / 4));
        List<TermId> orgNodes = new(orgCount);
        for(int i = 0; i < orgCount; i++)
        {
            orgNodes.Add(Mint("org"));
        }

        for(int i = 0; i + 1 < orgCount; i++)
        {
            if((i + 1) % chainDepth != 0)
            {
                Add(orgNodes[i], supplies, orgNodes[i + 1]);
            }
        }

        //Re-resolution cliques: duplicate identities merged by
        //owl:sameAs into cliques of 2-4, each duplicate carrying a supplies edge
        //so the merge replays real data. The bridge is retract-burst churn.
        List<(TermId A, TermId B)> sameAsBridges = [];
        int cliqueCount = Math.Max(entities / 300, 2);
        Random random = new(seed);
        for(int c = 0; c < cliqueCount; c++)
        {
            int cliqueSize = 2 + (c % 3);
            TermId anchor = orgNodes[random.Next(orgNodes.Count)];
            TermId previous = anchor;
            for(int m = 1; m < cliqueSize; m++)
            {
                TermId duplicate = Mint("dup");
                Add(duplicate, supplies, orgNodes[random.Next(orgNodes.Count)]);
                Add(previous, terms.SameAs, duplicate);
                if(m == 1)
                {
                    sameAsBridges.Add((previous, duplicate));
                }

                previous = duplicate;
            }
        }

        //Flat payload: attribute triples per root node — the append-dominant
        //bulk that derives nothing but grows the base.
        Dictionary<TermId, List<EncodedTriple>> productCertifications = new(productCount);
        foreach(TermId product in productNodes)
        {
            List<EncodedTriple> certs = new(CertificationsPerProduct);
            for(int k = 0; k < CertificationsPerProduct; k++)
            {
                TermId cert = Mint("cert");
                EncodedTriple triple = EncodedTriple.FromEncoded(product.Encoded, hasCertification.Encoded, cert.Encoded);
                baseSet.Add(triple);
                certs.Add(triple);
            }

            productCertifications[product] = certs;
        }

        return new SharedSubpartDagCorpus(
            dictionary,
            terms,
            baseSet,
            partOf,
            supplies,
            hasCertification,
            productClass,
            productNodes,
            subassemblyNodes,
            orgNodes,
            sameAsBridges,
            productCertifications,
            leaves,
            ordinal);
    }

    /// <summary>Appends a batch of new root nodes with their attribute triples and a DAG attachment — the steady append-dominant growth.</summary>
    /// <param name="random">The deterministic op-stream source.</param>
    /// <returns>The op's exact net base delta.</returns>
    public SharedSubpartDagDelta AppendBatch(Random random)
    {
        HashSet<EncodedTriple> added = [];
        HashSet<EncodedTriple> retracted = [];

        int batch = Math.Max(Products.Count / 4, 8);
        for(int i = 0; i < batch; i++)
        {
            TermId product = Mint("prod");
            Products.Add(product);
            RecordAdd(EncodedTriple.FromEncoded(product.Encoded, Terms.Type.Encoded, ProductClass.Encoded), added, retracted);

            //Attach an existing subassembly under the new product, extending the
            //shared-subpart roll-up.
            TermId subassembly = SubassemblyNodes[random.Next(SubassemblyNodes.Count)];
            RecordAdd(EncodedTriple.FromEncoded(subassembly.Encoded, PartOf.Encoded, product.Encoded), added, retracted);

            List<EncodedTriple> certs = new(CertificationsPerProduct);
            for(int k = 0; k < CertificationsPerProduct; k++)
            {
                TermId cert = Mint("cert");
                EncodedTriple triple = EncodedTriple.FromEncoded(product.Encoded, HasCertification.Encoded, cert.Encoded);
                RecordAdd(triple, added, retracted);
                certs.Add(triple);
            }

            ProductCertifications[product] = certs;
        }

        return new SharedSubpartDagDelta(added, retracted);
    }

    /// <summary>Runs a retract burst: an attribute rewrite (retract an attribute set, re-add a corrected one) and an entity re-resolution (retract a sameAs bridge, re-add a different one).</summary>
    /// <param name="random">The deterministic op-stream source.</param>
    /// <returns>The op's exact net base delta.</returns>
    public SharedSubpartDagDelta RetractBurst(Random random)
    {
        HashSet<EncodedTriple> added = [];
        HashSet<EncodedTriple> retracted = [];

        int rewrites = Math.Max(Products.Count / 8, 4);
        for(int i = 0; i < rewrites; i++)
        {
            TermId product = Products[random.Next(Products.Count)];
            if(!ProductCertifications.TryGetValue(product, out List<EncodedTriple>? certs))
            {
                continue;
            }

            //Recall: retract the product's whole certification set, re-add a
            //corrected one.
            foreach(EncodedTriple stale in certs)
            {
                RecordRemove(stale, added, retracted);
            }

            List<EncodedTriple> corrected = new(CertificationsPerProduct);
            for(int k = 0; k < CertificationsPerProduct; k++)
            {
                TermId cert = Mint("cert");
                EncodedTriple triple = EncodedTriple.FromEncoded(product.Encoded, HasCertification.Encoded, cert.Encoded);
                RecordAdd(triple, added, retracted);
                corrected.Add(triple);
            }

            ProductCertifications[product] = corrected;
        }

        //Entity re-resolution: retract one sameAs bridge and re-add a different
        //one, moving a merge from one organization pair to another.
        if(SameAsBridges.Count > 0)
        {
            int index = random.Next(SameAsBridges.Count);
            (TermId a, TermId b) = SameAsBridges[index];
            RecordRemove(EncodedTriple.FromEncoded(a.Encoded, Terms.SameAs.Encoded, b.Encoded), added, retracted);

            TermId reAnchor = OrgNodes[random.Next(OrgNodes.Count)];
            EncodedTriple replacement = EncodedTriple.FromEncoded(reAnchor.Encoded, Terms.SameAs.Encoded, b.Encoded);
            RecordAdd(replacement, added, retracted);
            SameAsBridges[index] = (reAnchor, b);
        }

        return new SharedSubpartDagDelta(added, retracted);
    }

    /// <summary>
    /// Routes a base addition through the op's net-delta recording: the triple
    /// enters <paramref name="added"/> only when it is genuinely new to the base
    /// and does not cancel an earlier retract of the same triple in this op, so
    /// <c>Added ∩ Retracted = ∅</c> and the recorded pair is the op's exact net.
    /// </summary>
    /// <param name="triple">The triple asserted into the base.</param>
    /// <param name="added">The op's accumulating added set.</param>
    /// <param name="retracted">The op's accumulating retracted set.</param>
    private void RecordAdd(EncodedTriple triple, HashSet<EncodedTriple> added, HashSet<EncodedTriple> retracted)
    {
        if(BaseSet.Add(triple) && !retracted.Remove(triple))
        {
            added.Add(triple);
        }
    }

    /// <summary>
    /// Routes a base removal through the op's net-delta recording: the triple
    /// enters <paramref name="retracted"/> only when it was genuinely present
    /// and does not cancel an earlier add of the same triple in this op, so
    /// <c>Added ∩ Retracted = ∅</c> and the recorded pair is the op's exact net.
    /// </summary>
    /// <param name="triple">The triple removed from the base.</param>
    /// <param name="added">The op's accumulating added set.</param>
    /// <param name="retracted">The op's accumulating retracted set.</param>
    private void RecordRemove(EncodedTriple triple, HashSet<EncodedTriple> added, HashSet<EncodedTriple> retracted)
    {
        if(BaseSet.Remove(triple) && !added.Remove(triple))
        {
            retracted.Add(triple);
        }
    }

    /// <summary>Mints a fresh IRI in the corpus namespace, keeping generation deterministic.</summary>
    /// <param name="prefix">The local-name prefix.</param>
    /// <returns>The minted identifier.</returns>
    private TermId Mint(string prefix)
    {
        return Dictionary.GetOrAdd(new NamedNode(Utf8Strings.From($"{Namespace}{prefix}{NextOrdinal++}")));
    }
}

/// <summary>
/// The exact net base delta of one <see cref="SharedSubpartDagCorpus"/> op: the facts
/// it asserts into and retracts from the base, sequentially flattened so
/// <c>Added ∩ Retracted = ∅</c> and
/// <c>post-base = (pre-base \ Retracted) ∪ Added</c> hold regardless of the
/// intra-op mutation order — an add of a triple retracted earlier in the same
/// op, or a retract of one added earlier, nets to no entry in either set.
/// </summary>
/// <param name="Added">The facts the op newly asserts into the base.</param>
/// <param name="Retracted">The facts the op removes from the base.</param>
internal readonly record struct SharedSubpartDagDelta(
    IReadOnlyCollection<EncodedTriple> Added,
    IReadOnlyCollection<EncodedTriple> Retracted);
