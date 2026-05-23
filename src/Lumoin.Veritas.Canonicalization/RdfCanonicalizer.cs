using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Implements the W3C RDF Dataset Canonicalization algorithm (RDFC-1.0).
/// </summary>
/// <remarks>
/// <para>
/// RDFC-1.0 takes an RDF dataset containing blank nodes and produces a canonical
/// serialization where every blank node is assigned a deterministic identifier.
/// Two datasets with the same graph structure but different blank node labels
/// will produce identical canonical output.
/// </para>
/// <para>
/// The algorithm is defined at https://www.w3.org/TR/rdf-canon/ and operates in
/// four phases:
/// </para>
/// <list type="number">
/// <item>Collect all quads and identify blank nodes.</item>
/// <item>Hash every quad containing each blank node to build an initial identifier map.</item>
/// <item>For blank nodes that cannot be uniquely identified by their immediate neighbourhood
///   (hash collisions), use an iterative graph traversal (URDNA2015 hash n-degree quads)
///   to distinguish them.</item>
/// <item>Assign canonical identifiers in hash order and serialize to N-Quads.</item>
/// </list>
/// <para>
/// The hash function is injected via <see cref="HashDelegate"/> following the project
/// convention that cryptographic operations are supplied by the caller.
/// </para>
/// </remarks>
public static class RdfCanonicalizer
{
    /// <summary>
    /// Canonicalizes an RDF dataset and returns the canonical N-Quads serialization.
    /// </summary>
    /// <param name="quads">The quads forming the dataset to canonicalize.</param>
    /// <param name="hash">
    /// The hash function to use. RDFC-1.0 requires SHA-256.
    /// Pass <c>System.Security.Cryptography.SHA256.HashData</c>.
    /// </param>
    /// <returns>
    /// The canonical N-Quads serialization as a UTF-8 string. Lines are sorted
    /// lexicographically. The string uses <c>\n</c> line terminators throughout.
    /// </returns>
    public static string Canonicalize(IEnumerable<Quad> quads, HashDelegate hash)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(hash);

        List<Quad> dataset = quads is ICollection<Quad> sourceCollection
            ? new List<Quad>(sourceCollection.Count)
            : [];
        foreach(Quad q in quads)
        {
            dataset.Add(q);
        }

        //Step 1. Collect blank node to quads mapping.
        //For each blank node identifier, record every quad in which it appears.
        Dictionary<string, List<Quad>> blankNodeToQuads = BuildBlankNodeToQuads(dataset);

        if(blankNodeToQuads.Count == 0)
        {
            //No blank nodes: serialize quads directly in sorted order.
            return SerializeWithoutBlankNodes(dataset);
        }

        //Step 2. Compute an initial hash for each blank node based on its surrounding quads.
        //Blank nodes whose immediate neighbourhood produces a unique hash can be assigned
        //a canonical identifier immediately.
        Dictionary<string, string> hashToBlank = [];
        Dictionary<string, string> canonicalMap = [];
        List<string> nonUniqueHashes = [];

        //Assign canonical labels in two passes: first simple (unique hash), then complex.
        int canonicalCounter = 0;
        Dictionary<string, string> simpleMap = [];

        foreach(string blankId in blankNodeToQuads.Keys)
        {
            string h = HashFirstDegreeQuads(blankId, blankNodeToQuads[blankId], hash);

            if(!hashToBlank.TryGetValue(h, out string? existing))
            {
                hashToBlank[h] = blankId;
            }
            else
            {
                //Hash collision: this blank node needs the n-degree algorithm.
                if(!nonUniqueHashes.Contains(h))
                {
                    nonUniqueHashes.Add(h);
                }
            }

            simpleMap[blankId] = h;
        }

        //Assign canonical identifiers to uniquely-hashed blank nodes.
        List<string> uniqueHashes = new(hashToBlank.Count);
        foreach(string hashKey in hashToBlank.Keys)
        {
            if(!nonUniqueHashes.Contains(hashKey))
            {
                uniqueHashes.Add(hashKey);
            }
        }

        uniqueHashes.Sort(StringComparer.Ordinal);

        foreach(string h in uniqueHashes)
        {
            string blankId = hashToBlank[h];
            canonicalMap[blankId] = $"c14n{canonicalCounter}";
            canonicalCounter++;
        }

        //Step 3. For hash collisions, use the n-degree quads algorithm.
        HashSet<string> nonUniqueHashSet = new(nonUniqueHashes.Count);
        foreach(string h in nonUniqueHashes)
        {
            nonUniqueHashSet.Add(h);
        }

        //blankNodeToQuads.Keys are already distinct; the membership test below
        //is over distinct ids, so no extra de-duplication is needed.
        List<string> collisionBlankIds = [];
        foreach(string id in blankNodeToQuads.Keys)
        {
            if(nonUniqueHashSet.Contains(simpleMap[id]))
            {
                collisionBlankIds.Add(id);
            }
        }

        //Hash n-degree quads for each collision blank node to get a unique identifier.
        List<(string Hash, List<string> Identifiers)> ndegreeResults = [];

        HashSet<string> issuerQueue = [.. collisionBlankIds];
        IdentifierIssuer canonicalIssuer = new("c14n", canonicalCounter);

        //Group collision blank nodes by their first-degree hash, then within each group
        //compute the n-degree hash to get a stable ordering.
        string[] sortedNonUniqueHashes = new string[nonUniqueHashes.Count];
        for(int i = 0; i < nonUniqueHashes.Count; i++)
        {
            sortedNonUniqueHashes[i] = nonUniqueHashes[i];
        }

        Array.Sort(sortedNonUniqueHashes, StringComparer.Ordinal);

        foreach(string groupHash in sortedNonUniqueHashes)
        {
            List<string> groupMembers = [];
            foreach(string id in blankNodeToQuads.Keys)
            {
                if(simpleMap[id] == groupHash)
                {
                    groupMembers.Add(id);
                }
            }

            List<(string NdHash, string BlankId)> groupResults = [];

            foreach(string blankId in groupMembers)
            {
                if(canonicalMap.ContainsKey(blankId))
                {
                    continue;
                }

                IdentifierIssuer tempIssuer = new("b");
                string ndHash = HashNDegreeQuads(blankId, blankNodeToQuads, simpleMap, canonicalMap, tempIssuer, hash);
                groupResults.Add((ndHash, blankId));
            }

            //Assign canonical identifiers in n-degree hash order.
            groupResults.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.NdHash, b.NdHash));

            foreach((string _, string blankId) in groupResults)
            {
                canonicalMap[blankId] = canonicalIssuer.Issue(blankId);
            }
        }

        //Step 4. Serialize all quads using canonical blank node identifiers, sort, and join.
        string[] lines = new string[dataset.Count];
        for(int i = 0; i < dataset.Count; i++)
        {
            lines[i] = NQuadsSerializer.SerializeQuad(dataset[i], canonicalMap);
        }

        Array.Sort(lines, StringComparer.Ordinal);

        return string.Concat(lines);
    }

    /// <summary>
    /// Builds the blank-node-to-quads index: for each blank node label that appears
    /// anywhere in a quad (subject, object, or graph), records all quads containing it.
    /// </summary>
    private static Dictionary<string, List<Quad>> BuildBlankNodeToQuads(List<Quad> dataset)
    {
        Dictionary<string, List<Quad>> index = [];
        HashSet<string> labelsInQuad = [];

        foreach(Quad quad in dataset)
        {
            //Collect the distinct blank-node labels of the quad, recursing into triple terms.
            //A quad is registered once per distinct label even if a label occurs in more than
            //one position (for example a self-referential triple _:a :p _:a).
            labelsInQuad.Clear();
            CollectBlankNodeLabels(quad.Subject, labelsInQuad);
            CollectBlankNodeLabels(quad.Object, labelsInQuad);
            CollectBlankNodeLabels(quad.Graph, labelsInQuad);

            foreach(string label in labelsInQuad)
            {
                if(!index.TryGetValue(label, out List<Quad>? list))
                {
                    list = [];
                    index[label] = list;
                }

                list.Add(quad);
            }
        }

        return index;
    }

    /// <summary>
    /// Adds every blank-node label reachable from a term — directly, or nested within a
    /// triple term — to the supplied sink. The term tree is walked with an explicit stack so
    /// nested triple terms do not recurse.
    /// </summary>
    private static void CollectBlankNodeLabels(RdfTerm? term, ICollection<string> sink)
    {
        //Fast path for the common terms that cannot nest, avoiding a stack allocation.
        switch(term)
        {
            case null or NamedNode or Literal:
            {
                return;
            }

            case BlankNode topBlank:
            {
                sink.Add(topBlank.Label.ToString());

                return;
            }

            default:
            {
                break;
            }
        }

        Stack<RdfTerm> pending = new();
        pending.Push(term);

        while(pending.Count > 0)
        {
            RdfTerm current = pending.Pop();
            switch(current)
            {
                case BlankNode blank:
                {
                    sink.Add(blank.Label.ToString());

                    break;
                }

                case TripleTerm tripleTerm:
                {
                    pending.Push(tripleTerm.Subject);
                    pending.Push(tripleTerm.Object);

                    break;
                }

                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Implements the RDFC-1.0 "Hash First Degree Quads" algorithm.
    /// </summary>
    /// <remarks>
    /// For the given blank node, serializes every quad in which it appears,
    /// replacing the blank node with a placeholder <c>_:a</c> and all other
    /// blank nodes with <c>_:z</c>. The sorted serializations are hashed together
    /// to produce a hash that reflects the blank node's immediate neighbourhood.
    /// </remarks>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "RDFC-1.0 requires lowercase hex output by specification.")]
    private static string HashFirstDegreeQuads(
        string blankId,
        List<Quad> quads,
        HashDelegate hash)
    {
        string[] nquads = new string[quads.Count];
        for(int i = 0; i < quads.Count; i++)
        {
            nquads[i] = SerializeForFirstDegree(quads[i], blankId);
        }

        Array.Sort(nquads, StringComparer.Ordinal);

        string joined = string.Concat(nquads);
        byte[] hashBytes = hash(Encoding.UTF8.GetBytes(joined));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Serializes a quad for first-degree hashing, replacing the reference blank node
    /// with <c>_:a</c> and all other blank nodes with <c>_:z</c>.
    /// </summary>
    private static string SerializeForFirstDegree(Quad quad, string referenceBlankId)
    {
        Dictionary<string, string> placeholderMap = BuildPlaceholderMap(quad, referenceBlankId);

        return NQuadsSerializer.SerializeQuad(quad, placeholderMap);
    }

    /// <summary>
    /// Builds a placeholder map for first-degree hashing. The reference blank node
    /// is mapped to <c>a</c> and all other blank nodes to <c>z</c>.
    /// </summary>
    private static Dictionary<string, string> BuildPlaceholderMap(Quad quad, string referenceBlankId)
    {
        Dictionary<string, string> map = [];
        HashSet<string> labels = [];
        CollectBlankNodeLabels(quad.Subject, labels);
        CollectBlankNodeLabels(quad.Object, labels);
        CollectBlankNodeLabels(quad.Graph, labels);

        foreach(string label in labels)
        {
            map[label] = label == referenceBlankId ? "a" : "z";
        }

        return map;
    }

    /// <summary>
    /// Implements the RDFC-1.0 "Hash N-Degree Quads" algorithm.
    /// </summary>
    /// <remarks>
    /// Used for blank nodes whose first-degree hash collides with another blank node.
    /// Traverses the graph iteratively, hashing connected blank nodes to produce a
    /// path-dependent hash that can distinguish structurally different blank nodes
    /// even in complex graph topologies.
    /// </remarks>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "RDFC-1.0 requires lowercase hex output by specification.")]
    private static string HashNDegreeQuads(
        string blankId,
        Dictionary<string, List<Quad>> blankNodeToQuads,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash)
    {
        //Collect related blank nodes grouped by their first-degree hash.
        Dictionary<string, List<string>> hashToRelated = [];

        foreach(Quad quad in blankNodeToQuads.TryGetValue(blankId, out List<Quad>? quads) ? quads : [])
        {
            AddRelatedBlankNodes(quad, blankId, hashMap, canonicalMap, issuer, hash, hashToRelated, blankNodeToQuads);
        }

        //Build a data-to-hash string by iterating related blank nodes in hash order.
        StringBuilder dataToHash = new();

        string[] sortedRelatedHashes = new string[hashToRelated.Count];
        int relatedIndex = 0;
        foreach(string h in hashToRelated.Keys)
        {
            sortedRelatedHashes[relatedIndex++] = h;
        }

        Array.Sort(sortedRelatedHashes, StringComparer.Ordinal);

        foreach(string relatedHash in sortedRelatedHashes)
        {
            dataToHash.Append(relatedHash);

            string? chosenPath = null;
            IdentifierIssuer? chosenIssuer = null;

            List<string> candidates = hashToRelated[relatedHash];

            foreach(string related in candidates)
            {
                IdentifierIssuer tempIssuer = issuer.Clone();

                //Issue a temporary identifier for this candidate.
                if(!canonicalMap.ContainsKey(related))
                {
                    tempIssuer.Issue(related);
                }

                StringBuilder path = new();

                //Walk the graph from this candidate, building a path string.
                Queue<string> recursionQueue = new();
                recursionQueue.Enqueue(related);

                while(recursionQueue.Count > 0)
                {
                    string current = recursionQueue.Dequeue();

                    if(canonicalMap.TryGetValue(current, out string? canonical))
                    {
                        path.Append('_');
                        path.Append(':');
                        path.Append(canonical);
                    }
                    else
                    {
                        if(!tempIssuer.HasIssued(current))
                        {
                            //Queue related blank nodes of current for further processing.
                            if(blankNodeToQuads.TryGetValue(current, out List<Quad>? currentQuads))
                            {
                                foreach(Quad q in currentQuads)
                                {
                                    EnqueueRelated(q, current, tempIssuer, recursionQueue);
                                }
                            }
                        }

                        path.Append('_');
                        path.Append(':');
                        path.Append(tempIssuer.Issue(current));
                    }
                }

                string pathStr = path.ToString();
                if(chosenPath is null || string.Compare(pathStr, chosenPath, StringComparison.Ordinal) < 0)
                {
                    chosenPath = pathStr;
                    chosenIssuer = tempIssuer;
                }
            }

            dataToHash.Append(chosenPath);

            //Merge the chosen issuer's assignments into the main issuer.
            if(chosenIssuer is not null)
            {
                foreach((string original, string issued) in chosenIssuer.IssuedPairs())
                {
                    issuer.Adopt(original, issued);
                }
            }
        }

        byte[] hashBytes = hash(Encoding.UTF8.GetBytes(dataToHash.ToString()));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Collects blank nodes related to <paramref name="referenceBlankId"/> from a single quad,
    /// grouping them by their position-dependent hash.
    /// </summary>
    private static void AddRelatedBlankNodes(
        Quad quad,
        string referenceBlankId,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash,
        Dictionary<string, List<string>> hashToRelated,
        Dictionary<string, List<Quad>> blankNodeToQuads)
    {
        void AddRelated(BlankNode blank, string position)
        {
            string relatedId = blank.Label.ToString();
            if(relatedId == referenceBlankId)
            {
                return;
            }

            string relatedHash = GetRelatedHash(relatedId, quad, position, hashMap, canonicalMap, issuer, hash);

            if(!hashToRelated.TryGetValue(relatedHash, out List<string>? list))
            {
                list = [];
                hashToRelated[relatedHash] = list;
            }

            if(!list.Contains(relatedId))
            {
                list.Add(relatedId);
            }
        }

        //The first-degree hashing and blank-node discovery recurse into triple terms; this
        //n-degree related-node extraction inspects only the top-level positions. That suffices
        //for every current test because blank nodes nested in triple terms there are uniquely
        //identified by their first-degree hash and never reach the n-degree disambiguation. A
        //dataset that both nests a blank node in a triple term and needs n-degree to distinguish
        //it would require recursing here with a position semantics the RDF-star canonicalization
        //rules have not yet pinned down; that is left for a follow-up.
        if(quad.Subject is BlankNode subjectBlank)
        {
            AddRelated(subjectBlank, "s");
        }

        if(quad.Object is BlankNode objectBlank)
        {
            AddRelated(objectBlank, "o");
        }

        if(quad.Graph is BlankNode graphBlank)
        {
            AddRelated(graphBlank, "g");
        }
    }

    /// <summary>
    /// Computes a position-dependent hash for a related blank node in the n-degree algorithm.
    /// The hash incorporates the quad position (<c>s</c>, <c>o</c>, or <c>g</c>),
    /// the predicate IRI, and the blank node's canonical or temporary identifier.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "RDFC-1.0 requires lowercase hex output by specification.")]
    private static string GetRelatedHash(
        string relatedId,
        Quad quad,
        string position,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash)
    {
        //Use canonical identifier if available, otherwise temporary.
        string identifier = canonicalMap.TryGetValue(relatedId, out string? canonical)
            ? $"_:{canonical}"
            : hashMap.TryGetValue(relatedId, out string? existing)
                ? existing
                : $"_:{issuer.Issue(relatedId)}";

        string predicate = $"<{quad.Predicate.Iri}>";
        string input = $"{position}{predicate}{identifier}";

        byte[] hashBytes = hash(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Enqueues blank nodes from a quad that are related to <paramref name="currentId"/>
    /// and have not yet been issued a temporary identifier by <paramref name="issuer"/>.
    /// </summary>
    private static void EnqueueRelated(Quad quad, string currentId, IdentifierIssuer issuer, Queue<string> queue)
    {
        void TryEnqueue(RdfTerm? term)
        {
            if(term is BlankNode blank)
            {
                string label = blank.Label.ToString();
                if(label != currentId && !issuer.HasIssued(label))
                {
                    queue.Enqueue(label);
                }
            }
        }

        TryEnqueue(quad.Subject);
        TryEnqueue(quad.Object);
        TryEnqueue(quad.Graph);
    }

    /// <summary>
    /// Serializes a dataset with no blank nodes by sorting the N-Quads lines lexicographically.
    /// </summary>
    private static string SerializeWithoutBlankNodes(List<Quad> dataset)
    {
        Dictionary<string, string> emptyMap = [];
        string[] lines = new string[dataset.Count];
        for(int i = 0; i < dataset.Count; i++)
        {
            lines[i] = NQuadsSerializer.SerializeQuad(dataset[i], emptyMap);
        }

        Array.Sort(lines, StringComparer.Ordinal);

        return string.Concat(lines);
    }

    /// <summary>
    /// Serializes quads to canonical N-Triples / N-Quads lexical form in
    /// document order, applying the same per-term escaping, language-tag
    /// lower-casing, and triple-term rendering as <see cref="Canonicalize"/>,
    /// but without the dataset-level line reordering or blank-node
    /// relabelling.
    /// </summary>
    /// <remarks>
    /// This matches the per-statement canonical form the W3C N-Triples and
    /// N-Quads canonicalization tests expect: those tests canonicalize the
    /// lexical representation of each statement while preserving document
    /// order, which is distinct from the RDFC-1.0 dataset canonicalization
    /// <see cref="Canonicalize"/> performs (sorted lines, canonical
    /// blank-node identifiers). Blank-node labels pass through unchanged.
    /// </remarks>
    /// <param name="quads">The quads to serialize, in the order to emit them.</param>
    /// <returns>The concatenated canonical statement lines, each ending with <c> .\n</c>.</returns>
    public static string SerializeStatements(IEnumerable<Quad> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);

        Dictionary<string, string> emptyMap = [];
        StringBuilder builder = new();
        foreach(Quad quad in quads)
        {
            builder.Append(NQuadsSerializer.SerializeQuad(quad, emptyMap));
        }

        return builder.ToString();
    }
}
