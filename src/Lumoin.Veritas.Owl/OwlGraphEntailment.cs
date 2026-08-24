using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// How the entailment check reads the comprehension conditions of the
/// OWL 2 RDF-Based Semantics — the informative appendix stating that every
/// well-formed class-expression node exists in every interpretation.
/// </summary>
public enum OwlComprehension
{
    /// <summary>The normative semantics only: a conclusion's expression structure must exist in the closure to embed.</summary>
    None = 0,

    /// <summary>
    /// The informative comprehension conditions are granted: a conclusion
    /// blank node whose every occurrence is well-formed class-expression
    /// scaffolding is satisfied by construction at check time, and the
    /// entailment path additionally mints the contentful scaffolds' grammar
    /// structure into its reasoned-over premise and fires the closure's
    /// comprehension completion family over it.
    /// </summary>
    InformativeConditions = 1,
}

/// <summary>
/// Graph-level entailment checking over a closure: whether a conclusion
/// graph embeds into a closure graph by subgraph homomorphism — blank
/// nodes bind existentially and consistently, named nodes and literals
/// match by value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Comprehension as a check-time mode, not closure content.</b> The
/// comprehension conditions are an infinite schema (every conceivable
/// expression node exists), so no materializer can carry them; a checker
/// holding a finite conclusion can grant them exactly. Under
/// <see cref="OwlComprehension.InformativeConditions"/> the conclusion is
/// first stripped of its pure-existence scaffolds: maximal blank-rooted
/// subgraphs that are well-formed class expressions (restrictions with
/// their property and value triples, boolean classes with their RDF
/// lists, nested expressions included) that form a finite acyclic
/// structure — list spines nil-terminated, no scaffold node reachable
/// from itself, shared sub-expressions allowed — and whose nodes occur
/// nowhere else in the conclusion. A blank with any occurrence outside
/// its scaffold is
/// contentful — its binding must be a real closure node — and embeds
/// normally, scaffold triples included.
/// </para>
/// <para>
/// The mode is named for what it claims: the conditions are informative
/// in the OWL 2 RDF-Based Semantics, so granting them goes beyond the
/// normative entailments. <see cref="OwlComprehension.None"/> is the
/// normative reading.
/// </para>
/// </remarks>
public static class OwlGraphEntailment
{
    /// <summary>
    /// Whether <paramref name="conclusion"/> embeds into
    /// <paramref name="closure"/>.
    /// </summary>
    /// <param name="conclusion">The graph to embed.</param>
    /// <param name="closure">The graph embedded into.</param>
    /// <param name="comprehension">How to read the comprehension conditions.</param>
    /// <returns><see langword="true"/> when an embedding exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool Embeds(IReadOnlyList<Quad> conclusion, IReadOnlyList<Quad> closure, OwlComprehension comprehension = OwlComprehension.None)
    {
        return TryEmbed(conclusion, closure, comprehension, out _);
    }

    /// <summary>
    /// Whether <paramref name="conclusion"/> embeds into
    /// <paramref name="closure"/>, reporting the unembedded remainder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern splits into ground triples and connected blank
    /// components — triples sharing a blank label, transitively. The parts
    /// bind independently, because only constants cross the split, so the
    /// remainder is exact and mapping-independent: every ground triple
    /// absent from the closure, plus every triple of each component with
    /// no consistent joint binding. An embedding exists exactly when the
    /// remainder is empty. Matching compares subject, predicate, and
    /// object; the graph position does not participate.
    /// </para>
    /// </remarks>
    /// <param name="conclusion">The graph to embed.</param>
    /// <param name="closure">The graph embedded into — under the comprehension mode also the forced-standing source: a scaffold strips only when its named arguments hold class or property standing in this graph.</param>
    /// <param name="comprehension">How to read the comprehension conditions.</param>
    /// <param name="unembedded">The triples that cannot embed, in pattern order over the comprehension-stripped conclusion; empty exactly when the embedding exists.</param>
    /// <returns><see langword="true"/> when an embedding exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool TryEmbed(IReadOnlyList<Quad> conclusion, IReadOnlyList<Quad> closure, OwlComprehension comprehension, out IReadOnlyList<Quad> unembedded)
    {
        ArgumentNullException.ThrowIfNull(conclusion);
        ArgumentNullException.ThrowIfNull(closure);

        IReadOnlyList<Quad> stripped = comprehension == OwlComprehension.InformativeConditions
            ? WithoutComprehensionScaffolds(conclusion, closure)
            : conclusion;
        List<Quad> pattern = stripped as List<Quad> ?? [.. stripped];

        int[] componentOf = AssignComponents(pattern);

        HashSet<(RdfTerm Subject, NamedNode Predicate, RdfTerm Object)> present = [];
        foreach(Quad data in closure)
        {
            present.Add((data.Subject, data.Predicate, data.Object));
        }

        Dictionary<int, List<Quad>> components = [];
        for(int i = 0; i < pattern.Count; i++)
        {
            if(componentOf[i] < 0)
            {
                continue;
            }

            if(!components.TryGetValue(componentOf[i], out List<Quad>? triples))
            {
                triples = [];
                components[componentOf[i]] = triples;
            }

            triples.Add(pattern[i]);
        }

        //A component's satisfiability is the same backtracking question the
        //whole-pattern search asked, over a smaller space.
        HashSet<int> failed = [];
        foreach(KeyValuePair<int, List<Quad>> component in components)
        {
            if(!EmbedsCore(component.Value, closure))
            {
                failed.Add(component.Key);
            }
        }

        List<Quad> remainder = [];
        for(int i = 0; i < pattern.Count; i++)
        {
            if(componentOf[i] < 0)
            {
                if(!present.Contains((pattern[i].Subject, pattern[i].Predicate, pattern[i].Object)))
                {
                    remainder.Add(pattern[i]);
                }

                continue;
            }

            if(failed.Contains(componentOf[i]))
            {
                remainder.Add(pattern[i]);
            }
        }

        unembedded = remainder;

        return remainder.Count == 0;
    }

    /// <summary>
    /// Assigns each pattern triple its blank-connectivity component:
    /// <c>-1</c> for a ground triple, otherwise an id shared by every
    /// triple reachable through common blank labels.
    /// </summary>
    /// <param name="pattern">The pattern triples.</param>
    /// <returns>The component id per pattern index.</returns>
    private static int[] AssignComponents(List<Quad> pattern)
    {
        int[] parent = new int[pattern.Count];
        for(int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        Dictionary<Utf8String, int> anchorOf = [];
        bool[] hasBlank = new bool[pattern.Count];
        for(int i = 0; i < pattern.Count; i++)
        {
            //The predicate is a named node by construction, and the graph
            //position does not participate in matching.
            Union(parent, hasBlank, anchorOf, pattern[i].Subject, i);
            Union(parent, hasBlank, anchorOf, pattern[i].Object, i);
        }

        int[] componentOf = new int[pattern.Count];
        for(int i = 0; i < pattern.Count; i++)
        {
            componentOf[i] = hasBlank[i] ? Find(parent, i) : -1;
        }

        return componentOf;
    }

    /// <summary>
    /// Joins the triple at <paramref name="index"/> to the component
    /// anchored by <paramref name="term"/>'s blank label, when the term is
    /// a blank node.
    /// </summary>
    /// <param name="parent">The union-find parent array over pattern indexes.</param>
    /// <param name="hasBlank">Marks per index whether the triple carries a blank.</param>
    /// <param name="anchorOf">The first pattern index seen per blank label.</param>
    /// <param name="term">The term at a matching position of the triple.</param>
    /// <param name="index">The triple's pattern index.</param>
    private static void Union(int[] parent, bool[] hasBlank, Dictionary<Utf8String, int> anchorOf, RdfTerm term, int index)
    {
        if(term is not BlankNode blank)
        {
            return;
        }

        hasBlank[index] = true;
        if(anchorOf.TryGetValue(blank.Label, out int anchor))
        {
            int left = Find(parent, anchor);
            int right = Find(parent, index);
            if(left != right)
            {
                parent[right] = left;
            }

            return;
        }

        anchorOf[blank.Label] = index;
    }

    /// <summary>Finds the union-find root of <paramref name="index"/>, compressing the walked path.</summary>
    /// <param name="parent">The union-find parent array.</param>
    /// <param name="index">The index resolved.</param>
    /// <returns>The root index.</returns>
    private static int Find(int[] parent, int index)
    {
        int root = index;
        while(parent[root] != root)
        {
            root = parent[root];
        }

        while(parent[index] != root)
        {
            int next = parent[index];
            parent[index] = root;
            index = next;
        }

        return root;
    }

    /// <summary>
    /// Whether <paramref name="conclusion"/> embeds into the store — the
    /// same question as <see cref="Embeds"/>, answered by the join engine:
    /// a conclusion graph with existential blanks is a basic graph pattern
    /// with variables, and an embedding exists exactly when the pattern
    /// has a solution. The first solution short-circuits, the ASK
    /// semantics.
    /// </summary>
    /// <remarks>
    /// A constant term the dictionary has never seen cannot match and
    /// answers <see langword="false"/> immediately. A conclusion triple
    /// using one blank in two of its own positions is a per-pattern
    /// self-join the trie driver rejects; such conclusions fall back to
    /// the list-scan embedding over the decoded store — correct, and rare
    /// enough that the fallback is the design.
    /// </remarks>
    /// <param name="conclusion">The graph to embed.</param>
    /// <param name="store">The store embedded into — typically the materialized closure.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <param name="timeProvider">Clock for the join engine's trace timestamps. Pass <see cref="TimeProvider.System"/> in production.</param>
    /// <param name="comprehension">How to read the comprehension conditions.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when an embedding exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<bool> EmbedsAsync(
        IReadOnlyList<Quad> conclusion,
        HypertrieGraphStore store,
        TermDictionary dictionary,
        TimeProvider timeProvider,
        OwlComprehension comprehension = OwlComprehension.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conclusion);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(timeProvider);

        //The store decodes whole to serve as the forced-standing source —
        //the DecodeStore precedent; the targeted-probe alternative is the
        //production stand's benchmark candidate.
        IReadOnlyList<Quad> pattern = comprehension == OwlComprehension.InformativeConditions
            ? WithoutComprehensionScaffolds(conclusion, DecodeStore(store, dictionary))
            : conclusion;

        if(pattern.Count == 0)
        {
            return true;
        }

        VariableRegistry registry = new();
        List<TriplePattern> patterns = [];
        bool selfJoin = false;
        foreach(Quad quad in pattern)
        {
            if(!TryMapPosition(quad.Subject, registry, dictionary, out PatternPosition subject)
                || !TryMapPosition(quad.Predicate, registry, dictionary, out PatternPosition predicate)
                || !TryMapPosition(quad.Object, registry, dictionary, out PatternPosition @object))
            {
                //A constant the store's dictionary never minted matches
                //nothing.
                return false;
            }

            selfJoin |= SharesAVariable(subject, predicate) || SharesAVariable(subject, @object) || SharesAVariable(predicate, @object);
            patterns.Add(new TriplePattern(subject, predicate, @object));
        }

        if(selfJoin)
        {
            return Embeds(pattern, DecodeStore(store, dictionary), OwlComprehension.None);
        }

        BasicGraphPattern query = new(patterns, registry);
        await foreach(Solution _ in store.QueryAsync(query, timeProvider, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    /// <summary>Maps one conclusion term to a pattern position: blanks become variables, constants encode through the dictionary.</summary>
    /// <param name="term">The conclusion term.</param>
    /// <param name="registry">The variable registry blanks register in.</param>
    /// <param name="dictionary">The store's term dictionary.</param>
    /// <param name="position">The mapped position.</param>
    /// <returns><see langword="false"/> when a constant is absent from the dictionary and the pattern cannot match.</returns>
    private static bool TryMapPosition(RdfTerm term, VariableRegistry registry, TermDictionary dictionary, out PatternPosition position)
    {
        if(term is BlankNode blank)
        {
            position = PatternPosition.OfVariable(registry.GetOrAdd(blank.Label.ToString()));

            return true;
        }

        TermId id = dictionary.GetIdOrDefault(term);
        position = PatternPosition.Bound(id);

        return id != TermId.None;
    }

    /// <summary>Whether two positions carry the same variable — the per-pattern self-join the trie driver rejects.</summary>
    /// <param name="left">The first position.</param>
    /// <param name="right">The second position.</param>
    /// <returns><see langword="true"/> when both are the same variable.</returns>
    private static bool SharesAVariable(PatternPosition left, PatternPosition right)
    {
        return left.IsVariable && right.IsVariable && left.Variable == right.Variable;
    }

    /// <summary>Decodes the store back to quads for the list-scan fallback.</summary>
    /// <param name="store">The store to decode.</param>
    /// <param name="dictionary">The store's term dictionary.</param>
    /// <returns>The decoded graph.</returns>
    private static List<Quad> DecodeStore(HypertrieGraphStore store, TermDictionary dictionary)
    {
        List<Quad> quads = [];
        foreach(EncodedTriple triple in store.Match(TermId.None, TermId.None, TermId.None))
        {
            if(dictionary.Resolve(triple.Predicate) is NamedNode predicate)
            {
                quads.Add(new Quad(dictionary.Resolve(triple.Subject), predicate, dictionary.Resolve(triple.Object), Graph: null));
            }
        }

        return quads;
    }

    /// <summary>The role a blank node plays inside a candidate scaffold walk; the cell roles carry the member obligation of the constructor that spawned the list.</summary>
    private enum ScaffoldRole
    {
        /// <summary>A class-expression root: a restriction or boolean class node.</summary>
        Expression = 0,

        /// <summary>A list cell of a union or intersection: members are class expressions, so a named member must be class-forced.</summary>
        ClassListCell = 1,

        /// <summary>A list cell of an enumeration: members are individuals, named or literal, and need no standing; a blank member is contentful.</summary>
        IndividualListCell = 2,
    }

    /// <summary>
    /// Strips the conclusion's pure-existence scaffolds: for each blank
    /// rooting a well-formed class expression whose nodes occur nowhere
    /// outside the scaffold and whose named arguments the standing source
    /// forces into class or property standing, the scaffold's triples
    /// drop — the comprehension conditions grant existence only over
    /// standing-bearing arguments. Anything malformed, contentful, or
    /// unforced stays a proof obligation.
    /// </summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <param name="standingSource">The graph whose triples force standing — the closure or the decoded store the conclusion embeds into.</param>
    /// <returns>The conclusion without its granted scaffolds.</returns>
    private static IReadOnlyList<Quad> WithoutComprehensionScaffolds(IReadOnlyList<Quad> conclusion, IReadOnlyList<Quad> standingSource)
    {
        OwlComprehensionScaffolds.ForcedStanding forced = OwlComprehensionScaffolds.CollectForcedStanding(standingSource);

        //Index blank-subject triples for walking and all blank occurrences
        //for the nothing-else-mentions-it check.
        Dictionary<Utf8String, List<Quad>> bySubject = [];
        Dictionary<Utf8String, List<Quad>> occurrences = [];
        foreach(Quad quad in conclusion)
        {
            if(quad.Subject is BlankNode subject)
            {
                Append(bySubject, subject.Label, quad);
                Append(occurrences, subject.Label, quad);
            }

            if(quad.Object is BlankNode @object)
            {
                Append(occurrences, @object.Label, quad);
            }
        }

        HashSet<Quad> dropped = [];
        foreach(KeyValuePair<Utf8String, List<Quad>> candidate in bySubject)
        {
            if(IsExpressionRoot(candidate.Value) && TryCollectScaffold(candidate.Key, bySubject, occurrences, forced, out HashSet<Quad>? scaffold))
            {
                foreach(Quad quad in scaffold)
                {
                    dropped.Add(quad);
                }
            }
        }

        if(dropped.Count == 0)
        {
            return conclusion;
        }

        List<Quad> remaining = [];
        foreach(Quad quad in conclusion)
        {
            if(!dropped.Contains(quad))
            {
                remaining.Add(quad);
            }
        }

        return remaining;
    }

    /// <summary>Whether the node's subject triples carry an expression constructor — a boolean-class predicate or a restriction's <c>owl:onProperty</c>.</summary>
    /// <param name="subjectTriples">The node's subject triples.</param>
    /// <returns><see langword="true"/> for a candidate expression root.</returns>
    private static bool IsExpressionRoot(List<Quad> subjectTriples)
    {
        foreach(Quad quad in subjectTriples)
        {
            Utf8String predicate = quad.Predicate.Iri;
            if(predicate.Equals(OwlVocabulary.UnionOf)
                || predicate.Equals(OwlVocabulary.IntersectionOf)
                || predicate.Equals(OwlVocabulary.OneOf)
                || predicate.Equals(OwlVocabulary.ComplementOf)
                || predicate.Equals(OwlVocabulary.OnProperty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks a candidate scaffold from its root: every reached blank must
    /// be a well-formed expression node or list cell, every triple of a
    /// reached node must belong to the scaffold grammar, the reachability
    /// among reached nodes must be acyclic — list spines nil-terminated and
    /// no node reachable from itself, shared sub-expressions allowed — and no
    /// reached node may occur anywhere outside the collected triples.
    /// </summary>
    /// <param name="root">The root blank's label.</param>
    /// <param name="bySubject">Blank-subject triple index.</param>
    /// <param name="occurrences">All-position blank occurrence index.</param>
    /// <param name="forced">The standing source's forced class and property standing.</param>
    /// <param name="scaffold">The collected scaffold triples when valid.</param>
    /// <returns><see langword="true"/> when the scaffold is pure, well-formed, and standing-forced.</returns>
    private static bool TryCollectScaffold(
        Utf8String root,
        Dictionary<Utf8String, List<Quad>> bySubject,
        Dictionary<Utf8String, List<Quad>> occurrences,
        OwlComprehensionScaffolds.ForcedStanding forced,
        [NotNullWhen(true)] out HashSet<Quad>? scaffold)
    {
        scaffold = null;
        HashSet<Quad> collected = [];
        Dictionary<Utf8String, ScaffoldRole> roles = [];
        List<(Utf8String From, Utf8String To)> edges = [];
        Stack<(Utf8String Label, ScaffoldRole Role)> work = new();
        work.Push((root, ScaffoldRole.Expression));

        while(work.Count > 0)
        {
            (Utf8String label, ScaffoldRole role) = work.Pop();
            if(roles.TryGetValue(label, out ScaffoldRole existing))
            {
                if(existing != role)
                {
                    return false;
                }

                continue;
            }

            roles[label] = role;

            if(!bySubject.TryGetValue(label, out List<Quad>? triples))
            {
                return false;
            }

            bool valid = role switch
            {
                ScaffoldRole.Expression => CollectExpressionNode(label, triples, forced, collected, work, edges),
                ScaffoldRole.ClassListCell => CollectListCell(label, triples, role, forced, collected, work, edges),
                ScaffoldRole.IndividualListCell => CollectListCell(label, triples, role, forced, collected, work, edges),
                _ => false
            };

            if(!valid)
            {
                return false;
            }
        }

        //Acyclicity: the comprehension conditions license only finite
        //structures, so a scaffold node reachable from itself — a cyclic
        //list spine or expression nesting — is never granted. Shared
        //sub-expressions reached more than once are a DAG, not a cycle.
        if(!ScaffoldIsAcyclic(roles, edges))
        {
            return false;
        }

        //Purity: every occurrence of every scaffold node — subject or
        //object position — must be one of the collected triples; a single
        //contentful mention makes the blank a real binding obligation.
        foreach(Utf8String label in roles.Keys)
        {
            if(occurrences.TryGetValue(label, out List<Quad>? mentions))
            {
                foreach(Quad mention in mentions)
                {
                    if(!collected.Contains(mention))
                    {
                        return false;
                    }
                }
            }
        }

        scaffold = collected;

        return true;
    }

    /// <summary>
    /// Whether the recorded reachability among a scaffold's nodes is
    /// acyclic, by an iterative Kahn topological sort: every node with no
    /// remaining in-edge is retired in turn, and a node reachable from
    /// itself never loses its last in-edge, so a remaining node witnesses a
    /// cycle. A node reached from two parents — a shared sub-expression — is
    /// a DAG join, retired once both parents are, not a cycle.
    /// </summary>
    /// <param name="nodes">The collected scaffold nodes keyed by label.</param>
    /// <param name="edges">The recorded reachability edges among those nodes.</param>
    /// <returns><see langword="true"/> when the reachability is acyclic.</returns>
    private static bool ScaffoldIsAcyclic(Dictionary<Utf8String, ScaffoldRole> nodes, List<(Utf8String From, Utf8String To)> edges)
    {
        Dictionary<Utf8String, int> inDegree = new(nodes.Count);
        Dictionary<Utf8String, List<Utf8String>> outgoing = new(nodes.Count);
        foreach(Utf8String node in nodes.Keys)
        {
            inDegree[node] = 0;
            outgoing[node] = [];
        }

        foreach((Utf8String from, Utf8String to) in edges)
        {
            outgoing[from].Add(to);
            inDegree[to] += 1;
        }

        Stack<Utf8String> ready = new();
        foreach(KeyValuePair<Utf8String, int> entry in inDegree)
        {
            if(entry.Value == 0)
            {
                ready.Push(entry.Key);
            }
        }

        int retired = 0;
        while(ready.Count > 0)
        {
            Utf8String node = ready.Pop();
            retired++;
            foreach(Utf8String next in outgoing[node])
            {
                int remaining = inDegree[next] - 1;
                inDegree[next] = remaining;
                if(remaining == 0)
                {
                    ready.Push(next);
                }
            }
        }

        return retired == nodes.Count;
    }

    /// <summary>
    /// Collects an expression node's triples: an optional class typing and
    /// exactly one constructor application — the same grant discipline the
    /// minting side enforces, counted category for category and gated by
    /// the shared <see cref="OwlComprehensionScaffolds.GrantsExactlyOneConstructorApplication"/>.
    /// Operand lists, fillers, and nested expressions push onto the walk
    /// with a reachability edge recorded for the acyclicity check; any
    /// second constructor form, duplicate detail, or unrecognized
    /// predicate refuses the strip, so the scaffold stays a proof
    /// obligation.
    /// </summary>
    /// <param name="label">This node's blank label, the source of the recorded edges.</param>
    /// <param name="triples">The node's subject triples.</param>
    /// <param name="forced">The standing source's forced standing: named fillers must be class-forced and the restriction property property-forced, or the strip refuses.</param>
    /// <param name="collectedToAppendTo">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edgesToAppendTo">The reachability edge sink, one <c>(from, to)</c> per pushed neighbour.</param>
    /// <returns><see langword="true"/> when the node states exactly one constructor application over standing-forced arguments.</returns>
    private static bool CollectExpressionNode(Utf8String label, List<Quad> triples, OwlComprehensionScaffolds.ForcedStanding forced, HashSet<Quad> collectedToAppendTo, Stack<(Utf8String Label, ScaffoldRole Role)> work, List<(Utf8String From, Utf8String To)> edgesToAppendTo)
    {
        int booleanConstructors = 0;
        int onPropertyCount = 0;
        int primaryConstraints = 0;
        int qualifierClasses = 0;
        bool qualifiedPrimary = false;

        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;

            if(predicate.Equals(Vocabulary.Rdf.Type))
            {
                if(quad.Object is not NamedNode typing
                    || (!typing.Iri.Equals(OwlVocabulary.ClassTerm) && !typing.Iri.Equals(OwlVocabulary.Restriction)))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.UnionOf) || predicate.Equals(OwlVocabulary.IntersectionOf))
            {
                booleanConstructors++;
                if(!PushListOrNil(label, quad.Object, ScaffoldRole.ClassListCell, work, edgesToAppendTo))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OneOf))
            {
                booleanConstructors++;
                if(!PushListOrNil(label, quad.Object, ScaffoldRole.IndividualListCell, work, edgesToAppendTo))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.ComplementOf)
                || predicate.Equals(OwlVocabulary.SomeValuesFrom)
                || predicate.Equals(OwlVocabulary.AllValuesFrom)
                || predicate.Equals(OwlVocabulary.OnClass)
                || predicate.Equals(OwlVocabulary.OnDataRange))
            {
                if(predicate.Equals(OwlVocabulary.ComplementOf))
                {
                    booleanConstructors++;
                }
                else if(predicate.Equals(OwlVocabulary.OnClass) || predicate.Equals(OwlVocabulary.OnDataRange))
                {
                    qualifierClasses++;
                }
                else
                {
                    primaryConstraints++;
                }

                //A named filler is a leaf granted only under forced class
                //standing; a blank one is a nested expression the walk
                //validates in turn.
                if(quad.Object is BlankNode nested)
                {
                    work.Push((nested.Label, ScaffoldRole.Expression));
                    edgesToAppendTo.Add((label, nested.Label));
                }
                else if(quad.Object is not NamedNode named || !OwlComprehensionScaffolds.IsClassForced(forced, named))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OnProperty))
            {
                onPropertyCount++;
                if(quad.Object is not NamedNode property || !forced.Properties.Contains(property.Iri))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.MinCardinality)
                || predicate.Equals(OwlVocabulary.MaxCardinality)
                || predicate.Equals(OwlVocabulary.Cardinality))
            {
                primaryConstraints++;
                if(quad.Object is not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.MinQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.MaxQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.QualifiedCardinality))
            {
                primaryConstraints++;
                qualifiedPrimary = true;
                if(quad.Object is not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.HasValue) || predicate.Equals(OwlVocabulary.HasSelf))
            {
                //A blank hasValue object is an anonymous individual —
                //contentful, never scaffolding.
                primaryConstraints++;
                if(quad.Object is BlankNode)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            collectedToAppendTo.Add(quad);
        }

        return OwlComprehensionScaffolds.GrantsExactlyOneConstructorApplication(booleanConstructors, onPropertyCount, primaryConstraints, qualifierClasses, qualifiedPrimary);
    }

    /// <summary>
    /// Collects a list cell's triples: an optional <c>rdf:List</c>
    /// typing, exactly one <c>rdf:first</c> (a leaf or a nested
    /// expression), and exactly one <c>rdf:rest</c> (the next cell or
    /// <c>rdf:nil</c>), with a reachability edge from this cell to each
    /// pushed neighbour recorded for the acyclicity check.
    /// </summary>
    /// <param name="label">This cell's blank label, the source of the recorded edges.</param>
    /// <param name="triples">The cell's subject triples.</param>
    /// <param name="role">The cell's role, carrying the member obligation of the constructor that spawned the list.</param>
    /// <param name="forced">The standing source's forced standing for class-list members.</param>
    /// <param name="collected">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edges">The reachability edge sink, one <c>(from, to)</c> per pushed neighbour.</param>
    /// <returns><see langword="true"/> when the cell is well-formed and its member obeys the role.</returns>
    private static bool CollectListCell(Utf8String label, List<Quad> triples, ScaffoldRole role, OwlComprehensionScaffolds.ForcedStanding forced, HashSet<Quad> collected, Stack<(Utf8String Label, ScaffoldRole Role)> work, List<(Utf8String From, Utf8String To)> edges)
    {
        int firstCount = 0;
        int restCount = 0;

        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;

            if(predicate.Equals(Vocabulary.Rdf.Type))
            {
                if(quad.Object is not NamedNode typing || !typing.Iri.Equals(RdfVocabulary.Rdf.List))
                {
                    return false;
                }
            }
            else if(predicate.Equals(RdfVocabulary.Rdf.First))
            {
                firstCount++;
                if(quad.Object is BlankNode nested)
                {
                    //A blank enumeration member is an anonymous
                    //individual — contentful, never scaffolding; a blank
                    //class member is a nested expression.
                    if(role == ScaffoldRole.IndividualListCell)
                    {
                        return false;
                    }

                    work.Push((nested.Label, ScaffoldRole.Expression));
                    edges.Add((label, nested.Label));
                }
                else if(role == ScaffoldRole.ClassListCell)
                {
                    if(quad.Object is not NamedNode member || !OwlComprehensionScaffolds.IsClassForced(forced, member))
                    {
                        return false;
                    }
                }
                else if(quad.Object is not NamedNode and not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(RdfVocabulary.Rdf.Rest))
            {
                restCount++;
                if(!PushListOrNil(label, quad.Object, role, work, edges))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            collected.Add(quad);
        }

        return firstCount == 1 && restCount == 1;
    }

    /// <summary>Pushes a list continuation onto the walk: a blank cell continues under the spawning constructor's role and records a reachability edge from its predecessor, <c>rdf:nil</c> terminates, anything else malforms.</summary>
    /// <param name="from">The predecessor node's blank label, the source of the recorded edge.</param>
    /// <param name="object">The list-position object term.</param>
    /// <param name="role">The cell role the continuation inherits.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edges">The reachability edge sink.</param>
    /// <returns><see langword="true"/> when the term is a valid list continuation.</returns>
    private static bool PushListOrNil(Utf8String from, RdfTerm @object, ScaffoldRole role, Stack<(Utf8String Label, ScaffoldRole Role)> work, List<(Utf8String From, Utf8String To)> edges)
    {
        if(@object is BlankNode cell)
        {
            work.Push((cell.Label, role));
            edges.Add((from, cell.Label));

            return true;
        }

        return @object is NamedNode named && named.Iri.Equals(RdfVocabulary.Rdf.Nil);
    }

    /// <summary>Appends a quad to a label's index list, creating it on first contact.</summary>
    /// <param name="index">The index.</param>
    /// <param name="label">The blank label.</param>
    /// <param name="quad">The quad to record.</param>
    private static void Append(Dictionary<Utf8String, List<Quad>> index, Utf8String label, Quad quad)
    {
        if(!index.TryGetValue(label, out List<Quad>? list))
        {
            list = [];
            index[label] = list;
        }

        list.Add(quad);
    }

    /// <summary>
    /// The embedding search: iterative backtracking over an explicit
    /// frame stack, binding pattern blanks existentially and
    /// consistently.
    /// </summary>
    /// <param name="pattern">The graph to embed.</param>
    /// <param name="data">The graph embedded into.</param>
    /// <returns><see langword="true"/> when an embedding exists.</returns>
    private static bool EmbedsCore(List<Quad> pattern, IReadOnlyList<Quad> data)
    {
        if(pattern.Count == 0)
        {
            return true;
        }

        Dictionary<Utf8String, RdfTerm> bindings = [];
        List<List<Utf8String>> boundAt = [];
        int[] candidate = new int[pattern.Count];
        int level = 0;

        while(true)
        {
            if(level == pattern.Count)
            {
                return true;
            }

            if(boundAt.Count == level)
            {
                boundAt.Add([]);
            }

            bool advanced = false;
            for(int i = candidate[level]; i < data.Count; i++)
            {
                List<Utf8String> newlyBound = boundAt[level];
                if(Matches(pattern[level], data[i], bindings, newlyBound))
                {
                    candidate[level] = i + 1;
                    level++;
                    advanced = true;

                    break;
                }
            }

            if(advanced)
            {
                continue;
            }

            //Exhausted this level: undo its bindings and backtrack.
            candidate[level] = 0;
            level--;
            if(level < 0)
            {
                return false;
            }

            foreach(Utf8String label in boundAt[level])
            {
                bindings.Remove(label);
            }

            boundAt[level].Clear();
        }
    }

    /// <summary>
    /// Tries to match one pattern triple against one data triple under
    /// the current bindings, recording newly bound labels so the caller
    /// can undo them on backtrack.
    /// </summary>
    /// <param name="pattern">The pattern triple.</param>
    /// <param name="data">The data triple.</param>
    /// <param name="bindings">The current blank bindings.</param>
    /// <param name="newlyBound">The labels this match bound.</param>
    /// <returns><see langword="true"/> when the triple matches.</returns>
    private static bool Matches(Quad pattern, Quad data, Dictionary<Utf8String, RdfTerm> bindings, List<Utf8String> newlyBound)
    {
        int rollback = newlyBound.Count;

        if(MatchTerm(pattern.Subject, data.Subject, bindings, newlyBound)
            && MatchTerm(pattern.Predicate, data.Predicate, bindings, newlyBound)
            && MatchTerm(pattern.Object, data.Object, bindings, newlyBound))
        {
            return true;
        }

        for(int i = newlyBound.Count - 1; i >= rollback; i--)
        {
            bindings.Remove(newlyBound[i]);
            newlyBound.RemoveAt(i);
        }

        return false;
    }

    /// <summary>Matches one term: blanks bind existentially and consistently, named nodes and literals by value.</summary>
    /// <param name="pattern">The pattern term.</param>
    /// <param name="data">The data term.</param>
    /// <param name="bindings">The current blank bindings.</param>
    /// <param name="newlyBound">The labels this match bound.</param>
    /// <returns><see langword="true"/> when the term matches.</returns>
    private static bool MatchTerm(RdfTerm pattern, RdfTerm data, Dictionary<Utf8String, RdfTerm> bindings, List<Utf8String> newlyBound)
    {
        if(pattern is BlankNode blank)
        {
            if(bindings.TryGetValue(blank.Label, out RdfTerm? bound))
            {
                return bound.Equals(data);
            }

            bindings[blank.Label] = data;
            newlyBound.Add(blank.Label);

            return true;
        }

        return pattern.Equals(data);
    }
}
