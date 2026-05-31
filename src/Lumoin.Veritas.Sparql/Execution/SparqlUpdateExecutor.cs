using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
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
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous execution.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">An operation (a graph-management op, or a <c>WITH</c>/<c>USING</c> modify) is not yet executable.</exception>
    public static async Task ExecuteAsync(SparqlUpdateRequest request, MutableSparqlDataset dataset, SparqlExpressionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(context);

        foreach(UpdateOperation operation in request.Operations)
        {
            await ApplyAsync(operation, dataset, context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Applies one update operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyAsync(UpdateOperation operation, MutableSparqlDataset dataset, SparqlExpressionContext context, CancellationToken cancellationToken)
        => operation switch
        {
            InsertDataOperation insert => ApplyDataAsync(insert.Data, dataset, add: true, cancellationToken),
            DeleteDataOperation delete => ApplyDataAsync(delete.Data, dataset, add: false, cancellationToken),
            DeleteWhereOperation deleteWhere => ApplyModifyAsync(deleteWhere.Pattern, insert: null, QuadsToWhere(deleteWhere.Pattern), dataset, context, cancellationToken),
            ModifyOperation modify => ApplyModifyOperationAsync(modify, dataset, context, cancellationToken),
            _ => throw new NotSupportedException($"SPARQL Update operation '{operation.GetType().Name}' is not yet executable.")
        };

    /// <summary>Applies a ground <c>INSERT</c>/<c>DELETE DATA</c> quad block.</summary>
    /// <param name="data">The ground quad block.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="add"><see langword="true"/> to insert, <see langword="false"/> to delete.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyDataAsync(Quads data, MutableSparqlDataset dataset, bool add, CancellationToken cancellationToken)
    {
        return ApplyQuadDeltaAsync(SparqlGraphConstruction.InstantiateQuads(data, GroundSolutions), dataset, add, cancellationToken);
    }

    /// <summary>Applies a general modify operation, rejecting the not-yet-executable <c>WITH</c>/<c>USING</c> forms.</summary>
    /// <param name="modify">The modify operation.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static Task ApplyModifyOperationAsync(ModifyOperation modify, MutableSparqlDataset dataset, SparqlExpressionContext context, CancellationToken cancellationToken)
    {
        if(modify.With is not null || modify.Using.Count > 0)
        {
            throw new NotSupportedException("SPARQL Update WITH / USING clauses are not yet executable.");
        }

        return ApplyModifyAsync(modify.Delete, modify.Insert, modify.Where, dataset, context, cancellationToken);
    }

    /// <summary>Evaluates the <c>WHERE</c> against the current dataset, then applies the delete and insert templates per solution (delete-before-insert).</summary>
    /// <param name="delete">The delete template, or <see langword="null"/>.</param>
    /// <param name="insert">The insert template, or <see langword="null"/>.</param>
    /// <param name="where">The WHERE pattern.</param>
    /// <param name="dataset">The mutable dataset.</param>
    /// <param name="context">The expression context.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>The asynchronous application.</returns>
    private static async Task ApplyModifyAsync(Quads? delete, Quads? insert, GroupGraphPattern where, MutableSparqlDataset dataset, SparqlExpressionContext context, CancellationToken cancellationToken)
    {
        //Evaluate WHERE against the pre-operation snapshot, through the engine's public surface.
        SparqlQueryEngine engine = new(dataset.Snapshot(), dataset.Dictionary, context);
        AlgebraOperator algebra = SparqlTranslator.Translate(WrapAsSelect(where));
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        if(delete is not null)
        {
            await ApplyQuadDeltaAsync(SparqlGraphConstruction.InstantiateQuads(delete, solutions), dataset, add: false, cancellationToken).ConfigureAwait(false);
        }

        if(insert is not null)
        {
            await ApplyQuadDeltaAsync(SparqlGraphConstruction.InstantiateQuads(insert, solutions), dataset, add: true, cancellationToken).ConfigureAwait(false);
        }
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
