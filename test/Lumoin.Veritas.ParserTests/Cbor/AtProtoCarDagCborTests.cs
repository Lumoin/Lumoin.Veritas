using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.DagCbor;
using Lumoin.Veritas.Cbor.Car;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Validates the DAG-CBOR codec and the streaming CBOR reader against a
/// real CAR file from a public AT Protocol repository endpoint. Fixtures live
/// in <c>Fixtures/AtProto/</c>; these are real-data probes, not
/// W3C conformance tests, so a probe whose fixture is absent passes with
/// nothing to validate.
/// </summary>
[TestClass]
internal sealed class AtProtoCarDagCborTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow("atproto-com.car")]
    public void FixtureParsesUnderRelaxedDagCbor(string fixtureName)
    {
        byte[]? carBytes = TryLoadFixture(fixtureName);
        if(carBytes is null)
        {
            //A real-data fixture, not a W3C conformance test: it comes from a
            //manual CAR download (see Fixtures/AtProto/README.md). When it is
            //not vendored there is nothing to validate and the probe passes.
            TestContext.WriteLine($"Real-data fixture {fixtureName} is not vendored; skipping (see Fixtures/AtProto/README.md).");
            return;
        }

        CarFileReader carReader = new(carBytes);
        CarFileHeader header = carReader.ReadHeader();
        Assert.IsGreaterThan(0, header.Roots.Count);

        int blockCount = 0;
        while(carReader.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            //Parse contiguous via relaxed DAG-CBOR. Walk every value to
            //surface any wire-form rejection.
            DagCborReader reader = new(block, strict: false);
            ConsumeAllValues(reader);
            blockCount++;
        }

        Assert.IsGreaterThan(0, blockCount);
    }

    [TestMethod]
    [DataRow("atproto-com.car")]
    public void MultiSegmentParsingMatchesContiguousParsing(string fixtureName)
    {
        byte[]? carBytes = TryLoadFixture(fixtureName);
        if(carBytes is null)
        {
            //A real-data fixture, not a W3C conformance test: it comes from a
            //manual CAR download (see Fixtures/AtProto/README.md). When it is
            //not vendored there is nothing to validate and the probe passes.
            TestContext.WriteLine($"Real-data fixture {fixtureName} is not vendored; skipping (see Fixtures/AtProto/README.md).");
            return;
        }

        CarFileReader carReader = new(carBytes);
        carReader.ReadHeader();

        int blocksChecked = 0;
        while(carReader.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            if(block.Length < 8)
            {
                continue;
            }

            //Contiguous baseline parse.
            DagCborReader contig = new(block, strict: false);
            List<TreeMarker> contigTree = ConsumeIntoMarkers(contig);

            //Re-parse at four split offsets; trees must match exactly.
            foreach(int splitOffset in new[] { 1, block.Length / 4, block.Length / 2, block.Length - 1 })
            {
                ReadOnlySequence<byte> split = BuildTwoSegmentSequence(block, splitOffset);
                DagCborReader segReader = new(split, strict: false);
                List<TreeMarker> segTree = ConsumeIntoMarkers(segReader);

                AssertMarkersEqual(contigTree, segTree, splitOffset);
            }
            blocksChecked++;

            //Limit the test's runtime: 50 blocks is enough to exercise
            //the split-offset matrix without taking minutes.
            if(blocksChecked >= 50)
            {
                break;
            }
        }
    }

    //CARv1 files are typically thousands of bytes. Files smaller than
    //this threshold are treated as placeholders (Inconclusive).
    private const int MinPlausibleCarSize = 256;

    private static byte[]? TryLoadFixture(string name)
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory, "Cbor", "Fixtures", "AtProto", name);
        if(!File.Exists(fixturePath))
        {
            return null;
        }
        byte[] bytes = File.ReadAllBytes(fixturePath);
        return bytes.Length >= MinPlausibleCarSize ? bytes : null;
    }

    private static void ConsumeAllValues(DagCborReader reader)
    {
        ConsumeIntoMarkers(reader);
    }

    private static List<TreeMarker> ConsumeIntoMarkers(DagCborReader reader)
    {
        List<TreeMarker> markers = [];
        ConsumeValue(reader, markers);
        return markers;
    }

    private static void ConsumeValue(DagCborReader reader, List<TreeMarker> markers)
    {
        CborReaderState state = reader.PeekState();
        switch(state)
        {
            case CborReaderState.Null:
            {
                reader.ReadNull();
                markers.Add(new TreeMarker.NullLeaf());
                break;
            }
            case CborReaderState.Boolean:
            {
                markers.Add(new TreeMarker.BoolLeaf(reader.ReadBoolean()));
                break;
            }
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
            {
                markers.Add(new TreeMarker.IntLeaf(reader.ReadInt64()));
                break;
            }
            case CborReaderState.DoublePrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.HalfPrecisionFloat:
            {
                markers.Add(new TreeMarker.DoubleLeaf(reader.ReadDouble()));
                break;
            }
            case CborReaderState.TextString:
            {
                markers.Add(new TreeMarker.StringLeaf(reader.ReadTextString()));
                break;
            }
            case CborReaderState.ByteString:
            {
                markers.Add(new TreeMarker.BytesLeaf(reader.ReadByteStringMemory().ToArray()));
                break;
            }
            case CborReaderState.StartArray:
            {
                int count = reader.ReadStartArray();
                markers.Add(new TreeMarker.ArrayStart(count));
                for(int i = 0; i < count; i++)
                {
                    ConsumeValue(reader, markers);
                }
                reader.ReadEndArray();
                markers.Add(new TreeMarker.ArrayEnd());
                break;
            }
            case CborReaderState.StartMap:
            {
                int count = reader.ReadStartMap();
                markers.Add(new TreeMarker.MapStart(count));
                for(int i = 0; i < count; i++)
                {
                    string key = reader.ReadTextString();
                    markers.Add(new TreeMarker.MapKey(key));
                    ConsumeValue(reader, markers);
                }
                reader.ReadEndMap();
                markers.Add(new TreeMarker.MapEnd());
                break;
            }
            case CborReaderState.Tag:
            {
                CidValue cid = reader.ReadCid();
                markers.Add(new TreeMarker.CidLeaf(cid.ToString() ?? string.Empty));
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unexpected reader state: {state}");
            }
        }
    }

    private static void AssertMarkersEqual(List<TreeMarker> expected, List<TreeMarker> actual, int splitOffset)
    {
        Assert.HasCount(expected.Count, actual,
            $"Marker-count mismatch at split offset {splitOffset}");
        for(int i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i], actual[i],
                $"Marker mismatch at index {i} for split offset {splitOffset}");
        }
    }

    private static ReadOnlySequence<byte> BuildTwoSegmentSequence(ReadOnlyMemory<byte> data, int splitOffset)
    {
        byte[] first = data.Slice(0, splitOffset).ToArray();
        byte[] second = data.Slice(splitOffset).ToArray();

        MemorySegment firstSeg = new(first);
        MemorySegment secondSeg = firstSeg.Append(second);
        return new ReadOnlySequence<byte>(firstSeg, 0, secondSeg, secondSeg.Memory.Length);
    }

    private sealed class MemorySegment: ReadOnlySequenceSegment<byte>
    {
        public MemorySegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public MemorySegment Append(ReadOnlyMemory<byte> memory)
        {
            MemorySegment seg = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = seg;
            return seg;
        }
    }

    private abstract record TreeMarker
    {
        internal sealed record NullLeaf: TreeMarker;
        internal sealed record BoolLeaf(bool Value): TreeMarker;
        internal sealed record IntLeaf(long Value): TreeMarker;
        internal sealed record DoubleLeaf(double Value): TreeMarker;
        internal sealed record StringLeaf(string Value): TreeMarker;
        internal sealed record BytesLeaf(byte[] Value): TreeMarker
        {
            public bool Equals(BytesLeaf? other)
                => other is not null && Value.AsSpan().SequenceEqual(other.Value);

            public override int GetHashCode() => Value.Length;
        }
        internal sealed record CidLeaf(string Value): TreeMarker;
        internal sealed record ArrayStart(int Count): TreeMarker;
        internal sealed record ArrayEnd: TreeMarker;
        internal sealed record MapStart(int Count): TreeMarker;
        internal sealed record MapKey(string Key): TreeMarker;
        internal sealed record MapEnd: TreeMarker;
    }
}
