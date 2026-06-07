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
    /// The informative comprehension conditions grant pure-existence
    /// structure: a conclusion blank node whose every occurrence is
    /// well-formed class-expression scaffolding is satisfied by
    /// construction, without the closure materialising it.
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
/// lists, nested expressions included) whose nodes occur nowhere else in
/// the conclusion. A blank with any occurrence outside its scaffold is
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
        ArgumentNullException.ThrowIfNull(conclusion);
        ArgumentNullException.ThrowIfNull(closure);

        IReadOnlyList<Quad> pattern = comprehension == OwlComprehension.InformativeConditions
            ? WithoutComprehensionScaffolds(conclusion)
            : conclusion;

        return EmbedsCore(pattern, closure);
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

        IReadOnlyList<Quad> pattern = comprehension == OwlComprehension.InformativeConditions
            ? WithoutComprehensionScaffolds(conclusion)
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
        await foreach(Solution _ in store.Query(query, timeProvider, cancellationToken: cancellationToken).ConfigureAwait(false))
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

    /// <summary>The role a blank node plays inside a candidate scaffold walk.</summary>
    private enum ScaffoldRole
    {
        /// <summary>A class-expression root: a restriction or boolean class node.</summary>
        Expression = 0,

        /// <summary>An RDF list cell of a boolean class's operand list.</summary>
        ListCell = 1,
    }

    /// <summary>
    /// Strips the conclusion's pure-existence scaffolds: for each blank
    /// rooting a well-formed class expression whose nodes occur nowhere
    /// outside the scaffold, the scaffold's triples drop — comprehension
    /// grants their existence. Anything malformed or contentful stays.
    /// </summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <returns>The conclusion without its granted scaffolds.</returns>
    private static IReadOnlyList<Quad> WithoutComprehensionScaffolds(IReadOnlyList<Quad> conclusion)
    {
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
            if(IsExpressionRoot(candidate.Value) && TryCollectScaffold(candidate.Key, bySubject, occurrences, out HashSet<Quad>? scaffold))
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
    /// reached node must belong to the scaffold grammar, and no reached
    /// node may occur anywhere outside the collected triples.
    /// </summary>
    /// <param name="root">The root blank's label.</param>
    /// <param name="bySubject">Blank-subject triple index.</param>
    /// <param name="occurrences">All-position blank occurrence index.</param>
    /// <param name="scaffold">The collected scaffold triples when valid.</param>
    /// <returns><see langword="true"/> when the scaffold is pure and well-formed.</returns>
    private static bool TryCollectScaffold(
        Utf8String root,
        Dictionary<Utf8String, List<Quad>> bySubject,
        Dictionary<Utf8String, List<Quad>> occurrences,
        [NotNullWhen(true)] out HashSet<Quad>? scaffold)
    {
        scaffold = null;
        HashSet<Quad> collected = [];
        Dictionary<Utf8String, ScaffoldRole> roles = [];
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
                ScaffoldRole.Expression => CollectExpressionNode(triples, collected, work),
                ScaffoldRole.ListCell => CollectListCell(triples, collected, work),
                _ => false
            };

            if(!valid)
            {
                return false;
            }
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
    /// Collects an expression node's triples: an optional class typing,
    /// exactly the constructor and detail predicates the expression
    /// grammar allows, with operand lists, fillers, and nested
    /// expressions pushed onto the walk.
    /// </summary>
    /// <param name="triples">The node's subject triples.</param>
    /// <param name="collected">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <returns><see langword="true"/> when the node is a well-formed expression.</returns>
    private static bool CollectExpressionNode(List<Quad> triples, HashSet<Quad> collected, Stack<(Utf8String Label, ScaffoldRole Role)> work)
    {
        bool hasBooleanConstructor = false;
        bool hasOnProperty = false;
        bool hasRestrictionValue = false;

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
            else if(predicate.Equals(OwlVocabulary.UnionOf) || predicate.Equals(OwlVocabulary.IntersectionOf) || predicate.Equals(OwlVocabulary.OneOf))
            {
                hasBooleanConstructor = true;
                if(!PushListOrNil(quad.Object, work))
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
                hasRestrictionValue |= !predicate.Equals(OwlVocabulary.ComplementOf);
                hasBooleanConstructor |= predicate.Equals(OwlVocabulary.ComplementOf);

                //A named filler is a leaf; a blank one is a nested
                //expression the walk validates in turn.
                if(quad.Object is BlankNode nested)
                {
                    work.Push((nested.Label, ScaffoldRole.Expression));
                }
                else if(quad.Object is not NamedNode)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OnProperty) || predicate.Equals(OwlVocabulary.OnDatatype))
            {
                hasOnProperty |= predicate.Equals(OwlVocabulary.OnProperty);
                if(quad.Object is not NamedNode)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.MinCardinality)
                || predicate.Equals(OwlVocabulary.MaxCardinality)
                || predicate.Equals(OwlVocabulary.Cardinality)
                || predicate.Equals(OwlVocabulary.MinQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.MaxQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.QualifiedCardinality))
            {
                hasRestrictionValue = true;
                if(quad.Object is not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.HasValue) || predicate.Equals(OwlVocabulary.HasSelf))
            {
                //A blank hasValue object is an anonymous individual —
                //contentful, never scaffolding.
                hasRestrictionValue = true;
                if(quad.Object is BlankNode)
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

        return hasBooleanConstructor ? !hasOnProperty : hasOnProperty && hasRestrictionValue;
    }

    /// <summary>
    /// Collects a list cell's triples: an optional <c>rdf:List</c>
    /// typing, exactly one <c>rdf:first</c> (a leaf or a nested
    /// expression), and exactly one <c>rdf:rest</c> (the next cell or
    /// <c>rdf:nil</c>).
    /// </summary>
    /// <param name="triples">The cell's subject triples.</param>
    /// <param name="collected">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <returns><see langword="true"/> when the cell is well-formed.</returns>
    private static bool CollectListCell(List<Quad> triples, HashSet<Quad> collected, Stack<(Utf8String Label, ScaffoldRole Role)> work)
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
                    work.Push((nested.Label, ScaffoldRole.Expression));
                }
                else if(quad.Object is not NamedNode and not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(RdfVocabulary.Rdf.Rest))
            {
                restCount++;
                if(!PushListOrNil(quad.Object, work))
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

    /// <summary>Pushes a list continuation onto the walk: a blank cell continues, <c>rdf:nil</c> terminates, anything else malforms.</summary>
    /// <param name="object">The list-position object term.</param>
    /// <param name="work">The walk stack.</param>
    /// <returns><see langword="true"/> when the term is a valid list continuation.</returns>
    private static bool PushListOrNil(RdfTerm @object, Stack<(Utf8String Label, ScaffoldRole Role)> work)
    {
        if(@object is BlankNode cell)
        {
            work.Push((cell.Label, ScaffoldRole.ListCell));

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
    private static bool EmbedsCore(IReadOnlyList<Quad> pattern, IReadOnlyList<Quad> data)
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
