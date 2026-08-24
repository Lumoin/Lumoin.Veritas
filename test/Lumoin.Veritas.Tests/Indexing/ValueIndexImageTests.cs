using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Indexing;

namespace Lumoin.Veritas.Tests.Indexing;

/// <summary>
/// The value-index sidecar container's wire pins: a point-and-pair image round-trips byte-exactly
/// through <see cref="ValueIndexImage.WriteTo"/> and <see cref="ValueIndexImage.TryReadFrom"/>, and
/// every malformation class — a foreign magic or version, truncation, trailing bytes, and negative
/// prefixes — is refused whole rather than parsed partially.
/// </summary>
[TestClass]
internal sealed class ValueIndexImageTests
{
    /// <summary>The dataset-state stamp the round-trip pins.</summary>
    private const ulong StateStamp = 0xABCDEF0123456789UL;

    /// <summary>The point-axis datatype IRI.</summary>
    private static Utf8String DateTimeIri => Utf8Strings.From("http://www.w3.org/2001/XMLSchema#dateTime");

    /// <summary>The point-axis predicate.</summary>
    private static Utf8String At => Utf8Strings.From("http://example.org/at");

    /// <summary>The interval pair's start predicate.</summary>
    private static Utf8String From => Utf8Strings.From("http://example.org/from");

    /// <summary>The interval pair's end predicate.</summary>
    private static Utf8String Until => Utf8Strings.From("http://example.org/until");

    /// <summary>A written image parses back whole: the stamp, both entries' axis identities (point and pair), and the payload bytes.</summary>
    [TestMethod]
    public void ImageRoundTripsPointAndPairEntries()
    {
        ValueIndexImage image = new(StateStamp,
        [
            new ValueIndexImageEntry(DateTimeIri, At, null, new byte[] { 1, 2, 3 }),
            new ValueIndexImageEntry(DateTimeIri, From, Until, new byte[] { 9, 8, 7, 6 }),
        ]);

        byte[] buffer = new byte[image.ComputeSerializedSize()];
        image.WriteTo(buffer);

        Assert.IsTrue(ValueIndexImage.TryReadFrom(buffer, out ValueIndexImage? read));
        Assert.AreEqual(StateStamp, read!.StateId);
        Assert.HasCount(2, read.Entries);

        Assert.AreEqual(DateTimeIri, read.Entries[0].DatatypeIri);
        Assert.AreEqual(At, read.Entries[0].StartPredicateIri);
        Assert.IsNull(read.Entries[0].EndPredicateIri);
        Assert.IsTrue(read.Entries[0].Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 }));

        Assert.AreEqual(From, read.Entries[1].StartPredicateIri);
        Assert.AreEqual(Until, read.Entries[1].EndPredicateIri);
        Assert.IsTrue(read.Entries[1].Payload.Span.SequenceEqual(new byte[] { 9, 8, 7, 6 }));
    }

    /// <summary>An empty-entry image (a registry whose every method declined snapshots never persists one, but the wire form is total) round-trips.</summary>
    [TestMethod]
    public void ImageRoundTripsWithNoEntries()
    {
        ValueIndexImage image = new(StateStamp, []);
        byte[] buffer = new byte[image.ComputeSerializedSize()];
        image.WriteTo(buffer);

        Assert.IsTrue(ValueIndexImage.TryReadFrom(buffer, out ValueIndexImage? read));
        Assert.AreEqual(StateStamp, read!.StateId);
        Assert.IsEmpty(read.Entries);
    }

    /// <summary>Every malformation class is refused whole: foreign magic, foreign version, truncation at several depths, trailing bytes, and a negative count or block prefix.</summary>
    [TestMethod]
    public void MalformedImagesAreRefusedWhole()
    {
        ValueIndexImage image = new(StateStamp, [new ValueIndexImageEntry(DateTimeIri, At, null, new byte[] { 1, 2, 3 })]);
        byte[] buffer = new byte[image.ComputeSerializedSize()];
        image.WriteTo(buffer);

        Assert.IsFalse(ValueIndexImage.TryReadFrom([], out _));

        byte[] foreignMagic = [.. buffer];
        foreignMagic[0] ^= 0xFF;
        Assert.IsFalse(ValueIndexImage.TryReadFrom(foreignMagic, out _));

        byte[] foreignVersion = [.. buffer];
        foreignVersion[4] = 9;
        Assert.IsFalse(ValueIndexImage.TryReadFrom(foreignVersion, out _));

        Assert.IsFalse(ValueIndexImage.TryReadFrom(buffer.AsSpan(..12), out _), "A header truncation is refused.");
        Assert.IsFalse(ValueIndexImage.TryReadFrom(buffer.AsSpan(..^1), out _), "A payload truncation is refused.");

        byte[] trailing = new byte[buffer.Length + 1];
        buffer.CopyTo(trailing, 0);
        Assert.IsFalse(ValueIndexImage.TryReadFrom(trailing, out _), "Trailing bytes are refused.");

        byte[] negativeCount = [.. buffer];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(negativeCount.AsSpan(13), -1);
        Assert.IsFalse(ValueIndexImage.TryReadFrom(negativeCount, out _), "A negative entry count is refused.");

        byte[] negativeBlock = [.. buffer];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(negativeBlock.AsSpan(17), -5);
        Assert.IsFalse(ValueIndexImage.TryReadFrom(negativeBlock, out _), "A negative block length is refused.");
    }
}
