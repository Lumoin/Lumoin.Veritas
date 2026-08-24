using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Executes a <see cref="SparqlUpdateRequest"/> against a <see cref="MutableSparqlDataset"/>: the whole request
/// runs inside ONE <see cref="DatasetEditSession"/>, so each operation sees the effects of the earlier ones
/// (SPARQL Update §3.1.3) through the session's working state, and the request commits as ONE atomic
/// <see cref="DatasetJournalEntry"/> — readers never observe a half-applied request, however many graphs it
/// touches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mutation path.</b> Every change stages into the session: triple deltas via
/// <see cref="DatasetEditSession.ApplyDeltaAsync"/>, wholesale replacement via
/// <see cref="DatasetEditSession.ReplaceGraphAsync"/>, structural changes via
/// <see cref="DatasetEditSession.CreateGraphAsync"/> and <see cref="DatasetEditSession.DropGraph"/>. The
/// commit appends the net per-graph transitions under the dataset journal's optimistic-concurrency contract;
/// a concurrent committer surfaces as <see cref="EditSessionConcurrencyException"/> and the caller retries
/// the request against the new state.
/// </para>
/// <para>
/// <b>WHERE semantics.</b> A modify evaluates its <c>WHERE</c> once, against the session's working state as
/// it stands before the operation's own writes, through the public <see cref="SparqlQueryEngine"/>; it then
/// instantiates the <c>DELETE</c> template and removes, then the <c>INSERT</c> template and adds —
/// delete-before-insert. Templates are assumed already normalized (RDF 1.2 sugar lowered).
/// </para>
/// </remarks>
public static class SparqlUpdateExecutor
{
    /// <summary>The single empty solution ground <c>INSERT</c>/<c>DELETE DATA</c> instantiates its quads against.</summary>
    private static IReadOnlyList<SparqlSolution> GroundSolutions { get; } = [new SparqlSolution([])];

    /// <summary>Executes every operation of an update request, in order, inside one dataset edit session, and commits the request atomically.</summary>
    /// <param name="request">The (normalized) update request.</param>
    /// <param name="dataset">The mutable dataset to apply the operations to.</param>
    /// <param name="context">The expression context the modify <c>WHERE</c> evaluation consumes.</param>
    /// <param name="graphSource">The resolver that <c>LOAD</c> fetches a source document's triples through; <see langword="null"/> makes a non-<c>SILENT</c> <c>LOAD</c> raise <see cref="NotSupportedException"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> step inside a modify's <c>WHERE</c> uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy the modify <c>WHERE</c> evaluation consults per candidate triple; <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque access context forwarded to <paramref name="graphSource"/>, <paramref name="serviceClient"/>, and <paramref name="accessControl"/>; <see langword="null"/> when none.</param>
    /// <param name="enginePolicy">The execution-strategy policy the modify <c>WHERE</c> evaluation engine is constructed under; the default keeps the materialising executor.</param>
    /// <param name="updateOptions">The update-semantics options — currently the contextual-assertion <c>LOAD</c> destination policy; the default keeps the SPARQL-specification behaviour everywhere.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous execution.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">A non-<c>SILENT</c> <c>LOAD</c> was reached with no <paramref name="graphSource"/>.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another request committed concurrently; retry against the new state.</exception>
    public static async Task ExecuteAsync(SparqlUpdateRequest request, MutableSparqlDataset dataset, SparqlExpressionContext context, GraphSourceResolver? graphSource = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, SparqlEnginePolicy enginePolicy = default, SparqlUpdateOptions updateOptions = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(context);

        //Acquire and dispose explicitly so the disposal await can
        //carry ConfigureAwait(false); C# does not yet have syntax
        //for that on `await using` declarations.
        DatasetEditSession session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for(int operationScope = 0; operationScope < request.Operations.Count; operationScope++)
            {
                await ApplyAsync(request.Operations[operationScope], session, context, graphSource, serviceClient, accessControl, accessContext, enginePolicy, updateOptions, operationScope, cancellationToken).ConfigureAwait(false);
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Applies one update operation to the session's working state.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="session">The open dataset edit session.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="graphSource">The <c>LOAD</c> source resolver, or <see langword="null"/>.</param>
    /// <param name="serviceClient">The <c>SERVICE</c> transport for modify <c>WHERE</c> evaluation, or <see langword="null"/>.</param>
    /// <param name="accessControl">The access-control policy for modify <c>WHERE</c> evaluation, or <see langword="null"/>.</param>
    /// <param name="accessContext">The opaque access context forwarded to the seams, or <see langword="null"/>.</param>
    /// <param name="enginePolicy">The execution-strategy policy the modify <c>WHERE</c> evaluation engine is constructed under.</param>
    /// <param name="updateOptions">The update-semantics options the <c>LOAD</c> destination consults.</param>
    /// <param name="operationScope">The operation's index, scoping its blank-node instantiation.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyAsync(UpdateOperation operation, DatasetEditSession session, SparqlExpressionContext context, GraphSourceResolver? graphSource, SparqlClient? serviceClient, AccessControlDelegate? accessControl, AccessContext? accessContext, SparqlEnginePolicy enginePolicy, SparqlUpdateOptions updateOptions, int operationScope, CancellationToken cancellationToken)
        => operation switch
        {
            InsertDataOperation insert => ApplyDataAsync(insert.Data, session, add: true, operationScope, cancellationToken),
            DeleteDataOperation delete => ApplyDataAsync(delete.Data, session, add: false, operationScope, cancellationToken),
            DeleteWhereOperation deleteWhere => ApplyModifyAsync(deleteWhere.Pattern, insert: null, QuadsToWhere(deleteWhere.Pattern), withGraph: null, session.Snapshot(), session, context, serviceClient, accessControl, accessContext, enginePolicy, operationScope, cancellationToken),
            ModifyOperation modify => ApplyModifyOperationAsync(modify, session, context, serviceClient, accessControl, accessContext, enginePolicy, operationScope, cancellationToken),
            ClearOperation clear => ClearTargetsAsync(clear.Target, session, dropNamed: false, cancellationToken),
            DropOperation drop => ClearTargetsAsync(drop.Target, session, dropNamed: true, cancellationToken),
            CreateOperation create => ApplyCreateAsync(create, session, cancellationToken),
            AddOperation add => ApplyBinaryAsync(add.Source, add.Destination, BinaryGraphMode.Add, session, cancellationToken),
            CopyOperation copy => ApplyBinaryAsync(copy.Source, copy.Destination, BinaryGraphMode.Copy, session, cancellationToken),
            MoveOperation move => ApplyBinaryAsync(move.Source, move.Destination, BinaryGraphMode.Move, session, cancellationToken),
            LoadOperation load => ApplyLoadAsync(load, session, graphSource, accessContext, updateOptions, cancellationToken),
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
    /// <param name="session">The open session.</param>
    /// <param name="add"><see langword="true"/> to insert, <see langword="false"/> to delete.</param>
    /// <param name="operationScope">The operation's index, scoping its blank-node instantiation.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyDataAsync(Quads data, DatasetEditSession session, bool add, int operationScope, CancellationToken cancellationToken)
    {
        return ApplyQuadDeltaAsync(SparqlGraphConstruction.InstantiateQuads(data, GroundSolutions, operationScope), session, add, cancellationToken);
    }

    /// <summary>Applies a general modify operation, honouring <c>WITH</c> (the default graph for the WHERE and unqualified templates) and <c>USING</c>/<c>USING NAMED</c> (the query dataset for the WHERE).</summary>
    /// <param name="modify">The modify operation.</param>
    /// <param name="session">The open session.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="serviceClient">The <c>SERVICE</c> transport for the WHERE evaluation, or <see langword="null"/>.</param>
    /// <param name="accessControl">The access-control policy for the WHERE evaluation, or <see langword="null"/>.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="enginePolicy">The execution-strategy policy the WHERE evaluation engine is constructed under.</param>
    /// <param name="operationScope">The operation's index, scoping its blank-node instantiation.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyModifyOperationAsync(ModifyOperation modify, DatasetEditSession session, SparqlExpressionContext context, SparqlClient? serviceClient, AccessControlDelegate? accessControl, AccessContext? accessContext, SparqlEnginePolicy enginePolicy, int operationScope, CancellationToken cancellationToken)
    {
        //USING / USING NAMED override the query dataset the WHERE matches; otherwise WITH names the WHERE's default
        //graph; otherwise the WHERE sees the whole working state. WITH also retargets unqualified template triples.
        SparqlDataset whereDataset = await BuildWhereDatasetAsync(modify, session, cancellationToken).ConfigureAwait(false);
        RdfTerm? withGraph = modify.With is IriRef with ? new NamedNode(with.Value) : null;

        await ApplyModifyAsync(modify.Delete, modify.Insert, modify.Where, withGraph, whereDataset, session, context, serviceClient, accessControl, accessContext, enginePolicy, operationScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates the <c>WHERE</c> against <paramref name="whereDataset"/>, then applies the delete and insert templates per solution (delete-before-insert), retargeting unqualified triples to <paramref name="withGraph"/> when present.</summary>
    /// <param name="delete">The delete template, or <see langword="null"/>.</param>
    /// <param name="insert">The insert template, or <see langword="null"/>.</param>
    /// <param name="where">The WHERE pattern.</param>
    /// <param name="withGraph">The <c>WITH</c> graph that unqualified template triples target, or <see langword="null"/>.</param>
    /// <param name="whereDataset">The dataset the WHERE matches against.</param>
    /// <param name="session">The open session the deltas stage into.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="serviceClient">The <c>SERVICE</c> transport for the WHERE evaluation, or <see langword="null"/>.</param>
    /// <param name="accessControl">The access-control policy for the WHERE evaluation, or <see langword="null"/>.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="enginePolicy">The execution-strategy policy the WHERE evaluation engine is constructed under.</param>
    /// <param name="operationScope">The operation's index, scoping its blank-node instantiation.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyModifyAsync(Quads? delete, Quads? insert, GroupGraphPattern where, RdfTerm? withGraph, SparqlDataset whereDataset, DatasetEditSession session, SparqlExpressionContext context, SparqlClient? serviceClient, AccessControlDelegate? accessControl, AccessContext? accessContext, SparqlEnginePolicy enginePolicy, int operationScope, CancellationToken cancellationToken)
    {
        //Evaluate WHERE against the pre-operation working state, through the engine's public surface — the same
        //federation, access-control, and access-context seams a standalone query gets.
        SparqlQueryEngine engine = new(whereDataset, session.Dictionary, context, serviceClient, accessControl, accessContext, enginePolicy: enginePolicy);
        AlgebraOperator algebra = SparqlTranslator.Translate(WrapAsSelect(where), context.ExtensionFunctions.AggregateIris);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        if(delete is not null)
        {
            await ApplyQuadDeltaAsync(Retarget(SparqlGraphConstruction.InstantiateQuads(delete, solutions, operationScope), withGraph), session, add: false, cancellationToken).ConfigureAwait(false);
        }

        if(insert is not null)
        {
            await ApplyQuadDeltaAsync(Retarget(SparqlGraphConstruction.InstantiateQuads(insert, solutions, operationScope), withGraph), session, add: true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Builds the dataset a modify's <c>WHERE</c> matches against: the <c>USING</c> graphs when present, else the <c>WITH</c> graph as the default, else the whole working state.</summary>
    /// <param name="modify">The modify operation.</param>
    /// <param name="session">The open session.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The WHERE dataset.</returns>
    private static async Task<SparqlDataset> BuildWhereDatasetAsync(ModifyOperation modify, DatasetEditSession session, CancellationToken cancellationToken)
    {
        if(modify.Using.Count > 0)
        {
            //USING iri => its graph joins the WHERE default graph; USING NAMED iri => it is a named graph. The
            //merged default is a throwaway read-only store, isolated from the arena on purpose — it dies with
            //the query and never enters the dataset's intern table.
            List<EncodedTriple> defaultTriples = [];
            Dictionary<TermId, HypertrieGraphStore> named = [];
            foreach(UsingClause clause in modify.Using)
            {
                TermId graphId = session.Dictionary.GetOrAdd(new NamedNode(clause.Iri.Value));
                if(!session.TryGetNamedGraph(graphId, out HypertrieGraphStore? store))
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

            return new SparqlDataset(await HypertrieGraphStore.BuildAsync(defaultTriples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false), named);
        }

        if(modify.With is IriRef with)
        {
            TermId graphId = session.Dictionary.GetOrAdd(new NamedNode(with.Value));
            HypertrieGraphStore whereDefault = session.TryGetNamedGraph(graphId, out HypertrieGraphStore? store)
                ? store!
                : await HypertrieGraphStore.BuildAsync([], VeritasHashing.Default, cancellationToken).ConfigureAwait(false);

            return session.SnapshotWithDefault(whereDefault);
        }

        return session.Snapshot();
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

    /// <summary>Encodes the instantiated quads, groups them by target graph, and stages the add/remove for each affected graph into the session.</summary>
    /// <param name="quads">The instantiated quads (each carrying its target graph, or none for the default graph).</param>
    /// <param name="session">The open session.</param>
    /// <param name="add"><see langword="true"/> to add, <see langword="false"/> to remove.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyQuadDeltaAsync(List<Quad> quads, DatasetEditSession session, bool add, CancellationToken cancellationToken)
    {
        if(quads.Count == 0)
        {
            return;
        }

        TermDictionary dictionary = session.Dictionary;
        Dictionary<TermId, List<EncodedTriple>> byGraph = [];
        foreach(Quad quad in quads)
        {
            EncodedTriple encoded = EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd((RdfTerm)quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded);

            TermId graphId = quad.Graph is RdfTerm graph ? dictionary.GetOrAdd(graph) : TermId.None;
            if(!byGraph.TryGetValue(graphId, out List<EncodedTriple>? list))
            {
                list = [];
                byGraph[graphId] = list;
            }

            list.Add(encoded);
        }

        foreach((TermId graphId, List<EncodedTriple> triples) in byGraph)
        {
            //DELETE from an absent graph is a no-op; INSERT into one creates it (§3.1.1) — the session's
            //ApplyDeltaAsync encodes exactly that leniency.
            await session.ApplyDeltaAsync(
                graphId,
                additions: add ? triples : [],
                removals: add ? [] : triples,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clears (<c>dropNamed</c> = <see langword="false"/>) or drops (<see langword="true"/>) the referenced graph(s): <c>DEFAULT</c>, <c>NAMED</c> (all named), <c>ALL</c>, or a specific <c>GRAPH iri</c>.</summary>
    /// <param name="target">The graph reference.</param>
    /// <param name="session">The open session.</param>
    /// <param name="dropNamed"><see langword="true"/> to remove a named graph entirely (<c>DROP</c>); <see langword="false"/> to empty it but keep it (<c>CLEAR</c>).</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ClearTargetsAsync(GraphRefTarget target, DatasetEditSession session, bool dropNamed, CancellationToken cancellationToken)
        => target switch
        {
            GraphRefDefault => session.ReplaceGraphAsync(TermId.None, [], cancellationToken).AsTask(),
            GraphRefNamed => ClearAllNamedAsync(session, dropNamed, cancellationToken),
            GraphRefAll => ClearAllAsync(session, dropNamed, cancellationToken),
            GraphRefIri iri => ClearNamedAsync(session, session.Dictionary.GetOrAdd(new NamedNode(iri.Iri.Value)), dropNamed, cancellationToken),
            _ => Task.CompletedTask
        };

    /// <summary>Clears or drops every named graph.</summary>
    /// <param name="session">The open session.</param>
    /// <param name="dropNamed">Whether to remove the graphs (<c>DROP</c>) rather than empty them (<c>CLEAR</c>).</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearAllNamedAsync(DatasetEditSession session, bool dropNamed, CancellationToken cancellationToken)
    {
        //Snapshot the names: clearing/dropping mutates the working named-graph collection.
        foreach(TermId graphId in new List<TermId>(session.NamedGraphNames))
        {
            await ClearNamedAsync(session, graphId, dropNamed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Clears or drops every graph — the default plus all named (<c>ALL</c>).</summary>
    /// <param name="session">The open session.</param>
    /// <param name="dropNamed">Whether to remove the named graphs rather than empty them.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearAllAsync(DatasetEditSession session, bool dropNamed, CancellationToken cancellationToken)
    {
        await session.ReplaceGraphAsync(TermId.None, [], cancellationToken).ConfigureAwait(false);
        await ClearAllNamedAsync(session, dropNamed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears or drops one named graph; an absent graph is a no-op (lenient — a non-<c>SILENT</c> failure on an absent graph is not modelled).</summary>
    /// <param name="session">The open session.</param>
    /// <param name="graphId">The graph-name term id.</param>
    /// <param name="dropNamed">Whether to remove the graph rather than empty it.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ClearNamedAsync(DatasetEditSession session, TermId graphId, bool dropNamed, CancellationToken cancellationToken)
    {
        if(!session.ContainsNamedGraph(graphId))
        {
            return;
        }

        if(dropNamed)
        {
            session.DropGraph(graphId);

            return;
        }

        await session.ReplaceGraphAsync(graphId, [], cancellationToken).ConfigureAwait(false);
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
    /// <param name="session">The open session.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyCreateAsync(CreateOperation create, DatasetEditSession session, CancellationToken cancellationToken)
    {
        await session.CreateGraphAsync(session.Dictionary.GetOrAdd(new NamedNode(create.Graph.Value)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies a binary graph operation (<c>ADD</c> merge, <c>COPY</c> replace, <c>MOVE</c> replace-then-drop-source) between a source and destination graph.</summary>
    /// <param name="source">The source graph reference (an IRI or the default graph).</param>
    /// <param name="destination">The destination graph reference (an IRI or the default graph).</param>
    /// <param name="mode">The copy semantics.</param>
    /// <param name="session">The open session.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyBinaryAsync(GraphRefTarget source, GraphRefTarget destination, BinaryGraphMode mode, DatasetEditSession session, CancellationToken cancellationToken)
    {
        (bool sourceDefault, TermId sourceId) = ResolveRef(source, session);
        (bool destinationDefault, TermId destinationId) = ResolveRef(destination, session);

        //Source and destination are the same graph: the operation is a no-op (§3.2.2–3.2.4).
        if(sourceDefault == destinationDefault && (sourceDefault || sourceId.Equals(destinationId)))
        {
            return;
        }

        HypertrieGraphStore? sourceStore = sourceDefault
            ? session.DefaultGraph
            : session.TryGetNamedGraph(sourceId, out HypertrieGraphStore? existing) ? existing : null;
        List<EncodedTriple> sourceTriples = sourceStore is null ? [] : CollectAllTriples(sourceStore);

        TermId destinationGraph = destinationDefault ? TermId.None : destinationId;
        if(mode == BinaryGraphMode.Add)
        {
            if(sourceTriples.Count > 0)
            {
                await session.ApplyDeltaAsync(destinationGraph, sourceTriples, [], cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        //COPY / MOVE: the destination becomes exactly the source's triples.
        await session.ReplaceGraphAsync(destinationGraph, sourceTriples, cancellationToken).ConfigureAwait(false);

        if(mode == BinaryGraphMode.Move)
        {
            if(sourceDefault)
            {
                await session.ReplaceGraphAsync(TermId.None, [], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                session.DropGraph(sourceId);
            }
        }
    }

    /// <summary>Resolves a binary-op graph reference to (is-default, named-graph-id).</summary>
    /// <param name="target">The graph reference (a <see cref="GraphRefDefault"/> or <see cref="GraphRefIri"/>).</param>
    /// <param name="session">The open session (for interning the IRI).</param>
    /// <returns>Whether it is the default graph, and the named-graph id (<see cref="TermId.None"/> for the default).</returns>
    private static (bool IsDefault, TermId Id) ResolveRef(GraphRefTarget target, DatasetEditSession session)
        => target switch
        {
            GraphRefIri iri => (false, session.Dictionary.GetOrAdd(new NamedNode(iri.Iri.Value))),
            _ => (true, TermId.None)
        };

    /// <summary>Loads a source document's triples (via the resolver seam) into the default graph, a named graph (<c>LOAD … INTO GRAPH</c>), or — under the contextual-assertion option — a freshly minted blank-node graph whose provenance the default graph records.</summary>
    /// <param name="load">The load operation.</param>
    /// <param name="session">The open session.</param>
    /// <param name="graphSource">The source resolver; <see langword="null"/> makes a non-<c>SILENT</c> load throw.</param>
    /// <param name="accessContext">The opaque access context forwarded to the resolver, or <see langword="null"/>.</param>
    /// <param name="updateOptions">The update-semantics options; <see cref="SparqlUpdateOptions.ContextualAssertionLoad"/> redirects a plain load's destination.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyLoadAsync(LoadOperation load, DatasetEditSession session, GraphSourceResolver? graphSource, AccessContext? accessContext, SparqlUpdateOptions updateOptions, CancellationToken cancellationToken)
    {
        if(graphSource is null)
        {
            if(load.Silent)
            {
                return;
            }

            throw new NotSupportedException("SPARQL Update LOAD requires a graph-source resolver.");
        }

        List<EncodedTriple> encoded = [];
        try
        {
            //Encode during enumeration: the only materialised copy is the compact EncodedTriple list, never the
            //term-bearing source document. Nothing is applied to the session until the stream completes, so the
            //apply below (the unchanged tail) is atomic with respect to a mid-stream failure.
            await foreach(DataTriple triple in graphSource(load.Source, accessContext, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                encoded.Add(EncodedTriple.FromEncoded(
                    session.Dictionary.GetOrAdd(triple.Subject).Encoded,
                    session.Dictionary.GetOrAdd(triple.Predicate).Encoded,
                    session.Dictionary.GetOrAdd(triple.Object).Encoded));
            }
        }
        catch(Exception exception) when(load.Silent && exception is not OperationCanceledException)
        {
            //LOAD SILENT swallows any resolution or mid-stream parse failure (a missing or unparseable source);
            //because nothing is applied until the stream completes, a swallowed failure applies NOTHING (§3.1.5).
            return;
        }

        if(load.Into is IriRef into)
        {
            //LOAD INTO creates the target graph even for an empty source document; an explicit
            //destination always wins, so the contextual-assertion option never redirects it.
            TermId graphId = session.Dictionary.GetOrAdd(new NamedNode(into.Value));
            await session.CreateGraphAsync(graphId, cancellationToken).ConfigureAwait(false);
            await session.ApplyDeltaAsync(graphId, encoded, [], cancellationToken).ConfigureAwait(false);

            return;
        }

        if(updateOptions.ContextualAssertionLoad)
        {
            //The contextual-assertion destination: the imported document lands whole in a freshly
            //minted blank-node graph — imported statements hold in that context, never globally —
            //and the default graph gains ONE provenance triple naming the graph's source document,
            //so the import is discoverable by query. The fresh graph is created even for an empty
            //source document, mirroring the LOAD INTO arm.
            TermId contextGraph = session.Dictionary.GetOrAdd(FreshContextGraph(session.Dictionary));
            await session.CreateGraphAsync(contextGraph, cancellationToken).ConfigureAwait(false);
            await session.ApplyDeltaAsync(contextGraph, encoded, [], cancellationToken).ConfigureAwait(false);

            EncodedTriple provenance = EncodedTriple.FromEncoded(
                contextGraph.Encoded,
                session.Dictionary.GetOrAdd(WasDerivedFrom).Encoded,
                session.Dictionary.GetOrAdd(new NamedNode(load.Source.Value)).Encoded);
            await session.ApplyDeltaAsync(TermId.None, [provenance], [], cancellationToken).ConfigureAwait(false);

            return;
        }

        await session.ApplyDeltaAsync(TermId.None, encoded, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The provenance predicate a contextual-assertion load asserts between the fresh context graph and its source document: PROV-O <c>wasDerivedFrom</c> (<see href="https://www.w3.org/TR/prov-o/#wasDerivedFrom"/>).</summary>
    private static NamedNode WasDerivedFrom { get; } = new(Utf8Strings.From("http://www.w3.org/ns/prov#wasDerivedFrom"));

    /// <summary>The bound on the fresh context-graph label probe — far above any plausible number of contextual loads; exceeding it is an invariant violation, not an expected condition.</summary>
    private const int FreshContextGraphProbeLimit = 1_000_000;

    /// <summary>Mints a blank-node graph name that is fresh for the WHOLE dataset: candidate labels are probed against the term dictionary until one is unseen, so the context graph can never conflate with a blank node already present in a loaded document, an earlier contextual load, or any other dataset term.</summary>
    /// <param name="dictionary">The dataset's term dictionary.</param>
    /// <returns>The fresh blank-node graph name.</returns>
    /// <exception cref="InvalidOperationException">No fresh label was found within the probe bound.</exception>
    private static BlankNode FreshContextGraph(TermDictionary dictionary)
    {
        for(int candidate = 0; candidate < FreshContextGraphProbeLimit; candidate++)
        {
            BlankNode node = new(Utf8Strings.From(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"contextual-load-{candidate}")));
            if(!dictionary.TryGetId(node, out _))
            {
                return node;
            }
        }

        throw new InvalidOperationException("No fresh contextual-load graph label was found within the probe bound.");
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
