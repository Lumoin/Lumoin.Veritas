using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Executes a <see cref="SparqlUpdateRequest"/> against a <see cref="MutableSparqlDataset"/>, applying each operation
/// in order so a later operation sees the effects of the earlier ones (SPARQL Update §3.1.3). Ground
/// <c>INSERT</c>/<c>DELETE DATA</c> and the <c>DELETE</c>/<c>INSERT … WHERE</c> modify forms are executed; the
/// graph-management operations (<c>LOAD</c>/<c>CLEAR</c>/<c>DROP</c>/<c>CREATE</c>/<c>ADD</c>/<c>MOVE</c>/<c>COPY</c>)
/// and <c>WITH</c>/<c>USING</c> are not yet executable and raise <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mutation path.</b> Every change goes through a real <see cref="EditSession"/> on the target graph store:
/// the affected triples are encoded into the dataset's shared dictionary, applied as a batched add/remove, committed
/// to a new snapshot (a journal entry + content-addressed <c>EditCommitment</c>), and the new store swapped into the
/// dataset via <see cref="HypertrieGraphStore.FromSnapshot"/>.
/// </para>
/// <para>
/// <b>WHERE semantics.</b> A modify evaluates its <c>WHERE</c> once, against the dataset as it stands before the
/// operation's own writes, through the public <see cref="SparqlQueryEngine"/> (so a future join-backend swap covers
/// update too); it then instantiates the <c>DELETE</c> template and removes, then the <c>INSERT</c> template and adds
/// — delete-before-insert. Templates are assumed already normalized (RDF 1.2 sugar lowered).
/// </para>
/// </remarks>
public static class SparqlUpdateExecutor
{
    /// <summary>The single empty solution ground <c>INSERT</c>/<c>DELETE DATA</c> instantiates its quads against.</summary>
    private static IReadOnlyList<SparqlSolution> GroundSolutions { get; } = [new SparqlSolution([])];

    /// <summary>Executes every operation of an update request, in order, against the dataset.</summary>
    /// <param name="request">The (normalized) update request.</param>
    /// <param name="dataset">The mutable dataset to apply the operations to.</param>
    /// <param name="context">The expression context the modify <c>WHERE</c> evaluation consumes.</param>
    /// <param name="graphSource">The resolver that <c>LOAD</c> fetches a source document's triples through; <see langword="null"/> makes a non-<c>SILENT</c> <c>LOAD</c> raise <see cref="NotSupportedException"/>.</param>
    /// <param name="accessContext">The opaque access context forwarded to <paramref name="graphSource"/> so a <c>LOAD</c> across a trust boundary can authorize the fetch; <see langword="null"/> when none.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous execution.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">A non-<c>SILENT</c> <c>LOAD</c> was reached with no <paramref name="graphSource"/>.</exception>
    public static async Task ExecuteAsync(SparqlUpdateRequest request, MutableSparqlDataset dataset, SparqlExpressionContext context, GraphSourceResolver? graphSource = null, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(context);

        for(int operationScope = 0; operationScope < request.Operations.Count; operationScope++)
        {
            await ApplyAsync(request.Operations[operationScope], dataset, context, graphSource, accessContext, operationScope, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Applies one update operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="graphSource">The <c>LOAD</c> source resolver, or <see langword="null"/>.</param>
    /// <param name="accessContext">The opaque access context forwarded to <c>LOAD</c>'s resolver, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyAsync(UpdateOperation operation, MutableSparqlDataset dataset, SparqlExpressionContext context, GraphSourceResolver? graphSource, AccessContext? accessContext, int operationScope, CancellationToken cancellationToken)
        => operation switch
        {
            InsertDataOperation insert => ApplyDataAsync(insert.Data, dataset, add: true, operationScope, cancellationToken),
            DeleteDataOperation delete => ApplyDataAsync(delete.Data, dataset, add: false, operationScope, cancellationToken),
            DeleteWhereOperation deleteWhere => ApplyModifyAsync(deleteWhere.Pattern, insert: null, QuadsToWhere(deleteWhere.Pattern), withGraph: null, dataset.Snapshot(), dataset, context, operationScope, cancellationToken),
            ModifyOperation modify => ApplyModifyOperationAsync(modify, dataset, context, operationScope, cancellationToken),
            ClearOperation clear => ClearTargetsAsync(clear.Target, dataset, dropNamed: false, cancellationToken),
            DropOperation drop => ClearTargetsAsync(drop.Target, dataset, dropNamed: true, cancellationToken),
            CreateOperation create => ApplyCreateAsync(create, dataset, cancellationToken),
            AddOperation add => ApplyBinaryAsync(add.Source, add.Destination, BinaryGraphMode.Add, dataset, cancellationToken),
            CopyOperation copy => ApplyBinaryAsync(copy.Source, copy.Destination, BinaryGraphMode.Copy, dataset, cancellationToken),
            MoveOperation move => ApplyBinaryAsync(move.Source, move.Destination, BinaryGraphMode.Move, dataset, cancellationToken),
            LoadOperation load => ApplyLoadAsync(load, dataset, graphSource, accessContext, cancellationToken),
            _ => throw new NotSupportedException($"SPARQL Update operation '{operation.GetType().Name}' is not yet executable.")
        };

    /// <summary>The whole-graph copy semantics of the binary graph operations.</summary>
    private enum BinaryGraphMode
    {
        /// <summary><c>ADD</c>: merge the source into the destination, keeping both.</summary>
        Add,

        /// <summary><c>COPY</c>: replace the destination with the source, keeping the source.</summary>
        Copy,

        /// <summary><c>MOVE</c>: replace the destination with the source, then drop the source.</summary>
        Move
    }

    /// <summary>Applies a ground <c>INSERT</c>/<c>DELETE DATA</c> quad block.</summary>
    /// <param name="data">The ground quad block.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="add"><see langword="true"/> to insert, <see langword="false"/> to delete.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyDataAsync(Quads data, MutableSparqlDataset dataset, bool add, int operationScope, CancellationToken cancellationToken)
    {
        return ApplyQuadDeltaAsync(SparqlGraphConstruction.InstantiateQuads(data, GroundSolutions, operationScope), dataset, add, cancellationToken);
    }

    /// <summary>Applies a general modify operation, honouring <c>WITH</c> (the default graph for the WHERE and unqualified templates) and <c>USING</c>/<c>USING NAMED</c> (the query dataset for the WHERE).</summary>
    /// <param name="modify">The modify operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyModifyOperationAsync(ModifyOperation modify, MutableSparqlDataset dataset, SparqlExpressionContext context, int operationScope, CancellationToken cancellationToken)
    {
        //USING / USING NAMED override the query dataset the WHERE matches; otherwise WITH names the WHERE's default
        //graph; otherwise the WHERE sees the whole current dataset. WITH also retargets unqualified template triples.
        SparqlDataset whereDataset = await BuildWhereDatasetAsync(modify, dataset, cancellationToken).ConfigureAwait(false);
        RdfTerm? withGraph = modify.With is IriRef with ? new NamedNode(with.Value) : null;

        await ApplyModifyAsync(modify.Delete, modify.Insert, modify.Where, withGraph, whereDataset, dataset, context, operationScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates the <c>WHERE</c> against <paramref name="whereDataset"/>, then applies the delete and insert templates per solution (delete-before-insert), retargeting unqualified triples to <paramref name="withGraph"/> when present.</summary>
    /// <param name="delete">The delete template, or <see langword="null"/>.</param>
    /// <param name="insert">The insert template, or <see langword="null"/>.</param>
    /// <param name="where">The WHERE pattern.</param>
    /// <param name="withGraph">The <c>WITH</c> graph that unqualified template triples target, or <see langword="null"/>.</param>
    /// <param name="whereDataset">The dataset the WHERE matches against.</param>
    /// <param name="dataset">The mutable dataset the deltas are applied to.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyModifyAsync(Quads? delete, Quads? insert, GroupGraphPattern where, RdfTerm? withGraph, SparqlDataset whereDataset, MutableSparqlDataset dataset, SparqlExpressionContext context, int operationScope, CancellationToken cancellationToken)
    {
        //Evaluate WHERE against the pre-operation snapshot, through the engine's public surface.
        SparqlQueryEngine engine = new(whereDataset, dataset.Dictionary, context);
        AlgebraOperator algebra = SparqlTranslator.Translate(WrapAsSelect(where));
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        if(delete is not null)
        {
            await ApplyQuadDeltaAsync(Retarget(SparqlGraphConstruction.InstantiateQuads(delete, solutions, operationScope), withGraph), dataset, add: false, cancellationToken).ConfigureAwait(false);
        }

        if(insert is not null)
        {
            await ApplyQuadDeltaAsync(Retarget(SparqlGraphConstruction.InstantiateQuads(insert, solutions, operationScope), withGraph), dataset, add: true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Builds the dataset a modify's <c>WHERE</c> matches against: the <c>USING</c> graphs when present, else the <c>WITH</c> graph as the default, else the whole current dataset.</summary>
    /// <param name="modify">The modify operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The WHERE dataset.</returns>
    private static async Task<SparqlDataset> BuildWhereDatasetAsync(ModifyOperation modify, MutableSparqlDataset dataset, CancellationToken cancellationToken)
    {
        if(modify.Using.Count > 0)
        {
            //USING iri => its graph joins the WHERE default graph; USING NAMED iri => it is a named graph.
            List<EncodedTriple> defaultTriples = [];
            Dictionary<TermId, HypertrieGraphStore> named = [];
            foreach(UsingClause clause in modify.Using)
            {
                TermId graphId = dataset.Dictionary.GetOrAdd(new NamedNode(clause.Iri.Value));
                if(!dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? store))
                {
                    continue;
                }

                if(clause.IsNamed)
                {
                    named[graphId] = store!;
                }
                else
                {
                    defaultTriples.AddRange(CollectAllTriples(store!));
                }
            }

            return new SparqlDataset(await MutableSparqlDataset.BuildGraphAsync(defaultTriples, cancellationToken).ConfigureAwait(false), named);
        }

        if(modify.With is IriRef with)
        {
            TermId graphId = dataset.Dictionary.GetOrAdd(new NamedNode(with.Value));
            HypertrieGraphStore whereDefault = dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? store)
                ? store!
                : await MutableSparqlDataset.BuildGraphAsync([], cancellationToken).ConfigureAwait(false);

            return dataset.SnapshotWithDefault(whereDefault);
        }

        return dataset.Snapshot();
    }

    /// <summary>Retargets every default-graph (graph-less) quad to <paramref name="graph"/>; a <see langword="null"/> graph leaves the quads unchanged (the <c>WITH</c>-less case).</summary>
    /// <param name="quads">The instantiated quads.</param>
    /// <param name="graph">The target graph for graph-less quads, or <see langword="null"/>.</param>
    /// <returns>The retargeted quads (the same list, mutated in place when a graph is given).</returns>
    private static List<Quad> Retarget(List<Quad> quads, RdfTerm? graph)
    {
        if(graph is null)
        {
            return quads;
        }

        for(int i = 0; i < quads.Count; i++)
        {
            if(quads[i].Graph is null)
            {
                quads[i] = quads[i] with { Graph = graph };
            }
        }

        return quads;
    }

    /// <summary>Encodes the instantiated quads, groups them by target graph, and applies the add/remove to each affected graph through an edit session.</summary>
    /// <param name="quads">The instantiated quads (each carrying its target graph, or none for the default graph).</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="add"><see langword="true"/> to add, <see langword="false"/> to remove.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyQuadDeltaAsync(List<Quad> quads, MutableSparqlDataset dataset, bool add, CancellationToken cancellationToken)
    {
        if(quads.Count == 0)
        {
            return;
        }

        TermDictionary dictionary = dataset.Dictionary;
        List<EncodedTriple> defaultTriples = [];
        Dictionary<TermId, List<EncodedTriple>> namedTriples = [];
        foreach(Quad quad in quads)
        {
            EncodedTriple encoded = EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd((RdfTerm)quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded);

            if(quad.Graph is not RdfTerm graph)
            {
                defaultTriples.Add(encoded);

                continue;
            }

            TermId graphId = dictionary.GetOrAdd(graph);
            if(!namedTriples.TryGetValue(graphId, out List<EncodedTriple>? list))
            {
                list = [];
                namedTriples[graphId] = list;
            }

            list.Add(encoded);
        }

        if(defaultTriples.Count > 0)
        {
            dataset.SetDefaultGraph(await ApplyToStoreAsync(dataset.DefaultGraph, defaultTriples, add, cancellationToken).ConfigureAwait(false));
        }

        foreach((TermId graphId, List<EncodedTriple> triples) in namedTriples)
        {
            HypertrieGraphStore? store = dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? existing) ? existing : null;
            if(store is null)
            {
                //DELETE from a graph that does not exist is a no-op; INSERT DATA into one creates it (§3.1.1).
                if(!add)
                {
                    continue;
                }

                store = await MutableSparqlDataset.BuildGraphAsync([], cancellationToken).ConfigureAwait(false);
            }

            dataset.SetNamedGraph(graphId, await ApplyToStoreAsync(store, triples, add, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>Applies a batched add or remove to one graph store through an edit session, returning the committed store.</summary>
    /// <param name="store">The graph store to edit.</param>
    /// <param name="triples">The triples to add or remove.</param>
    /// <param name="add"><see langword="true"/> to add, <see langword="false"/> to remove.</param>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    /// <returns>The store over the committed snapshot.</returns>
    private static async Task<HypertrieGraphStore> ApplyToStoreAsync(HypertrieGraphStore store, IReadOnlyList<EncodedTriple> triples, bool add, CancellationToken cancellationToken)
    {
        EditSession session = await store.OpenEditSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if(add)
            {
                session.AddRange(triples);
            }
            else
            {
                session.RemoveRange(triples);
            }

            using HypertrieSnapshot committed = await session.CommitAsync(cancellationToken).ConfigureAwait(false);

            return HypertrieGraphStore.FromSnapshot(committed);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Clears (<c>dropNamed</c> = <see langword="false"/>) or drops (<see langword="true"/>) the referenced graph(s): <c>DEFAULT</c>, <c>NAMED</c> (all named), <c>ALL</c>, or a specific <c>GRAPH iri</c>.</summary>
    /// <param name="target">The graph reference.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="dropNamed"><see langword="true"/> to remove a named graph entirely (<c>DROP</c>); <see langword="false"/> to empty it but keep it (<c>CLEAR</c>).</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ClearTargetsAsync(GraphRefTarget target, MutableSparqlDataset dataset, bool dropNamed, CancellationToken cancellationToken)
        => target switch
        {
            GraphRefDefault => ClearDefaultAsync(dataset, cancellationToken),
            GraphRefNamed => ClearAllNamedAsync(dataset, dropNamed, cancellationToken),
            GraphRefAll => ClearAllAsync(dataset, dropNamed, cancellationToken),
            GraphRefIri iri => ClearNamedAsync(dataset, dataset.Dictionary.GetOrAdd(new NamedNode(iri.Iri.Value)), dropNamed, cancellationToken),
            _ => Task.CompletedTask
        };

    /// <summary>Empties the default graph (the default graph always exists, so <c>DROP DEFAULT</c> and <c>CLEAR DEFAULT</c> both empty it).</summary>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearDefaultAsync(MutableSparqlDataset dataset, CancellationToken cancellationToken)
    {
        dataset.SetDefaultGraph(await EmptiedAsync(dataset.DefaultGraph, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Clears or drops every named graph.</summary>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="dropNamed">Whether to remove the graphs (<c>DROP</c>) rather than empty them (<c>CLEAR</c>).</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearAllNamedAsync(MutableSparqlDataset dataset, bool dropNamed, CancellationToken cancellationToken)
    {
        //Snapshot the names: clearing/dropping mutates the named-graph collection.
        foreach(TermId graphId in new List<TermId>(dataset.NamedGraphNames))
        {
            await ClearNamedAsync(dataset, graphId, dropNamed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clears or drops every graph — the default plus all named (<c>ALL</c>).</summary>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="dropNamed">Whether to remove the named graphs rather than empty them.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearAllAsync(MutableSparqlDataset dataset, bool dropNamed, CancellationToken cancellationToken)
    {
        await ClearDefaultAsync(dataset, cancellationToken).ConfigureAwait(false);
        await ClearAllNamedAsync(dataset, dropNamed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears or drops one named graph; an absent graph is a no-op (lenient — a non-<c>SILENT</c> failure on an absent graph is not modelled).</summary>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="graphId">The graph-name term id.</param>
    /// <param name="dropNamed">Whether to remove the graph rather than empty it.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearNamedAsync(MutableSparqlDataset dataset, TermId graphId, bool dropNamed, CancellationToken cancellationToken)
    {
        if(!dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? store))
        {
            return;
        }

        if(dropNamed)
        {
            dataset.RemoveNamedGraph(graphId);

            return;
        }

        dataset.SetNamedGraph(graphId, await EmptiedAsync(store!, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Returns an emptied copy of a store (removing all its triples through an edit session), or the store itself when already empty.</summary>
    /// <param name="store">The store to empty.</param>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    /// <returns>The emptied store.</returns>
    private static async Task<HypertrieGraphStore> EmptiedAsync(HypertrieGraphStore store, CancellationToken cancellationToken)
    {
        List<EncodedTriple> all = CollectAllTriples(store);

        return all.Count == 0 ? store : await ApplyToStoreAsync(store, all, add: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads all of a store's encoded triples.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The store's encoded triples.</returns>
    private static List<EncodedTriple> CollectAllTriples(HypertrieGraphStore store)
    {
        List<EncodedTriple> triples = [];
        foreach(EncodedTriple triple in store.Match(TermId.None, TermId.None, TermId.None))
        {
            triples.Add(triple);
        }

        return triples;
    }

    /// <summary>Creates an empty named graph (<c>CREATE GRAPH iri</c>); an already-existing graph is left unchanged.</summary>
    /// <param name="create">The create operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyCreateAsync(CreateOperation create, MutableSparqlDataset dataset, CancellationToken cancellationToken)
    {
        TermId graphId = dataset.Dictionary.GetOrAdd(new NamedNode(create.Graph.Value));
        if(!dataset.ContainsNamedGraph(graphId))
        {
            dataset.SetNamedGraph(graphId, await MutableSparqlDataset.BuildGraphAsync([], cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>Applies a binary graph operation (<c>ADD</c> merge, <c>COPY</c> replace, <c>MOVE</c> replace-then-drop-source) between a source and destination graph.</summary>
    /// <param name="source">The source graph reference (an IRI or the default graph).</param>
    /// <param name="destination">The destination graph reference (an IRI or the default graph).</param>
    /// <param name="mode">The copy semantics.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyBinaryAsync(GraphRefTarget source, GraphRefTarget destination, BinaryGraphMode mode, MutableSparqlDataset dataset, CancellationToken cancellationToken)
    {
        (bool sourceDefault, TermId sourceId) = ResolveRef(source, dataset);
        (bool destinationDefault, TermId destinationId) = ResolveRef(destination, dataset);

        //Source and destination are the same graph: the operation is a no-op (§3.2.2–3.2.4).
        if(sourceDefault == destinationDefault && (sourceDefault || sourceId.Equals(destinationId)))
        {
            return;
        }

        HypertrieGraphStore? sourceStore = sourceDefault
            ? dataset.DefaultGraph
            : dataset.TryGetNamedGraph(sourceId, out HypertrieGraphStore? existing) ? existing : null;
        List<EncodedTriple> sourceTriples = sourceStore is null ? [] : CollectAllTriples(sourceStore);

        if(mode == BinaryGraphMode.Add)
        {
            await MergeIntoAsync(dataset, destinationDefault, destinationId, sourceTriples, cancellationToken).ConfigureAwait(false);

            return;
        }

        //COPY / MOVE: the destination becomes exactly the source's triples.
        HypertrieGraphStore replacement = await MutableSparqlDataset.BuildGraphAsync(sourceTriples, cancellationToken).ConfigureAwait(false);
        if(destinationDefault)
        {
            dataset.SetDefaultGraph(replacement);
        }
        else
        {
            dataset.SetNamedGraph(destinationId, replacement);
        }

        if(mode == BinaryGraphMode.Move)
        {
            if(sourceDefault)
            {
                dataset.SetDefaultGraph(await EmptiedAsync(dataset.DefaultGraph, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                dataset.RemoveNamedGraph(sourceId);
            }
        }
    }

    /// <summary>Merges triples into a destination graph (<c>ADD</c>), creating the named destination when it does not exist.</summary>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="isDefault">Whether the destination is the default graph.</param>
    /// <param name="graphId">The destination named-graph id (ignored when <paramref name="isDefault"/>).</param>
    /// <param name="triples">The triples to merge.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task MergeIntoAsync(MutableSparqlDataset dataset, bool isDefault, TermId graphId, List<EncodedTriple> triples, CancellationToken cancellationToken)
    {
        if(triples.Count == 0)
        {
            return;
        }

        if(isDefault)
        {
            dataset.SetDefaultGraph(await ApplyToStoreAsync(dataset.DefaultGraph, triples, add: true, cancellationToken).ConfigureAwait(false));

            return;
        }

        HypertrieGraphStore store = dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? existing)
            ? existing!
            : await MutableSparqlDataset.BuildGraphAsync([], cancellationToken).ConfigureAwait(false);
        dataset.SetNamedGraph(graphId, await ApplyToStoreAsync(store, triples, add: true, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Resolves a binary-op graph reference to (is-default, named-graph-id).</summary>
    /// <param name="target">The graph reference (a <see cref="GraphRefDefault"/> or <see cref="GraphRefIri"/>).</param>
    /// <param name="dataset">The mutable dataset (for interning the IRI).</param>
    /// <returns>Whether it is the default graph, and the named-graph id (<see cref="TermId.None"/> for the default).</returns>
    private static (bool IsDefault, TermId Id) ResolveRef(GraphRefTarget target, MutableSparqlDataset dataset)
        => target switch
        {
            GraphRefIri iri => (false, dataset.Dictionary.GetOrAdd(new NamedNode(iri.Iri.Value))),
            _ => (true, TermId.None)
        };

    /// <summary>Loads a source document's triples (via the resolver seam) into the default graph or a named graph (<c>LOAD … INTO GRAPH</c>).</summary>
    /// <param name="load">The load operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="graphSource">The source resolver; <see langword="null"/> makes a non-<c>SILENT</c> load throw.</param>
    /// <param name="accessContext">The opaque access context forwarded to the resolver, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyLoadAsync(LoadOperation load, MutableSparqlDataset dataset, GraphSourceResolver? graphSource, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        if(graphSource is null)
        {
            if(load.Silent)
            {
                return;
            }

            throw new NotSupportedException("SPARQL Update LOAD requires a graph-source resolver.");
        }

        IReadOnlyList<DataTriple> triples;
        try
        {
            triples = await graphSource(load.Source, accessContext, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception exception) when(load.Silent && exception is not OperationCanceledException)
        {
            //LOAD SILENT swallows any resolution failure (a missing or unparseable source) per §3.1.5.
            return;
        }

        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dataset.Dictionary.GetOrAdd(triple.Subject).Encoded,
                dataset.Dictionary.GetOrAdd(triple.Predicate).Encoded,
                dataset.Dictionary.GetOrAdd(triple.Object).Encoded));
        }

        if(load.Into is IriRef into)
        {
            TermId graphId = dataset.Dictionary.GetOrAdd(new NamedNode(into.Value));
            HypertrieGraphStore store = dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? existing)
                ? existing!
                : await MutableSparqlDataset.BuildGraphAsync([], cancellationToken).ConfigureAwait(false);
            dataset.SetNamedGraph(graphId, await ApplyToStoreAsync(store, encoded, add: true, cancellationToken).ConfigureAwait(false));

            return;
        }

        dataset.SetDefaultGraph(await ApplyToStoreAsync(dataset.DefaultGraph, encoded, add: true, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Wraps a group graph pattern as a <c>SELECT *</c> query so the existing translator/engine produce its solutions.</summary>
    /// <param name="where">The WHERE pattern.</param>
    /// <returns>The synthesized query.</returns>
    private static SparqlQuery WrapAsSelect(GroupGraphPattern where)
    {
        return new SparqlQuery(
            where.Span,
            new Prologue(where.Span, [], [], []),
            new SelectQuery(where.Span, IsDistinct: false, IsReduced: false, IsStar: true, []),
            new DatasetClause(where.Span, [], []),
            new WhereClause(where.Span, where),
            new SolutionModifier(where.Span, null, null, null, null, null),
            Values: null);
    }

    /// <summary>Lifts a quad pattern to a group graph pattern (the <c>WHERE</c> of a <c>DELETE WHERE</c>): default triples become a basic block, each <c>GRAPH</c> group a graph pattern.</summary>
    /// <param name="quads">The quad pattern.</param>
    /// <returns>The group graph pattern.</returns>
    private static GroupGraphPattern QuadsToWhere(Quads quads)
    {
        List<GraphPattern> members = [];
        if(quads.DefaultTriples.Count > 0)
        {
            members.Add(new BasicGraphPatternBlock(quads.Span, quads.DefaultTriples, []));
        }

        foreach(QuadsGraphGroup group in quads.GraphGroups)
        {
            GroupGraphPattern inner = new(group.Span, [new BasicGraphPatternBlock(group.Span, group.Triples, [])]);
            members.Add(new GraphGraphPattern(group.Span, group.Graph, inner));
        }

        return new GroupGraphPattern(quads.Span, members);
    }
}
