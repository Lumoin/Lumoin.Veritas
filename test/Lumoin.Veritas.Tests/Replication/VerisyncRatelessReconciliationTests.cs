using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// Validates the wiring between a Veritas graph and the Lumoin.Verisync rateless
/// anti-entropy tier: a built <see cref="ColumnarTripleIndex"/> projects each triple to a
/// 16-byte <see cref="ContentKey128"/> reconciliation item, two diverged replicas' coded-symbol
/// streams are combined and decoded to recover EXACTLY their symmetric difference, and applying
/// the recovered triples as repair-as-ingest converges both replicas to identical state. Verisync
/// proves the reconciliation protocol against its own law and vector suites; these tests assert
/// only the Veritas-side contract — the projection (pure, injective, invertible) and the
/// repair-as-ingest convergence.
/// </summary>
[TestClass]
internal sealed class VerisyncRatelessReconciliationTests
{
    /// <summary>The MSTest execution context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The governed pool the reconciliation encoder and decoder rent from, shared across the suite — the same pool kind production threads, so the tests exercise the tracked allocation path rather than an untracked shared allocator.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>A line of triples with a shared predicate: subjects <c>[start, start + count)</c>, each linked to the next identifier.</summary>
    /// <param name="start">The first subject identifier.</param>
    /// <param name="count">The number of triples.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Line(uint start, uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            uint subject = start + i;
            triples[i] = EncodedTriple.FromEncoded(subject, 10, subject + 1);
        }

        return triples;
    }

    /// <summary>Adds every triple of a graph to a reconciliation encoder as its 16-byte content-key item, reusing one stack buffer.</summary>
    /// <param name="encoder">The encoder to feed.</param>
    /// <param name="graph">The graph whose triples are projected.</param>
    private static void AddProjectedItems(ReconciliationEncoder encoder, ColumnarTripleIndex graph)
    {
        Span<byte> item = stackalloc byte[ContentKey128.ByteWidth];
        foreach(EncodedTriple triple in graph.EnumerateTriples())
        {
            StructuralReconciliationProjection.Project(triple).WriteBytes(item);
            encoder.Add(item);
        }
    }

    /// <summary>Reconciles two diverged graphs through the rateless tier and returns the recovered symmetric-difference keys.</summary>
    /// <param name="contract">The shared reconciliation contract.</param>
    /// <param name="left">One graph.</param>
    /// <param name="right">The other graph.</param>
    /// <returns>The recovered difference keys.</returns>
    private static HashSet<ContentKey128> ReconcileDifference(ReconciliationContract contract, ColumnarTripleIndex left, ColumnarTripleIndex right)
    {
        using ReconciliationEncoder leftEncoder = new(contract, ReconciliationInjectivityEnforcement.None, Pool);
        AddProjectedItems(leftEncoder, left);
        using ReconciliationEncoder rightEncoder = new(contract, ReconciliationInjectivityEnforcement.None, Pool);
        AddProjectedItems(rightEncoder, right);

        using ReconciliationDecoder decoder = new(contract, Pool);
        int cap = 100 + (20 * (left.TripleCount + right.TripleCount));
        int absorbed = 0;
        while(!decoder.IsComplete && absorbed < cap)
        {
            ReconciliationSymbol difference = leftEncoder.ProduceNext().Combine(rightEncoder.ProduceNext());
            decoder.Absorb(difference);
            absorbed++;
        }

        Assert.IsTrue(decoder.IsComplete, "The rateless decoder must converge within the symbol cap.");

        return [.. decoder.DecodedItems.Select(static recovered => ContentKey128.FromBytes(recovered.Span))];
    }

    /// <summary>The structural projection is injective and invertible: distinct triples map to distinct keys, and every key recovers its triple.</summary>
    [TestMethod]
    public void StructuralProjectionIsInjectiveAndInvertible()
    {
        EncodedTriple[] triples = Line(0, 1000);

        HashSet<ContentKey128> keys = [.. triples.Select(StructuralReconciliationProjection.Project)];
        Assert.HasCount(triples.Length, keys, "Distinct triples must project to distinct content keys.");

        foreach(EncodedTriple triple in triples)
        {
            Assert.AreEqual(triple, StructuralReconciliationProjection.Invert(StructuralReconciliationProjection.Project(triple)), "A structural content key must recover its triple exactly.");
        }
    }

    /// <summary>Two diverged replicas reconcile to exactly their symmetric difference, and repair-as-ingest converges both to identical state.</summary>
    [TestMethod]
    public void DivergedReplicasRecoverTheDifferenceAndConverge()
    {
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triplesB);

        ReconciliationContract contract = new(
            ReconciliationItemDomain.Structural,
            ContentKey128.ByteWidth,
            8,
            ReconciliationContract.WellKnownChecksumKeyLow,
            ReconciliationContract.WellKnownChecksumKeyHigh);

        HashSet<ContentKey128> recovered = ReconcileDifference(contract, replicaA, replicaB);

        HashSet<ContentKey128> expected = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        expected.SymmetricExceptWith(triplesB.Select(StructuralReconciliationProjection.Project));
        Assert.IsTrue(expected.SetEquals(recovered), "The rateless tier must recover exactly the symmetric difference of the two replicas.");

        //Repair-as-ingest: applying the recovered triples to BOTH replicas converges them — Apply
        //is idempotent, so each side ignores the triples it already holds and admits the ones it lacks.
        EncodedTriple[] recoveredTriples = [.. recovered.Select(StructuralReconciliationProjection.Invert)];
        ColumnarTripleIndex convergedA = replicaA.Apply(recoveredTriples, []);
        ColumnarTripleIndex convergedB = replicaB.Apply(recoveredTriples, []);

        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        HashSet<EncodedTriple> finalA = [.. convergedA.EnumerateTriples()];
        HashSet<EncodedTriple> finalB = [.. convergedB.EnumerateTriples()];

        Assert.IsTrue(union.SetEquals(finalA), "Replica A must converge to the union of both replicas.");
        Assert.IsTrue(union.SetEquals(finalB), "Replica B must converge to the union of both replicas.");
        Assert.IsTrue(finalA.SetEquals(finalB), "Both replicas must reach identical state.");
    }

    /// <summary>Identical replicas reconcile to an empty difference — no spurious items recovered.</summary>
    [TestMethod]
    public void IdenticalReplicasRecoverNoDifference()
    {
        EncodedTriple[] triples = Line(0, 100);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triples);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triples);

        ReconciliationContract contract = new(
            ReconciliationItemDomain.Structural,
            ContentKey128.ByteWidth,
            8,
            ReconciliationContract.WellKnownChecksumKeyLow,
            ReconciliationContract.WellKnownChecksumKeyHigh);

        HashSet<ContentKey128> recovered = ReconcileDifference(contract, replicaA, replicaB);

        Assert.IsEmpty(recovered, "Identical replicas must reconcile to an empty difference.");
    }
}
