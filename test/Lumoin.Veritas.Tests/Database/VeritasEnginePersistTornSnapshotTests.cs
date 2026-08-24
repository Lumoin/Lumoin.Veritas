using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Pins cross-graph atomicity of <see cref="VeritasEngine.Persist"/> against a concurrent committer.
/// <para>
/// <see cref="VeritasEngine.Persist"/> captures the default graph and every named graph from ONE committed
/// dataset state, so a concurrent SPARQL Update that moves BOTH the default graph and a named graph in one
/// atomic commit lands wholly before or wholly after the capture - never split half-old/half-new into a
/// persisted generation that corresponds to no committed dataset state.
/// </para>
/// <para>
/// The probe keeps one marker triple in lockstep across the default graph and a named graph (present in both
/// or absent from both in every committed state), so a self-consistent persisted generation holds either
/// <c>base</c> triples (marker in neither) or <c>base + 2</c> (marker in both); a torn snapshot would hold
/// exactly <c>base + 1</c> - a count no committed state can produce. Persisting repeatedly while the committer
/// oscillates the marker, the probe asserts no capture ever holds <c>base + 1</c> and that a reopen of the
/// final generation serves the marker in both graphs or in neither.
/// </para>
/// </summary>
[TestClass]
internal sealed class VeritasEnginePersistTornSnapshotTests
{
    /// <summary>The example-namespace prefix the probe data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The number of fixed base triples seeded into the default graph; a larger base widens Persist's capture window, so a torn capture, were it possible, would be caught.</summary>
    private const int BaseTripleCount = 4000;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A directory durability barrier that does nothing, so the probe does not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a named IRI term.</summary>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>
    /// Under a concurrent cross-graph committer, every <see cref="VeritasEngine.Persist"/> captures a
    /// self-consistent snapshot - the persisted generation holds a triple count only a committed dataset state
    /// can produce (<c>base</c> or <c>base + 2</c>, never <c>base + 1</c>) - and a reopen of the final generation
    /// serves the marker in both graphs or in neither, never in exactly one.
    /// </summary>
    [TestMethod]
    public async Task PersistUnderConcurrentCrossGraphCommitsNeverCapturesATornSnapshot()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-torn-").FullName;

        List<DataTriple> seed = new(BaseTripleCount);
        for(int i = 0; i < BaseTripleCount; i++)
        {
            seed.Add(new DataTriple(Iri($"{Ex}base{i}"), Iri($"{Ex}p"), Iri($"{Ex}o")));
        }

        VeritasEngine mutable = await VeritasEngine
            .OpenMutableAsync(seed, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        //The oscillator toggles ONE marker in both the default graph and named graph <g> in ONE update, so every
        //committed state has the marker in both graphs or in neither - the invariant a torn Persist would violate.
        Utf8String insertBoth = Utf8Strings.From($"INSERT DATA {{ <{Ex}marker> <{Ex}p> <{Ex}o> . GRAPH <{Ex}g> {{ <{Ex}marker> <{Ex}p> <{Ex}o> }} }}");
        Utf8String deleteBoth = Utf8Strings.From($"DELETE DATA {{ <{Ex}marker> <{Ex}p> <{Ex}o> . GRAPH <{Ex}g> {{ <{Ex}marker> <{Ex}p> <{Ex}o> }} }}");

        CancellationToken cancellationToken = TestContext.CancellationToken;
        bool stop = false;
        Task oscillator = Task.Run(async () =>
        {
            while(!Volatile.Read(ref stop))
            {
                await mutable.UpdateAsync(insertBoth, cancellationToken: cancellationToken).ConfigureAwait(false);
                await mutable.UpdateAsync(deleteBoth, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        int tornTripleCount = -1;
        long tornGeneration = -1;
        bool sawMarkerPresent = false;
        bool sawMarkerAbsent = false;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            //Persist repeatedly against the oscillator until it has raced the capture through both the
            //marker-present and marker-absent phases (contention demonstrated) or a torn capture is
            //caught. Every capture is checked; a torn one would hold base + 1. The token check keeps
            //the loop cooperative, so an oscillator that never commits both phases surfaces at the
            //runner-level hang guard rather than spinning unobserved.
            while(tornTripleCount < 0
                && !(sawMarkerPresent && sawMarkerAbsent))
            {
                TestContext.CancellationToken.ThrowIfCancellationRequested();
                DurableSystemOfRecordCommit commit = mutable.Persist(store);

                //A self-consistent generation holds base (marker absent from both) or base + 2 (present in both);
                //base + 1 would mean the default segment and the named segment came from different committed states.
                if(commit.TripleCount == BaseTripleCount + 1)
                {
                    tornTripleCount = commit.TripleCount;
                    tornGeneration = commit.Generation;
                }
                else if(commit.TripleCount == BaseTripleCount + 2)
                {
                    sawMarkerPresent = true;
                }
                else if(commit.TripleCount == BaseTripleCount)
                {
                    sawMarkerAbsent = true;
                }

                //Yield between captures so the oscillator's continuations are guaranteed
                //scheduling on any core count — the loop's exit rides its progress.
                await Task.Yield();
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            await oscillator.ConfigureAwait(false);
            await mutable.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            Assert.AreEqual(
                -1,
                tornTripleCount,
                $"Persist captured a torn cross-graph snapshot: generation {tornGeneration} holds base + 1 triples, a count no committed dataset state can produce.");

            //End to end: the reopened database serves a committed state - the marker present in both graphs or in
            //neither, never in exactly one.
            FileSystemPersistenceStore reopenStore = new(directory, NoOpBarrier);
            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(reopenStore, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using(reopened.ConfigureAwait(false))
            {
                bool defaultHasMarker = await reopened
                    .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}marker> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                bool namedHasMarker = await reopened
                    .AskAsync(Utf8Strings.From($"ASK {{ GRAPH <{Ex}g> {{ <{Ex}marker> <{Ex}p> <{Ex}o> }} }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);

                Assert.AreEqual(
                    defaultHasMarker,
                    namedHasMarker,
                    $"The reopened durable generation must serve a committed state: default has marker = {defaultHasMarker}, named has marker = {namedHasMarker}. Every committed state has the marker in both graphs or in neither.");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
