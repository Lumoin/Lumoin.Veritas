using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

        List<Quad> dataset = quads.ToList();

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
        List<string> uniqueHashes = hashToBlank.Keys
            .Where(h => !nonUniqueHashes.Contains(h))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();

        foreach(string h in uniqueHashes)
        {
            string blankId = hashToBlank[h];
            canonicalMap[blankId] = $"c14n{canonicalCounter}";
            canonicalCounter++;
        }

        //Step 3. For hash collisions, use the n-degree quads algorithm.
        List<string> collisionBlankIds = nonUniqueHashes
            .SelectMany(h => blankNodeToQuads.Keys.Where(id => simpleMap[id] == h))
            .Distinct()
            .ToList();

        //Hash n-degree quads for each collision blank node to get a unique identifier.
        List<(string Hash, List<string> Identifiers)> ndegreeResults = [];

        HashSet<string> issuerQueue = [.. collisionBlankIds];
        IdentifierIssuer canonicalIssuer = new("c14n", canonicalCounter);

        //Group collision blank nodes by their first-degree hash, then within each group
        //compute the n-degree hash to get a stable ordering.
        foreach(string groupHash in nonUniqueHashes.OrderBy(h => h, StringComparer.Ordinal))
        {
            List<string> groupMembers = blankNodeToQuads.Keys
                .Where(id => simpleMap[id] == groupHash)
                .ToList();

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
            foreach((string _, string blankId) in groupResults.OrderBy(r => r.NdHash, StringComparer.Ordinal))
            {
                canonicalMap[blankId] = canonicalIssuer.Issue(blankId);
            }
        }

        //Step 4. Serialize all quads using canonical blank node identifiers, sort, and join.
        List<string> lines = dataset
            .Select(q => NQuadsSerializer.SerializeQuad(q, canonicalMap))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        return string.Concat(lines);
    }

    /// <summary>
    /// Builds the blank-node-to-quads index: for each blank node label that appears
    /// anywhere in a quad (subject, object, or graph), records all quads containing it.
    /// </summary>
    private static Dictionary<string, List<Quad>> BuildBlankNodeToQuads(List<Quad> dataset)
    {
        Dictionary<string, List<Quad>> index = [];

        void Register(string blankId, Quad quad)
        {
            if(!index.TryGetValue(blankId, out List<Quad>? list))
            {
                list = [];
                index[blankId] = list;
            }

            list.Add(quad);
        }

        foreach(Quad quad in dataset)
        {
            if(quad.Subject is BlankNode subjectBlank)
            {
                Register(subjectBlank.Label.ToString(), quad);
            }

            if(quad.Object is BlankNode objectBlank)
            {
                Register(objectBlank.Label.ToString(), quad);
            }

            if(quad.Graph is BlankNode graphBlank)
            {
                Register(graphBlank.Label.ToString(), quad);
            }
        }

        return index;
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
        List<string> nquads = quads
            .Select(q => SerializeForFirstDegree(q, blankId))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

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

        void Map(RdfTerm? term)
        {
            if(term is BlankNode blank)
            {
                string label = blank.Label.ToString();
                map[label] = label == referenceBlankId ? "a" : "z";
            }
        }

        Map(quad.Subject);
        Map(quad.Object);
        Map(quad.Graph);

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

        foreach(string relatedHash in hashToRelated.Keys.OrderBy(h => h, StringComparer.Ordinal))
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
        List<string> lines = dataset
            .Select(q => NQuadsSerializer.SerializeQuad(q, emptyMap))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        return string.Concat(lines);
    }
}
