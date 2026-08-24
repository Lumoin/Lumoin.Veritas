using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;
using EncodedTriplePattern = Lumoin.Veritas.Core.Hypertrie.Query.TriplePattern;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming BGP leaf: pulls backend solutions row by row through the same machinery the materialising
/// evaluator drains — the batched column source when eligible (default graph, no per-solution rewrites, no type
/// expansion; still drained batch-by-batch internally, handing out rows), else the per-row source with the
/// self-join equality filter, cross-variant deduplication, and triple-term destructuring applied per pull, and
/// the <c>rdf:type</c> expansion ladder walked variant by variant. Encoding and the expansion plan are computed
/// once at construction; sources open lazily on first pull. Access-controlled queries stream on the per-row
/// source, which consults the policy per candidate.
/// </summary>
internal sealed class BgpCursor : SolutionCursor
{
    private readonly BgpMachinery machinery;

    private readonly TermId activeGraph;

    private readonly BgpMachinery.EncodedBgp encoded;

    private readonly List<(int PatternIndex, List<TermId> Alternatives)> typeExpansions;

    private readonly BasicGraphPattern? baseQuery;

    private bool opened;

    private HypertrieGraphStore? graphStore;

    private QueryEngineRendezvous? rendezvous;

    private readonly HashSet<string>? seenAcrossVariants;

    private int[]? expansionCursors;

    private bool baseVariantOpened;

    private bool variantsExhausted;

    private IEnumerator<SolutionBatch>? batchSource;

    private SolutionBatch? currentBatch;

    private int batchRow;

    private IReadOnlyList<Variable>? batchProjectionSchema;

    private SparqlVariable?[]? batchProjection;

    private SparqlSolution? current;

    private bool disposed;

    private readonly BgpSeedPlan? seedPlan;

    private List<EncodedTriplePattern>? seededPatterns;

    private bool seededImpossible;

    /// <summary>Constructs the cursor over a BGP leaf, encoding the pattern and computing the expansion plan once; no source opens until the first pull.</summary>
    /// <param name="machinery">The shared BGP machinery.</param>
    /// <param name="bgp">The BGP leaf.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    public BgpCursor(BgpMachinery machinery, Bgp bgp, TermId activeGraph)
        : this(machinery, machinery.EncodeBgp(bgp), activeGraph, seedPlan: null)
    {
    }

    /// <summary>Constructs the cursor over an already-encoded skeleton — the compile-once <c>EXISTS</c> configuration; a non-null <paramref name="seedPlan"/> arms per-binding seeding through <see cref="ResetAsync"/>.</summary>
    /// <param name="machinery">The shared BGP machinery.</param>
    /// <param name="encoded">The encoding skeleton (computed once per site).</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="seedPlan">The seeding plan, or <see langword="null"/> for the unseeded configuration.</param>
    public BgpCursor(BgpMachinery machinery, BgpMachinery.EncodedBgp encoded, TermId activeGraph, BgpSeedPlan? seedPlan)
    {
        this.machinery = machinery;
        this.activeGraph = activeGraph;
        this.encoded = encoded;
        this.seedPlan = seedPlan;
        typeExpansions = seedPlan?.SkeletonExpansions ?? (encoded.Encodable ? machinery.ComputeTypeExpansions(encoded.Patterns) : []);
        baseQuery = encoded.Encodable ? new BasicGraphPattern(encoded.Patterns, encoded.Registry) : null;
        seenAcrossVariants = typeExpansions.Count > 0 ? [] : null;
    }

    /// <summary>The per-row backend enumerator of the current variant, or <see langword="null"/> when the batched source (or no source) is active.</summary>
    private IAsyncEnumerator<Solution>? RowSource { get; set; }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(disposed || variantsExhausted || !encoded.Encodable || seededImpossible)
        {
            return false;
        }

        if(!opened)
        {
            Open();
        }

        while(true)
        {
            if(batchSource is null && RowSource is null)
            {
                if(!TryAdvanceVariant())
                {
                    variantsExhausted = true;

                    return false;
                }

                OpenVariant(cancellationToken);
            }

            if(batchSource is not null)
            {
                if(currentBatch is null || batchRow >= currentBatch.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(batchSource.MoveNext())
                    {
                        currentBatch = batchSource.Current;
                        batchRow = 0;

                        continue;
                    }

                    CloseBatchSource();

                    continue;
                }

                current = DecodeBatchRow(currentBatch, batchRow);
                batchRow++;
                RowsProduced++;

                return true;
            }

            if(await RowSource!.MoveNextAsync().ConfigureAwait(false))
            {
                Solution solution = RowSource.Current;
                if(encoded.SelfJoinEqualities.Count > 0 && !BgpMachinery.SelfJoinHolds(solution, encoded.SelfJoinEqualities))
                {
                    continue;
                }

                if(seenAcrossVariants is not null && !seenAcrossVariants.Add(BgpMachinery.SolutionKey(solution)))
                {
                    continue;
                }

                if(encoded.TripleTermMatches.Count > 0)
                {
                    //Destructure each variable-bearing triple-term position: resolve the matched triple-term value
                    //and unify its components against the solution. A non-triple-term value or a component mismatch
                    //drops the solution.
                    List<VariableBinding> bindings = [.. solution.Bindings];
                    if(!machinery.TryApplyTripleTermMatches(encoded.TripleTermMatches, bindings))
                    {
                        continue;
                    }

                    current = machinery.DecodeBindings(bindings, encoded.ToSparql);
                }
                else
                {
                    current = machinery.DecodeStreamedSolution(solution, encoded.ToSparql, keep: null);
                }

                RowsProduced++;

                return true;
            }

            await CloseRowSourceAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        //Reset owns the disposal of the current binding's live source before re-arming (the cursor contract);
        //the encoding, expansion plan, and resolved store/rendezvous are binding-independent and retained. The
        //unseeded configuration ignores the pre-binding — the compatibility filter above applies it.
        CloseBatchSource();
        await CloseRowSourceAsync().ConfigureAwait(false);
        seenAcrossVariants?.Clear();
        expansionCursors = null;
        baseVariantOpened = false;
        variantsExhausted = false;
        current = null;
        RowsProduced = 0;

        //The seeded configuration patches the binding's bound seed variables onto the skeleton: an absent
        //seed term decides the binding false without opening a source; a rewrite-set-diff decline (the
        //SEM-1 carve-out) probes unseeded for this binding — the caller's compatibility check applies
        //either way, so seeding only ever narrows the scan, never the answer.
        seededPatterns = null;
        seededImpossible = false;
        if(seedPlan is not null && seedPlan.TryPatch(preBinding, machinery, out List<EncodedTriplePattern>? patched, out bool impossible))
        {
            seededPatterns = patched;
            seededImpossible = impossible;
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if(disposed)
        {
            return;
        }

        disposed = true;
        CloseBatchSource();
        await CloseRowSourceAsync().ConfigureAwait(false);
        current = null;
    }

    /// <summary>Resolves the active graph's store and rendezvous once, mirroring the materialising leaf's per-evaluation resolution.</summary>
    /// <exception cref="InvalidOperationException">A named active graph is absent from the dataset (an executor bug — compilation should not have produced this cursor).</exception>
    private void Open()
    {
        //The default graph is null only under deferred residency before the trie is materialised; the BGP path
        //routes through the rendezvous (which serves the warm view or materialises on demand). A named graph is
        //never deferred — an absent one is an executor bug.
        graphStore = machinery.Dataset.Resolve(activeGraph);
        if(graphStore is null && !activeGraph.IsNone)
        {
            throw new InvalidOperationException($"Active graph '{activeGraph.Encoded}' is not in the dataset; the pipeline compiler should not have produced this cursor.");
        }

        rendezvous = activeGraph.IsNone ? machinery.Dataset.DefaultGraphRendezvous : null;
        opened = true;
    }

    /// <summary>Advances to the next expansion variant (or the single base variant), returning <see langword="false"/> when all variants have run.</summary>
    /// <returns><see langword="true"/> when a variant is available to open.</returns>
    private bool TryAdvanceVariant()
    {
        if(typeExpansions.Count == 0)
        {
            if(baseVariantOpened)
            {
                return false;
            }

            baseVariantOpened = true;

            return true;
        }

        if(expansionCursors is null)
        {
            expansionCursors = new int[typeExpansions.Count];

            return true;
        }

        //The cartesian product over the per-pattern alternatives, walked with explicit counters.
        int advance = typeExpansions.Count - 1;
        while(advance >= 0 && ++expansionCursors[advance] == typeExpansions[advance].Alternatives.Count)
        {
            expansionCursors[advance] = 0;
            advance--;
        }

        return advance >= 0;
    }

    /// <summary>Opens the current variant's source: the batched column source when eligible, else the per-row backend enumerator. The seeded configuration substitutes the current binding's patched patterns for the skeleton's.</summary>
    /// <param name="cancellationToken">The pull's token, bound to the opened source.</param>
    private void OpenVariant(CancellationToken cancellationToken)
    {
        List<EncodedTriplePattern> basePatterns = seededPatterns ?? encoded.Patterns;
        BasicGraphPattern query;
        if(typeExpansions.Count == 0)
        {
            query = seededPatterns is null ? baseQuery! : new BasicGraphPattern(seededPatterns, encoded.Registry);
        }
        else
        {
            //Expansion targets are skeleton-bound rdf:type objects; seeding patches only variable positions,
            //so the two substitutions never collide on a position.
            List<EncodedTriplePattern> variantPatterns = [.. basePatterns];
            for(int i = 0; i < typeExpansions.Count; i++)
            {
                (int patternIndex, List<TermId> alternatives) = typeExpansions[i];
                EncodedTriplePattern original = variantPatterns[patternIndex];
                variantPatterns[patternIndex] = new EncodedTriplePattern(original.Subject, original.Predicate, PatternPosition.Bound(alternatives[expansionCursors![i]]));
            }

            query = new BasicGraphPattern(variantPatterns, encoded.Registry);
        }

        if(machinery.TryOpenBatchedColumns(query, graphStore, rendezvous, encoded, typeExpansions) is IEnumerable<SolutionBatch> batches)
        {
            batchSource = batches.GetEnumerator();

            return;
        }

        RowSource = machinery.OpenRowSource(query, graphStore, rendezvous, activeGraph, cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    /// <summary>Decodes one batch row into a SELECT solution, resolving each bound projected cell through the dictionary.</summary>
    /// <param name="batch">The current batch.</param>
    /// <param name="row">The row index within the batch.</param>
    /// <returns>The decoded solution.</returns>
    private SparqlSolution DecodeBatchRow(SolutionBatch batch, int row)
    {
        IReadOnlyList<Variable> schema = batch.Schema;
        if(!ReferenceEquals(batchProjectionSchema, schema))
        {
            //One projection map per batch schema: each batch column either decodes to its distinguished SPARQL
            //variable or is a non-projected join variable (null slot).
            SparqlVariable?[] projection = new SparqlVariable?[schema.Count];
            for(int source = 0; source < schema.Count; source++)
            {
                projection[source] = encoded.ToSparql.TryGetValue(schema[source], out SparqlVariable variable) ? variable : null;
            }

            batchProjectionSchema = schema;
            batchProjection = projection;
        }

        SparqlVariable?[] map = batchProjection!;
        List<SparqlBinding> bindings = new(map.Length);
        for(int source = 0; source < map.Length; source++)
        {
            if(map[source] is not SparqlVariable variable)
            {
                continue;
            }

            uint value = batch.ColumnOf(source)[row];
            if(value == 0)
            {
                continue;
            }

            bindings.Add(new SparqlBinding(variable, machinery.Dictionary.Resolve(TermId.FromEncoded(value))));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>Disposes the batched source and clears its per-batch state.</summary>
    private void CloseBatchSource()
    {
        batchSource?.Dispose();
        batchSource = null;
        currentBatch = null;
        batchRow = 0;
    }

    /// <summary>Disposes the per-row source enumerator.</summary>
    /// <returns>A task completing when the enumerator is disposed.</returns>
    private async ValueTask CloseRowSourceAsync()
    {
        if(RowSource is IAsyncEnumerator<Solution> source)
        {
            RowSource = null;
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }
}
