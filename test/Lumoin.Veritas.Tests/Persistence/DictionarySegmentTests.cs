using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The persisted term dictionary: every term kind — named nodes, blank nodes, plain / language-tagged /
/// directional literals, nested triple terms, and engine-minted nodes — round-trips through the segment across the checksum
/// selections and block geometries, the dictionary epoch and the exact identifier assignment are preserved,
/// and at-rest corruption (a block byte, the front matter, a foreign block epoch), truncation, a foreign
/// checksum algorithm, and a hostile over-deep triple term are all refused.
/// </summary>
[TestClass]
internal sealed class DictionarySegmentTests
{
    /// <summary>The fixed header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) and scalar (8 epoch + 4 term count + 4 block term count) byte size before the per-block directory.</summary>
    private const int FrontMatterBase = 19 + 16;

    /// <summary>Builds a dictionary exercising every term kind, including a nested triple term and an engine-minted node, under a given epoch.</summary>
    /// <param name="epoch">The replication epoch the dictionary carries.</param>
    /// <returns>The sample dictionary; nine terms in identifier order.</returns>
    private static TermDictionary BuildSampleDictionary(ulong epoch)
    {
        TermDictionary dictionary = new(epoch);
        NamedNode xsdString = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"));
        NamedNode langString = new(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"));
        NamedNode dirLangString = new(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"));

        dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s")));
        dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/p")));
        dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("b0")));
        dictionary.GetOrAdd(new Literal(Utf8Strings.From("plain"), xsdString));
        dictionary.GetOrAdd(new Literal(Utf8Strings.From("hello"), langString, Utf8Strings.From("en")));
        dictionary.GetOrAdd(new Literal(Utf8Strings.From("שלום"), dirLangString, Utf8Strings.From("he"), TextDirection.Rtl));

        TripleTerm inner = new(
            new NamedNode(Utf8Strings.From("http://example.org/a")),
            new NamedNode(Utf8Strings.From("http://example.org/b")),
            new NamedNode(Utf8Strings.From("http://example.org/c")));
        dictionary.GetOrAdd(inner);
        dictionary.GetOrAdd(new TripleTerm(
            inner,
            new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")),
            new Literal(Utf8Strings.From("note"), xsdString)));
        dictionary.GetOrAdd((RdfTerm)new EngineNode(EngineNodeFamily.Create(7), 11, 22, 33, 44));

        return dictionary;
    }

    /// <summary>Builds a dictionary whose single term is a triple term left-nested to the given depth.</summary>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="epoch">The replication epoch the dictionary carries.</param>
    /// <returns>The dictionary holding the one deeply-nested term.</returns>
    private static TermDictionary BuildDeeplyNestedDictionary(int depth, ulong epoch)
    {
        TermDictionary dictionary = new(epoch);
        NamedNode predicate = new(Utf8Strings.From("http://example.org/p"));
        RdfTerm term = new NamedNode(Utf8Strings.From("http://example.org/leaf"));
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, new NamedNode(Utf8Strings.From("http://example.org/o")));
        }

        dictionary.GetOrAdd(term);

        return dictionary;
    }

    /// <summary>Serializes a dictionary into a fresh image under the given checksum selection and block geometry.</summary>
    /// <param name="dictionary">The dictionary to serialize.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="blockTermCount">The terms per block.</param>
    /// <returns>The serialized image.</returns>
    private static byte[] Serialize(TermDictionary dictionary, ChecksumAlgorithm? checksum, int blockTermCount)
    {
        DictionarySegment segment = new(dictionary, blockTermCount);
        byte[] image = new byte[segment.ComputeSerializedSize(checksum)];
        segment.WriteTo(image, checksum);

        return image;
    }

    /// <summary>Asserts the reconstructed dictionary matches the source: same count, same epoch, and each identifier resolves to a value-equal term.</summary>
    /// <param name="source">The original dictionary.</param>
    /// <param name="restored">The reconstructed dictionary.</param>
    /// <param name="context">A description of the case for assertion messages.</param>
    private static void AssertRoundTrip(TermDictionary source, TermDictionary restored, string context)
    {
        Assert.AreEqual(source.Count, restored.Count, $"The term count did not round-trip ({context}).");
        Assert.AreEqual(source.Epoch, restored.Epoch, $"The epoch did not round-trip ({context}).");
        for(uint id = 1; id <= (uint)source.Count; id++)
        {
            Assert.AreEqual(source.Resolve(id), restored.Resolve(id), $"Term {id} did not round-trip ({context}).");
        }
    }

    /// <summary>Every term kind round-trips in identifier order across the checksum selections and the single-block and multi-block geometries.</summary>
    [TestMethod]
    public void RoundTripsAllTermKindsAcrossChecksumSelectionsAndGeometries()
    {
        using Utf8StringPool pool = new();
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            foreach(int blockTermCount in (int[])[3, 1000])
            {
                TermDictionary source = BuildSampleDictionary(0xABCDEF01);
                byte[] image = Serialize(source, checksum, blockTermCount);
                TermDictionary restored = DictionarySegment.ReadFrom(image, pool, null);

                AssertRoundTrip(source, restored, $"algorithm {checksum?.Name ?? "none"}, block term count {blockTermCount}");
            }
        }
    }

    /// <summary>A distinct, non-zero epoch survives the round-trip — the recorded term-id space a restart checks files against.</summary>
    [TestMethod]
    public void EpochIsPreserved()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildSampleDictionary(0x0123456789ABCDEF);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 4);
        TermDictionary restored = DictionarySegment.ReadFrom(image, pool, null);

        Assert.AreEqual(0x0123456789ABCDEFUL, restored.Epoch);
    }

    /// <summary>An empty dictionary round-trips to an empty dictionary carrying the same epoch.</summary>
    [TestMethod]
    public void EmptyDictionaryRoundTrips()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = new(0x99);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 8);
        TermDictionary restored = DictionarySegment.ReadFrom(image, pool, null);

        Assert.AreEqual(0, restored.Count);
        Assert.AreEqual(0x99UL, restored.Epoch);
    }

    /// <summary>A byte flipped in a block's term records fails that block's checksum, under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void BlockCorruptionIsRejected()
    {
        using Utf8StringPool pool = new();
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            TermDictionary source = BuildSampleDictionary(0xABCD);
            byte[] image = Serialize(source, checksum, blockTermCount: 3);

            //The last payload byte before the trailer lies in the final block's records.
            image[image.Length - checksum.ByteWidth - 1] ^= 0xFF;

            InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => DictionarySegment.ReadFrom(image, pool, null));
            Assert.IsTrue(thrown.Message.Contains("checksum", StringComparison.Ordinal), $"The refusal did not name a checksum failure (algorithm {checksum.Name}): {thrown.Message}");
        }
    }

    /// <summary>A byte flipped in the per-block checksum section — covered by the front-matter trailer but not by any per-block digest — fails the front-matter trailer.</summary>
    [TestMethod]
    public void FrontMatterCorruptionIsRejected()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildSampleDictionary(0xABCD);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 3);

        //Nine terms in 3-term blocks is three blocks; the per-block checksum section begins after the
        //directory (three 4-byte lengths).
        int checksumSectionOffset = FrontMatterBase + (3 * sizeof(int));
        image[checksumSectionOffset] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => DictionarySegment.ReadFrom(image, pool, null));
    }

    /// <summary>A truncated image is refused rather than read past its end.</summary>
    [TestMethod]
    public void TruncatedImageIsRejected()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildSampleDictionary(0xABCD);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 3);
        int truncatedLength = image.Length - 10;

        Assert.ThrowsExactly<InvalidDataException>(() => DictionarySegment.ReadFrom(image.AsSpan(0, truncatedLength), pool, null));
    }

    /// <summary>An image stamped with a checksum-algorithm id no resolver knows is refused, not read under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIsRejected()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildSampleDictionary(0xABCD);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 3);

        //The checksum-algorithm id is the last header byte: magic (8) + major (1) + minor (1) + feature mask (8).
        image[18] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => DictionarySegment.ReadFrom(image, pool, null));
    }

    /// <summary>The epoch folded under each block defends a checksum-free image: a flipped block-epoch prefix is refused as a foreign generation, the recycle-safety the front-matter trailer alone cannot give when no checksum is selected.</summary>
    [TestMethod]
    public void ForeignBlockEpochIsRejectedWithoutChecksums()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildSampleDictionary(0xABCD);
        byte[] image = Serialize(source, checksum: null, blockTermCount: 3);

        //Without checksums the front matter ends after the directory (three 4-byte lengths); the first block's
        //epoch prefix begins there.
        int firstBlockOffset = FrontMatterBase + (3 * sizeof(int));
        image[firstBlockOffset] ^= 0xFF;

        InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => DictionarySegment.ReadFrom(image, pool, null));
        Assert.IsTrue(thrown.Message.Contains("epoch", StringComparison.Ordinal), $"The refusal did not name a foreign epoch: {thrown.Message}");
    }

    /// <summary>A triple term nested past the depth cap is refused on read with a recoverable error rather than overflowing the call stack.</summary>
    [TestMethod]
    public void OverDeepTripleTermIsRejected()
    {
        using Utf8StringPool pool = new();
        TermDictionary source = BuildDeeplyNestedDictionary(depth: 70, epoch: 0xABCD);
        byte[] image = Serialize(source, ChecksumAlgorithm.XxHash3, blockTermCount: 8);

        Assert.ThrowsExactly<InvalidDataException>(() => DictionarySegment.ReadFrom(image, pool, null));
    }

    /// <summary>The new Dictionary file role resolves by its on-disk code (6) when a manifest naming it round-trips — so a recovered generation can find its dictionary segment.</summary>
    [TestMethod]
    public void DictionaryRoleRoundTripsThroughTheManifest()
    {
        Assert.AreEqual(6, ManifestFileRole.Dictionary.Code);

        ManifestEntry entry = new(ManifestFileRole.Dictionary, "dict-0.dic", 0, 1234, ReadOnlyMemory<byte>.Empty);
        Manifest manifest = new(commitGeneration: 0, dictionaryEpoch: 7, provenanceEpoch: 0, [entry]);
        byte[] image = new byte[manifest.ComputeSerializedSize(null)];
        manifest.WriteTo(image, null);

        Manifest restored = Manifest.ReadFrom(image, null);
        Assert.HasCount(1, restored.Entries);
        Assert.AreEqual(ManifestFileRole.Dictionary, restored.Entries[0].Role);
        Assert.AreEqual("dict-0.dic", restored.Entries[0].FileName);
    }
}
