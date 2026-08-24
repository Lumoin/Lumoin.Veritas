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
    //The hash-n-degree algorithm explores permutations of mutually-related blank nodes; a complete clique of N
    //blank nodes drives this factorially. This bound is orders of magnitude above any well-formed dataset's needs
    //(the W3C eval suite's most symmetric graphs sit far below it) yet is reached quickly by a poison graph.
    private const long MaxNDegreePermutations = 1_000_000;

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
    /// <exception cref="RdfCanonicalizationException">The dataset's blank-node structure exceeds the work budget (a poison graph).</exception>
    public static string Canonicalize(IEnumerable<Quad> quads, HashDelegate hash)
    {
        return CanonicalizeCore(quads, hash).Canonical;
    }

    /// <summary>
    /// Canonicalizes an RDF dataset and returns the canonical N-Quads serialization together with the
    /// issued-identifier map (each input blank-node label to its <c>c14nN</c> canonical identifier).
    /// </summary>
    /// <param name="quads">The quads forming the dataset to canonicalize.</param>
    /// <param name="hash">The hash function (RDFC-1.0 requires SHA-256).</param>
    /// <returns>The canonical serialization and the issued-identifier map.</returns>
    /// <exception cref="RdfCanonicalizationException">The dataset's blank-node structure exceeds the work budget (a poison graph).</exception>
    public static RdfCanonicalizationResult CanonicalizeWithMap(IEnumerable<Quad> quads, HashDelegate hash)
    {
        return CanonicalizeCore(quads, hash);
    }

    private static RdfCanonicalizationResult CanonicalizeCore(IEnumerable<Quad> quads, HashDelegate hash)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(hash);

        //Bounds the permutations the hash-n-degree algorithm may explore across the whole canonicalization; a
        //highly-symmetric "poison" graph (a clique of mutually-related blank nodes) would otherwise demand a
        //factorial amount of work. The limit is far above any well-formed dataset's needs.
        WorkBudget budget = new(MaxNDegreePermutations);

        //An RDF dataset is a SET of quads: RDFC-1.0 operates on distinct quads, so duplicate input quads are removed
        //before hashing and serialization (otherwise a blank node's first-degree hash would double-count a repeated
        //quad, and the output would carry a duplicate line). Quad value-equality covers all four positions.
        List<Quad> dataset = quads is ICollection<Quad> sourceCollection
            ? new List<Quad>(sourceCollection.Count)
            : [];
        HashSet<Quad> seenQuads = [];
        foreach(Quad q in quads)
        {
            if(seenQuads.Add(q))
            {
                dataset.Add(q);
            }
        }

        //Step 1. Collect blank node to quads mapping.
        //For each blank node identifier, record every quad in which it appears.
        Dictionary<string, List<Quad>> blankNodeToQuads = BuildBlankNodeToQuads(dataset);

        if(blankNodeToQuads.Count == 0)
        {
            //No blank nodes: serialize quads directly in sorted order.
            return new RdfCanonicalizationResult(SerializeWithoutBlankNodes(dataset), new Dictionary<string, string>());
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

            List<(string NdHash, IdentifierIssuer Issuer)> groupResults = [];

            foreach(string blankId in groupMembers)
            {
                if(canonicalMap.ContainsKey(blankId))
                {
                    continue;
                }

                //RDFC-1.0 §4.5.3 step 6.2: seed the temporary issuer with an
                //identifier for this blank node, THEN compute its n-degree hash
                //(which issues identifiers for the related blank nodes reached
                //along the chosen path, in order). The hashing replaces the working
                //issuer as it resolves each group, so the RETURNED issuer — not the
                //seed — carries the final issued order used for assignment.
                IdentifierIssuer tempIssuer = new("b");
                tempIssuer.Issue(blankId);
                (string ndHash, IdentifierIssuer resultIssuer) = HashNDegreeQuads(blankId, blankNodeToQuads, simpleMap, canonicalMap, tempIssuer, hash, budget);
                groupResults.Add((ndHash, resultIssuer));
            }

            //Step 6.3: process the results in n-degree hash order and, for each,
            //assign canonical identifiers for EVERY blank node in that result's
            //issuer in its issued order — not just the group member. This path
            //ordering is what canonically (input-order-independently) resolves
            //automorphic blank nodes; assigning only the group member would leak
            //the input ordering when two members share an n-degree hash.
            groupResults.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.NdHash, b.NdHash));

            foreach((string _, IdentifierIssuer resultIssuer) in groupResults)
            {
                foreach(string original in resultIssuer.IssuedOrder)
                {
                    if(!canonicalMap.ContainsKey(original))
                    {
                        canonicalMap[original] = canonicalIssuer.Issue(original);
                    }
                }
            }
        }

        //Step 4. Serialize all quads using canonical blank node identifiers, sort, and join.
        string[] lines = new string[dataset.Count];
        for(int i = 0; i < dataset.Count; i++)
        {
            lines[i] = NQuadsSerializer.SerializeQuad(dataset[i], canonicalMap);
        }

        Array.Sort(lines, StringComparer.Ordinal);

        return new RdfCanonicalizationResult(string.Concat(lines), canonicalMap);
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
    /// Implements the RDFC-1.0 "Hash N-Degree Quads" algorithm (§4.8.3) for a blank node whose first-degree hash
    /// collides with another's, producing both the path-dependent hash that distinguishes structurally different
    /// blank nodes and the issuer recording the identifiers issued, in order, along the chosen path.
    /// </summary>
    /// <remarks>
    /// The algorithm is recursive in the specification: each related blank node reached along a candidate path is
    /// itself hashed by this routine, and every <b>permutation</b> of the blank nodes that share a related hash is
    /// tried so that automorphic (symmetric) siblings resolve to one canonical, input-order-independent order. This
    /// port keeps those exact semantics but runs over an explicit frame stack rather than the call stack (recursion
    /// is banned project-wide): each frame is one logical activation, and a frame that must hash a related node
    /// pushes a child frame and resumes when the child completes.
    /// </remarks>
    /// <param name="blankId">The blank node being hashed.</param>
    /// <param name="blankNodeToQuads">The blank-node-to-quads index.</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="issuer">The temporary issuer seeded with <paramref name="blankId"/>.</param>
    /// <param name="hash">The hash function.</param>
    /// <returns>The n-degree hash and the issuer carrying the final issued order.</returns>
    private static (string Hash, IdentifierIssuer Issuer) HashNDegreeQuads(
        string blankId,
        Dictionary<string, List<Quad>> blankNodeToQuads,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash,
        WorkBudget budget)
    {
        Stack<NDegreeFrame> stack = new();
        stack.Push(new NDegreeFrame(blankId, issuer));
        (string Hash, IdentifierIssuer Issuer) result = default;
        bool hasResult = false;

        while(stack.Count > 0)
        {
            NDegreeFrame frame = stack.Peek();
            if(hasResult)
            {
                //Deliver a just-completed child's result to the parent now resuming at RecursionResume.
                frame.ChildResult = result;
                hasResult = false;
            }

            if(StepNDegreeFrame(frame, blankNodeToQuads, hashMap, canonicalMap, hash, stack, budget))
            {
                stack.Pop();
                result = (frame.ResultHash!, frame.Issuer);
                hasResult = true;
            }
        }

        return result;
    }

    /// <summary>
    /// Advances one n-degree frame's state machine until it either completes (returns <see langword="true"/>, with
    /// <see cref="NDegreeFrame.ResultHash"/> set) or suspends by pushing a child frame to hash a related node
    /// (returns <see langword="false"/>, the child now on top of <paramref name="stack"/>).
    /// </summary>
    /// <param name="frame">The frame to advance.</param>
    /// <param name="blankNodeToQuads">The blank-node-to-quads index.</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="hash">The hash function.</param>
    /// <param name="stack">The frame stack a recursion step pushes onto.</param>
    /// <returns><see langword="true"/> when the frame completed.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "RDFC-1.0 requires lowercase hex output by specification.")]
    private static bool StepNDegreeFrame(
        NDegreeFrame frame,
        Dictionary<string, List<Quad>> blankNodeToQuads,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        HashDelegate hash,
        Stack<NDegreeFrame> stack,
        WorkBudget budget)
    {
        while(true)
        {
            switch(frame.Stage)
            {
                case NDegreeStage.Start:
                {
                    frame.Groups = BuildRelatedGroups(frame.Identifier, blankNodeToQuads, hashMap, canonicalMap, frame.Issuer, hash);
                    frame.Stage = NDegreeStage.GroupLoop;

                    break;
                }

                case NDegreeStage.GroupLoop:
                {
                    if(frame.GroupIndex >= frame.Groups.Count)
                    {
                        byte[] bytes = hash(Encoding.UTF8.GetBytes(frame.DataToHash.ToString()));
                        frame.ResultHash = Convert.ToHexString(bytes).ToLowerInvariant();

                        return true;
                    }

                    frame.DataToHash.Append(frame.Groups[frame.GroupIndex].Hash);
                    frame.ChosenPath = null;
                    frame.ChosenIssuer = null;
                    frame.Permutation = [.. frame.Groups[frame.GroupIndex].Related];
                    frame.Permutation.Sort(StringComparer.Ordinal);
                    frame.PermutationStarted = false;
                    frame.Stage = NDegreeStage.PermutationLoop;

                    break;
                }

                case NDegreeStage.PermutationLoop:
                {
                    if(frame.PermutationStarted && !NextPermutation(frame.Permutation))
                    {
                        //Every permutation tried: commit the least path and its issuer, then advance to the next group.
                        frame.DataToHash.Append(frame.ChosenPath);
                        frame.Issuer = frame.ChosenIssuer!;
                        frame.GroupIndex++;
                        frame.Stage = NDegreeStage.GroupLoop;

                        break;
                    }

                    //Each permutation explored counts against the work budget; a poison graph exhausts it.
                    budget.Spend();

                    frame.PermutationStarted = true;
                    frame.PermutationIssuer = frame.Issuer.Clone();
                    frame.PermutationPath = new StringBuilder();
                    frame.RecursionList = [];

                    //§4.8.3 step 5.4.4 (first pass): issue a temporary id for each related node, recording the
                    //newly-issued ones for the recursion pass; a node already assigned a canonical id uses it.
                    bool skip = false;
                    foreach(string related in frame.Permutation)
                    {
                        if(canonicalMap.TryGetValue(related, out string? canonical))
                        {
                            frame.PermutationPath.Append("_:").Append(canonical);
                        }
                        else
                        {
                            if(!frame.PermutationIssuer.HasIssued(related))
                            {
                                frame.RecursionList.Add(related);
                            }

                            frame.PermutationPath.Append("_:").Append(frame.PermutationIssuer.Issue(related));
                        }

                        if(IsWorseThanChosen(frame))
                        {
                            skip = true;

                            break;
                        }
                    }

                    //A path already not better than the chosen one cannot win; skip to the next permutation.
                    frame.Stage = NDegreeStage.PermutationLoop;
                    if(!skip)
                    {
                        frame.RecursionIndex = 0;
                        frame.Stage = NDegreeStage.RecursionLoop;
                    }

                    break;
                }

                case NDegreeStage.RecursionLoop:
                {
                    if(frame.RecursionIndex >= frame.RecursionList.Count)
                    {
                        //Permutation complete: keep it when it is the least path seen for this group (§4.8.3 step 5.4.6).
                        string path = frame.PermutationPath.ToString();
                        if(frame.ChosenPath is null || string.CompareOrdinal(path, frame.ChosenPath) < 0)
                        {
                            frame.ChosenPath = path;
                            frame.ChosenIssuer = frame.PermutationIssuer;
                        }

                        frame.Stage = NDegreeStage.PermutationLoop;

                        break;
                    }

                    //§4.8.3 step 5.4.5: hash the related node recursively — push a child frame, resume at RecursionResume.
                    frame.Stage = NDegreeStage.RecursionResume;
                    stack.Push(new NDegreeFrame(frame.RecursionList[frame.RecursionIndex], frame.PermutationIssuer));

                    return false;
                }

                case NDegreeStage.RecursionResume:
                {
                    //§4.8.3 step 5.4.5.2–5.4.5.4: append the related node's id (in the pre-recursion issuer) and the
                    //child's hash, then adopt the child's issuer for the next related node.
                    string related = frame.RecursionList[frame.RecursionIndex];
                    frame.PermutationPath.Append("_:").Append(frame.PermutationIssuer.Issue(related));
                    frame.PermutationPath.Append('<').Append(frame.ChildResult.Hash).Append('>');
                    frame.PermutationIssuer = frame.ChildResult.Issuer;

                    if(IsWorseThanChosen(frame))
                    {
                        frame.Stage = NDegreeStage.PermutationLoop;

                        break;
                    }

                    frame.RecursionIndex++;
                    frame.Stage = NDegreeStage.RecursionLoop;

                    break;
                }

                default:
                {
                    throw new InvalidOperationException($"Unexpected n-degree stage '{frame.Stage}'.");
                }
            }
        }
    }

    /// <summary>Groups the blank nodes related to <paramref name="blankId"/> by their position-dependent related hash, sorted by that hash (§4.8.3 steps 2–4).</summary>
    /// <param name="blankId">The blank node whose related nodes are grouped.</param>
    /// <param name="blankNodeToQuads">The blank-node-to-quads index.</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="issuer">The working issuer, consulted for already-issued temporary ids.</param>
    /// <param name="hash">The hash function.</param>
    /// <returns>The related-node groups, ascending by related hash.</returns>
    private static List<(string Hash, List<string> Related)> BuildRelatedGroups(
        string blankId,
        Dictionary<string, List<Quad>> blankNodeToQuads,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash)
    {
        Dictionary<string, List<string>> hashToRelated = [];
        foreach(Quad quad in blankNodeToQuads.TryGetValue(blankId, out List<Quad>? quads) ? quads : [])
        {
            AddRelatedBlankNodes(quad, blankId, hashMap, canonicalMap, issuer, hash, hashToRelated);
        }

        List<(string Hash, List<string> Related)> groups = new(hashToRelated.Count);
        foreach((string relatedHash, List<string> related) in hashToRelated)
        {
            groups.Add((relatedHash, related));
        }

        groups.Sort(static (a, b) => string.CompareOrdinal(a.Hash, b.Hash));

        return groups;
    }

    /// <summary>
    /// Returns whether the in-flight permutation path can no longer beat the chosen path and may be abandoned early
    /// (§4.8.3 steps 5.4.4.3 / 5.4.5.5): a path is hopeless once it is not shorter than the chosen path and orders
    /// after it.
    /// </summary>
    /// <param name="frame">The frame whose permutation path is tested.</param>
    /// <returns><see langword="true"/> when the permutation cannot improve on the chosen path.</returns>
    private static bool IsWorseThanChosen(NDegreeFrame frame)
    {
        return frame.ChosenPath is string chosen
            && frame.PermutationPath.Length >= chosen.Length
            && string.CompareOrdinal(frame.PermutationPath.ToString(), chosen) > 0;
    }

    /// <summary>
    /// Advances <paramref name="items"/> in place to the next lexicographically (ordinal) greater permutation,
    /// returning <see langword="false"/> when it is already the greatest (the list having been the last permutation).
    /// The standard next-permutation algorithm, iterating all permutations without recursion when seeded from the
    /// sorted order.
    /// </summary>
    /// <param name="items">The permutation to advance.</param>
    /// <returns><see langword="true"/> when a next permutation exists.</returns>
    private static bool NextPermutation(List<string> items)
    {
        int pivot = items.Count - 2;
        while(pivot >= 0 && string.CompareOrdinal(items[pivot], items[pivot + 1]) >= 0)
        {
            pivot--;
        }

        if(pivot < 0)
        {
            return false;
        }

        int successor = items.Count - 1;
        while(string.CompareOrdinal(items[successor], items[pivot]) <= 0)
        {
            successor--;
        }

        (items[pivot], items[successor]) = (items[successor], items[pivot]);
        items.Reverse(pivot + 1, items.Count - pivot - 1);

        return true;
    }

    /// <summary>
    /// Collects blank nodes related to <paramref name="referenceBlankId"/> from a single quad,
    /// grouping them by their position-dependent hash.
    /// </summary>
    /// <param name="quad">The quad to inspect.</param>
    /// <param name="referenceBlankId">The blank node whose related nodes are being collected.</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="issuer">The working issuer, consulted by <see cref="GetRelatedHash"/> for already-issued temporary ids.</param>
    /// <param name="hash">The hash function.</param>
    /// <param name="hashToRelated">The related-hash to related-node-id map being built.</param>
    private static void AddRelatedBlankNodes(
        Quad quad,
        string referenceBlankId,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash,
        Dictionary<string, List<string>> hashToRelated)
    {
        //The first-degree hashing and blank-node discovery recurse into triple terms; this
        //n-degree related-node extraction inspects only the top-level positions. That suffices
        //for every current test because blank nodes nested in triple terms there are uniquely
        //identified by their first-degree hash and never reach the n-degree disambiguation. The
        //spec's resolution for a dataset that both nests a blank node in a triple term and needs
        //n-degree to distinguish it is to basic-encode the triple terms away first (RDF 1.2
        //Interoperability §3, available as a composable step in BasicEncoder) and then run RDFC-1.0
        //on the triple-term-free result, where the nested blank node becomes a top-level position
        //this extraction already handles. That pre-pass is not applied here by default because no
        //published conformance corpus pins the basic-encoded canonical output.
        if(quad.Subject is BlankNode subjectBlank)
        {
            AddRelatedBlankNode(subjectBlank, "s", referenceBlankId, quad, hashMap, canonicalMap, issuer, hash, hashToRelated);
        }

        if(quad.Object is BlankNode objectBlank)
        {
            AddRelatedBlankNode(objectBlank, "o", referenceBlankId, quad, hashMap, canonicalMap, issuer, hash, hashToRelated);
        }

        if(quad.Graph is BlankNode graphBlank)
        {
            AddRelatedBlankNode(graphBlank, "g", referenceBlankId, quad, hashMap, canonicalMap, issuer, hash, hashToRelated);
        }
    }

    /// <summary>
    /// Adds one related blank node (at quad position <paramref name="position"/>) to the related-hash map,
    /// threading the collection context as explicit parameters so the n-degree extraction needs no closure.
    /// </summary>
    /// <param name="blank">The blank node at the inspected position.</param>
    /// <param name="position">The quad position (<c>s</c>, <c>o</c>, or <c>g</c>).</param>
    /// <param name="referenceBlankId">The blank node whose related nodes are being collected.</param>
    /// <param name="quad">The quad being inspected.</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="issuer">The working issuer.</param>
    /// <param name="hash">The hash function.</param>
    /// <param name="hashToRelated">The related-hash to related-node-id map being built.</param>
    private static void AddRelatedBlankNode(
        BlankNode blank,
        string position,
        string referenceBlankId,
        Quad quad,
        Dictionary<string, string> hashMap,
        Dictionary<string, string> canonicalMap,
        IdentifierIssuer issuer,
        HashDelegate hash,
        Dictionary<string, List<string>> hashToRelated)
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

    /// <summary>
    /// Computes the "Hash Related Blank Node" value (RDFC-1.0 §4.8.2): the hash of the related node's quad position
    /// (<c>s</c>, <c>o</c>, or <c>g</c>), the predicate IRI (omitted for the graph position, which has none), and an
    /// identifier — the canonical id when assigned, otherwise the related node's first-degree hash (every blank node
    /// has one; the algorithm never mints a new id here, keeping the call read-only on the issuer state).
    /// </summary>
    /// <param name="relatedId">The related blank node's label.</param>
    /// <param name="quad">The quad the related node appears in.</param>
    /// <param name="position">The position token (<c>s</c>, <c>o</c>, or <c>g</c>).</param>
    /// <param name="hashMap">Every blank node's first-degree hash.</param>
    /// <param name="canonicalMap">The blank nodes assigned a canonical identifier so far.</param>
    /// <param name="issuer">The working issuer; a temporary id already issued for the related node takes priority over its first-degree hash (this path-dependence distinguishes nodes reached along different routes).</param>
    /// <param name="hash">The hash function.</param>
    /// <returns>The lowercase-hex related hash.</returns>
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
        string identifier = canonicalMap.TryGetValue(relatedId, out string? canonical)
            ? "_:" + canonical
            : issuer.TryGetIssued(relatedId, out string? temporary)
                ? "_:" + temporary
                : hashMap.TryGetValue(relatedId, out string? firstDegree)
                    ? firstDegree
                    : "_:" + relatedId;

        //The predicate is part of the input for the subject/object positions; the graph position has no predicate.
        string input = position == "g"
            ? position + identifier
            : position + "<" + quad.Predicate.Iri + ">" + identifier;

        byte[] hashBytes = hash(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// A bound on the number of hash-n-degree permutations a single canonicalization may explore. Each explored
    /// permutation calls <see cref="Spend"/>; exceeding the limit throws <see cref="RdfCanonicalizationException"/>
    /// (the poison-graph guard).
    /// </summary>
    /// <param name="limit">The maximum number of permutations.</param>
    private sealed class WorkBudget(long limit)
    {
        private long spent;

        /// <summary>Records one explored permutation, throwing when the budget is exhausted.</summary>
        /// <exception cref="RdfCanonicalizationException">The budget has been exceeded.</exception>
        public void Spend()
        {
            spent++;
            if(spent > limit)
            {
                throw new RdfCanonicalizationException($"RDFC-1.0 canonicalization exceeded its work budget of {limit} hash-n-degree permutations; the blank-node structure is too complex to canonicalize (a poison graph).");
            }
        }
    }

    /// <summary>The stage of an in-flight <see cref="NDegreeFrame"/>'s state machine.</summary>
    private enum NDegreeStage
    {
        /// <summary>Build the related-node groups (entry).</summary>
        Start,

        /// <summary>Process the next related-hash group, or finish the frame.</summary>
        GroupLoop,

        /// <summary>Begin (or advance to) the next permutation of the current group.</summary>
        PermutationLoop,

        /// <summary>Process the next related node to hash recursively in the current permutation.</summary>
        RecursionLoop,

        /// <summary>Fold a completed child's result back into the current permutation's path.</summary>
        RecursionResume
    }

    /// <summary>One logical activation of the iterative "Hash N-Degree Quads" algorithm, holding the state a recursive call would keep on the stack.</summary>
    private sealed class NDegreeFrame
    {
        /// <summary>Constructs a frame for a blank node over a working issuer.</summary>
        /// <param name="identifier">The blank node this frame hashes.</param>
        /// <param name="issuer">The working issuer the frame threads through its groups.</param>
        public NDegreeFrame(string identifier, IdentifierIssuer issuer)
        {
            Identifier = identifier;
            Issuer = issuer;
        }

        /// <summary>The blank node this frame hashes.</summary>
        public string Identifier { get; }

        /// <summary>The working issuer, replaced by the chosen permutation's issuer as each group resolves.</summary>
        public IdentifierIssuer Issuer { get; set; }

        /// <summary>The current state-machine stage.</summary>
        public NDegreeStage Stage { get; set; } = NDegreeStage.Start;

        /// <summary>The accumulating data-to-hash buffer.</summary>
        public StringBuilder DataToHash { get; } = new();

        /// <summary>The related-node groups, ascending by related hash.</summary>
        public List<(string Hash, List<string> Related)> Groups { get; set; } = [];

        /// <summary>The index of the group currently being processed.</summary>
        public int GroupIndex { get; set; }

        /// <summary>The least permutation path found for the current group, or <see langword="null"/> before the first completes.</summary>
        public string? ChosenPath { get; set; }

        /// <summary>The issuer of the least permutation path for the current group.</summary>
        public IdentifierIssuer? ChosenIssuer { get; set; }

        /// <summary>The current permutation of the current group's related nodes.</summary>
        public List<string> Permutation { get; set; } = [];

        /// <summary>Whether the first permutation of the current group has been started (so the next visit advances it).</summary>
        public bool PermutationStarted { get; set; }

        /// <summary>The issuer clone for the in-flight permutation.</summary>
        public IdentifierIssuer PermutationIssuer { get; set; } = null!;

        /// <summary>The path being built for the in-flight permutation.</summary>
        public StringBuilder PermutationPath { get; set; } = new();

        /// <summary>The newly-issued related nodes of the in-flight permutation, hashed recursively in order.</summary>
        public List<string> RecursionList { get; set; } = [];

        /// <summary>The index into <see cref="RecursionList"/> of the related node being hashed.</summary>
        public int RecursionIndex { get; set; }

        /// <summary>The frame's computed n-degree hash, set when it completes.</summary>
        public string? ResultHash { get; set; }

        /// <summary>The most recently completed child frame's result, delivered to this frame as it resumes.</summary>
        public (string Hash, IdentifierIssuer Issuer) ChildResult { get; set; }
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
