using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Causality;

/// <summary>
/// The dotted observed-remove primitives: causal-context coverage stays correct under out-of-order folds and
/// monotone merges, and the causality annotation survives its serialized forms byte-exactly — the journal
/// record's causality section and the at-rest artifact's assignment sections, which replay READS rather than
/// re-derives.
/// </summary>
[TestClass]
internal sealed class CausalityPrimitiveTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A deterministic replica axis whose 32 bytes all carry <paramref name="seed"/>.</summary>
    /// <param name="seed">The byte every position of the identity carries.</param>
    /// <returns>The axis.</returns>
    private static ReplicaAxis Axis(byte seed)
    {
        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, seed);

        return new ReplicaAxis(bytes);
    }

    /// <summary>Coverage folded out of order compacts to the same knowledge: after folding counters 1, 3, and 2 the contiguous prefix reaches 3, every dot is covered, and a counter never folded stays uncovered.</summary>
    [TestMethod]
    public void ContextFoldCoversAndCompactsOutOfOrderCounters()
    {
        ReplicaAxis axis = Axis(0x0A);
        CausalContext context = new();
        context.Fold(new CausalDot(axis, 1));
        context.Fold(new CausalDot(axis, 3));

        Assert.IsTrue(context.Covers(new CausalDot(axis, 3)), "A folded counter beyond the prefix is covered from the cloud.");
        Assert.IsFalse(context.Covers(new CausalDot(axis, 2)), "A counter never folded is not covered by its neighbours.");
        Assert.AreEqual(1UL, context.PrefixMaxOn(axis), "The contiguous prefix stops at the gap.");
        Assert.AreEqual(3UL, context.MaxOn(axis), "The overall maximum reads through the cloud.");

        context.Fold(new CausalDot(axis, 2));

        Assert.AreEqual(3UL, context.PrefixMaxOn(axis), "Filling the gap drains the cloud run into the contiguous prefix.");
        Assert.IsTrue(context.Covers(new CausalDot(axis, 2)), "The gap counter is covered once folded.");
    }

    /// <summary>Merging contexts is a monotone, idempotent join: the merged context dominates both inputs, and re-merging changes nothing.</summary>
    [TestMethod]
    public void ContextMergeIsAMonotoneIdempotentJoin()
    {
        ReplicaAxis a = Axis(0x0A);
        ReplicaAxis b = Axis(0x0B);
        CausalContext left = new();
        left.Fold(new CausalDot(a, 1));
        left.Fold(new CausalDot(a, 2));
        CausalContext right = new();
        right.Fold(new CausalDot(a, 4));
        right.Fold(new CausalDot(b, 1));

        CausalContext merged = left.Clone();
        merged.Merge(right);

        Assert.IsTrue(left.CoveredBy(merged), "The join dominates its left input.");
        Assert.IsTrue(right.CoveredBy(merged), "The join dominates its right input.");
        Assert.IsFalse(merged.Covers(new CausalDot(a, 3)), "The join invents no coverage: the gap neither input observed stays open.");

        CausalContext remerged = merged.Clone();
        remerged.Merge(right);
        Assert.IsTrue(remerged.CoveredBy(merged), "Re-merging observed knowledge is a no-op.");
        Assert.IsTrue(merged.CoveredBy(remerged), "Re-merging observed knowledge is a no-op in both directions.");
    }

    /// <summary>A context with a prefix, a cloud gap, and a second axis round-trips its serialized form with identical coverage.</summary>
    [TestMethod]
    public void ContextSerializationRoundTripsCoverage()
    {
        ReplicaAxis a = Axis(0x0A);
        ReplicaAxis b = Axis(0x0B);
        CausalContext context = new();
        context.Fold(new CausalDot(a, 1));
        context.Fold(new CausalDot(a, 2));
        context.Fold(new CausalDot(a, 5));
        context.Fold(new CausalDot(b, 7));

        byte[] image = new byte[context.ComputeSerializedSize()];
        int written = context.WriteTo(image);
        Assert.AreEqual(image.Length, written, "The computed size names the exact written length.");

        int position = 0;
        CausalContext read = CausalContext.ReadFrom(image, ref position);
        Assert.AreEqual(image.Length, position, "The read consumes the whole image.");
        Assert.IsTrue(context.CoveredBy(read), "The round-tripped context dominates the original.");
        Assert.IsTrue(read.CoveredBy(context), "The original dominates the round-tripped context.");
        Assert.IsFalse(read.Covers(new CausalDot(a, 3)), "The round-trip invents no coverage inside the gap.");
    }

    /// <summary>A full annotation — dotted additions, dotted drops, a folded context, and the baseline flag — round-trips its wire form field by field.</summary>
    [TestMethod]
    public void CommitCausalityRoundTripsThroughItsRecordForm()
    {
        ReplicaAxis a = Axis(0x0A);
        ReplicaAxis b = Axis(0x0B);
        EncodedTriple added = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple dropped = EncodedTriple.FromEncoded(3, 100, 4);
        CausalContext folded = new();
        folded.Fold(new CausalDot(b, 9));
        CommitCausality causality = new(
            [new DottedTripleAssignment(added, [new CausalDot(a, 5), new CausalDot(b, 9)])],
            [new DottedTripleAssignment(dropped, [new CausalDot(a, 2)])],
            folded,
            IsBaseline: false);

        byte[] image = new byte[causality.ComputeSerializedSize()];
        int written = causality.WriteTo(image);
        Assert.AreEqual(image.Length, written, "The computed size names the exact written length.");

        int position = 0;
        CommitCausality read = CommitCausality.ReadFrom(image, ref position);
        Assert.AreEqual(image.Length, position, "The read consumes the whole image.");
        Assert.IsFalse(read.IsBaseline, "The baseline flag round-trips.");
        Assert.HasCount(1, read.Additions);
        Assert.AreEqual(added, read.Additions[0].Triple, "The addition's triple round-trips.");
        Assert.HasCount(2, read.Additions[0].Dots);
        Assert.AreEqual(new CausalDot(a, 5), read.Additions[0].Dots[0], "The addition's first dot round-trips, axis and counter.");
        Assert.AreEqual(new CausalDot(b, 9), read.Additions[0].Dots[1], "The addition's second dot round-trips, axis and counter.");
        Assert.HasCount(1, read.Drops);
        Assert.AreEqual(dropped, read.Drops[0].Triple, "The drop's triple round-trips.");
        Assert.AreEqual(new CausalDot(a, 2), read.Drops[0].Dots[0], "The drop's dot round-trips.");
        Assert.IsNotNull(read.FoldedContext, "The folded context's presence round-trips.");
        Assert.IsTrue(read.FoldedContext!.Covers(new CausalDot(b, 9)), "The folded context's coverage round-trips.");
    }

    /// <summary>A durable dataset journal record carries its causality annotation through the framed record form, and a causality-only Committed record — empty transitions, child equal to parent — is a first-class record.</summary>
    [TestMethod]
    public void CausalityRidesTheDurableJournalRecordIncludingCausalityOnly()
    {
        ReplicaAxis a = Axis(0x0A);
        using Utf8StringPool pool = new();
        ChecksumAlgorithm checksum = ChecksumAlgorithm.XxHash3;
        EncodedTriple added = EncodedTriple.FromEncoded(1, 100, 2);
        CommitCausality minted = new([new DottedTripleAssignment(added, [new CausalDot(a, 1)])], [], FoldedContext: null, IsBaseline: false);
        DatasetGraphTransition transition = new(TermId.None, ParentRoot: NodeIdentifier.Empty, ChildRoot: new NodeIdentifier(7), Additions: [added], Removals: []);
        DatasetJournalEntry annotated = DatasetJournalEntry.Committed(VeritasHashing.Default, new NodeIdentifier(1), new NodeIdentifier(2), SessionId.NewId(), [transition], minted);

        byte[] record = new byte[DatasetJournalRecordFormat.ComputeRecordSize(annotated, 0, [], checksum)];
        DatasetJournalRecordFormat.WriteRecord(record, annotated, 0, [], checksum);
        Assert.IsTrue(DatasetJournalRecordFormat.TryReadRecord(record, checksum, pool, out DatasetJournalRecord readBack, out _), "An annotated record reads back checksum-valid.");
        Assert.IsNotNull(readBack.Entry.Causality, "The annotation rides the record.");
        Assert.AreEqual(new CausalDot(a, 1), readBack.Entry.Causality!.Additions[0].Dots[0], "The recorded dot reads back verbatim — replay reads, never re-derives.");

        CausalContext folded = new();
        folded.Fold(new CausalDot(a, 1));
        CommitCausality terminalFold = new([], [], folded, IsBaseline: false);
        DatasetJournalEntry causalityOnly = DatasetJournalEntry.Committed(VeritasHashing.Default, new NodeIdentifier(2), new NodeIdentifier(2), SessionId.NewId(), [], terminalFold);

        byte[] bare = new byte[DatasetJournalRecordFormat.ComputeRecordSize(causalityOnly, 0, [], checksum)];
        DatasetJournalRecordFormat.WriteRecord(bare, causalityOnly, 0, [], checksum);
        Assert.IsTrue(DatasetJournalRecordFormat.TryReadRecord(bare, checksum, pool, out DatasetJournalRecord readBare, out _), "A causality-only record reads back checksum-valid.");
        Assert.AreEqual(readBare.Entry.ParentId, readBare.Entry.ChildId, "A causality-only commit changes no committed state.");
        Assert.IsEmpty(readBare.Entry.Transitions, "A causality-only commit carries no transitions.");
        Assert.IsNotNull(readBare.Entry.Causality, "The terminal fold's knowledge rides the record.");
        Assert.IsTrue(readBare.Entry.Causality!.FoldedContext!.Covers(new CausalDot(a, 1)), "The folded context reads back verbatim.");
    }

    /// <summary>The at-rest causality artifact — identities, entry table, context, and pairing StateId — round-trips its image form.</summary>
    [TestMethod]
    public void LedgerSnapshotImageRoundTrips()
    {
        ReplicaAxis a = Axis(0x0A);
        EncodedTriple present = EncodedTriple.FromEncoded(1, 100, 2);
        CausalContext context = new();
        context.Fold(new CausalDot(a, 1));
        context.Fold(new CausalDot(a, 2));
        DottedLedgerSnapshot snapshot = new(
            [a],
            [new DottedTripleAssignment(present, [new CausalDot(a, 2)])],
            context,
            new NodeIdentifier(0xBEEF));

        byte[] image = new byte[snapshot.ComputeSerializedSize()];
        int written = snapshot.WriteTo(image);
        Assert.AreEqual(image.Length, written, "The computed size names the exact written length.");

        DottedLedgerSnapshot read = DottedLedgerSnapshot.ReadFrom(image);
        Assert.AreEqual(new NodeIdentifier(0xBEEF), read.StateId, "The pairing StateId round-trips.");
        Assert.HasCount(1, read.Identities);
        Assert.AreEqual(a, read.Identities[0], "The identity axis round-trips.");
        Assert.HasCount(1, read.Entries);
        Assert.AreEqual(present, read.Entries[0].Triple, "The entry's triple round-trips.");
        Assert.AreEqual(new CausalDot(a, 2), read.Entries[0].Dots[0], "The entry's dot round-trips.");
        Assert.IsTrue(read.Context.Covers(new CausalDot(a, 1)), "The context's coverage round-trips, dropped dots included.");
    }
}
