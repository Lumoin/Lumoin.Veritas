using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The Lumoin.Veritas.Replication library boundary: the rateless codec it binds lives outside the core, the
/// dependency edges stay acyclic (the core references neither the replication library nor the Lumoin.Verisync
/// tier; the replication library references both), and reconciling two diverged replicas THROUGH the library's
/// persisted-sketch codec recovers exactly their symmetric difference and converges them by repair-as-ingest.
/// </summary>
[TestClass]
internal sealed class ReplicationLibraryBoundaryTests
{
    /// <summary>A line of triples with a shared predicate: subjects [start, start + count), each linked to the next identifier.</summary>
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

    /// <summary>Persists a replica's items as a structural sketch through the library's encode binder and loads it back verified.</summary>
    /// <param name="items">The replica's projected items.</param>
    /// <param name="symbolCount">The number of symbols to persist.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from.</param>
    /// <returns>The verified sketch.</returns>
    private static VerifiedSketch PersistAndLoad(ContentKey128[] items, int symbolCount, MemoryPool<byte> pool)
    {
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCount, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchPersistence.LoadVerifiedSketch(writer.WrittenSpan, SketchContract.Structural);
    }

    /// <summary>The core assembly references neither the replication library nor the Lumoin.Verisync tier — the reconciliation codec is host-bound and the dependency edge runs only one way.</summary>
    [TestMethod]
    public void CoreReferencesNeitherTheReplicationLibraryNorVerisync()
    {
        AssemblyName[] referenced = typeof(SketchContract).Assembly.GetReferencedAssemblies();
        foreach(AssemblyName name in referenced)
        {
            bool isReplication = name.Name is string replicationName && replicationName.StartsWith("Lumoin.Veritas.Replication", StringComparison.Ordinal);
            Assert.IsFalse(isReplication, $"Lumoin.Veritas.Core must not reference {name.Name}; replication is a downstream library.");
            bool isVerisync = name.Name is string verisyncName && verisyncName.StartsWith("Lumoin.Verisync", StringComparison.Ordinal);
            Assert.IsFalse(isVerisync, $"Lumoin.Veritas.Core must not reference {name.Name}; the reconciliation seam is host-bound.");
        }
    }

    /// <summary>The replication library references the core and the Lumoin.Verisync rateless tier — it is the active-active home where the codec lives.</summary>
    [TestMethod]
    public void TheReplicationLibraryReferencesCoreAndTheRatelessTier()
    {
        List<string> referenced = [.. typeof(RatelessSketchCodec).Assembly.GetReferencedAssemblies().Select(static name => name.Name ?? string.Empty)];
        Assert.Contains("Lumoin.Veritas.Core", referenced, "The replication library must reference the core.");
        Assert.Contains("Lumoin.Verisync.Core", referenced, "The replication library must reference the Lumoin.Verisync rateless tier.");
    }

    /// <summary>Two diverged replicas reconcile to exactly their symmetric difference through the library's persisted-sketch codec, and repair-as-ingest converges both to identical state.</summary>
    [TestMethod]
    public void DivergedReplicasReconcileThroughTheLibraryCodecAndConverge()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triplesB);

        ContentKey128[] itemsA = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        ContentKey128[] itemsB = [.. triplesB.Select(StructuralReconciliationProjection.Project)];
        int cap = 100 + (20 * (replicaA.TripleCount + replicaB.TripleCount));

        VerifiedSketch sketchA = PersistAndLoad(itemsA, cap, pool);
        VerifiedSketch sketchB = PersistAndLoad(itemsB, cap, pool);

        ContentKey128[] recovered = new ContentKey128[sketchA.SymbolCount + sketchB.SymbolCount];
        int n = new RatelessSketchCodec(pool).Decode(sketchA, sketchB, cap, recovered);

        HashSet<ContentKey128> recoveredKeys = [.. recovered[..n]];
        HashSet<ContentKey128> expected = [.. itemsA];
        expected.SymmetricExceptWith(itemsB);
        Assert.IsTrue(expected.SetEquals(recoveredKeys), "The library codec must recover exactly the symmetric difference.");

        //Repair-as-ingest: applying the recovered triples to BOTH replicas converges them — Apply is idempotent.
        EncodedTriple[] recoveredTriples = [.. recoveredKeys.Select(StructuralReconciliationProjection.Invert)];
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
}
