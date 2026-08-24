using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.DagCbor;

namespace Lumoin.Veritas.ParserTests.Cbor.DagCbor;

/// <summary>
/// CsCheck-driven round-trip property test for DAG-CBOR. For any
/// randomly generated DAG-CBOR-shape tree (string-keyed maps, arrays,
/// primitives, finite floats), encoding under strict
/// <see cref="DagCborWriter"/> and decoding under strict
/// <see cref="DagCborReader"/> reproduces the same tree.
/// </summary>
[TestClass]
internal sealed class DagCborRoundTripPropertyTests
{
    private const long Iterations = 10_000;

    [TestMethod]
    public void StrictRoundTripPreservesTreeStructure()
    {
        TreeGenerator(maxDepth: 3).Sample(original =>
        {
            ArrayBufferWriter<byte> buffer = new();
            DagCborWriter writer = new(buffer);
            EmitTree(writer, original);

            DagCborReader reader = new(buffer.WrittenMemory, strict: true);
            Node read = ReadTree(reader);

            Assert.IsTrue(TreesEqual(original, read));
        }, iter: Iterations);
    }

    private static void EmitTree(DagCborWriter writer, Node node)
    {
        switch(node)
        {
            case NullNode:
            {
                writer.WriteNull();
                break;
            }
            case BoolNode b:
            {
                writer.WriteBoolean(b.Value);
                break;
            }
            case IntNode i:
            {
                writer.WriteInt64(i.Value);
                break;
            }
            case DoubleNode d:
            {
                writer.WriteDouble(d.Value);
                break;
            }
            case StringNode s:
            {
                writer.WriteTextString(s.Value);
                break;
            }
            case BytesNode by:
            {
                writer.WriteByteString(by.Value);
                break;
            }
            case ArrayNode a:
            {
                writer.WriteStartArray(a.Items.Count);
                foreach(Node item in a.Items)
                {
                    EmitTree(writer, item);
                }
                writer.WriteEndArray();
                break;
            }
            case MapNode m:
            {
                writer.WriteStartMap(m.Entries.Count);
                foreach(KeyValuePair<string, Node> entry in m.Entries)
                {
                    writer.WriteTextString(entry.Key);
                    EmitTree(writer, entry.Value);
                }
                writer.WriteEndMap();
                break;
            }
        }
    }

    private static Node ReadTree(DagCborReader reader)
    {
        CborReaderState state = reader.PeekState();
        switch(state)
        {
            case CborReaderState.Null:
            {
                reader.ReadNull();
                return NullNode.Instance;
            }
            case CborReaderState.Boolean:
            {
                return new BoolNode(reader.ReadBoolean());
            }
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
            {
                return new IntNode(reader.ReadInt64());
            }
            case CborReaderState.DoublePrecisionFloat:
            {
                return new DoubleNode(reader.ReadDouble());
            }
            case CborReaderState.TextString:
            {
                return new StringNode(reader.ReadTextString());
            }
            case CborReaderState.ByteString:
            {
                return new BytesNode(reader.ReadByteStringSpan().ToArray());
            }
            case CborReaderState.StartArray:
            {
                int count = reader.ReadStartArray();
                List<Node> items = new(count);
                for(int i = 0; i < count; i++)
                {
                    items.Add(ReadTree(reader));
                }
                reader.ReadEndArray();
                return new ArrayNode(items);
            }
            case CborReaderState.StartMap:
            {
                int count = reader.ReadStartMap();
                List<KeyValuePair<string, Node>> entries = new(count);
                for(int i = 0; i < count; i++)
                {
                    string key = reader.ReadTextString();
                    Node value = ReadTree(reader);
                    entries.Add(new KeyValuePair<string, Node>(key, value));
                }
                reader.ReadEndMap();
                return new MapNode(entries);
            }
            default:
            {
                throw new System.InvalidOperationException($"Unexpected reader state {state}");
            }
        }
    }

    private static Gen<Node> TreeGenerator(int maxDepth)
    {
        Gen<Node> primitive = PrimitiveGenerator();
        if(maxDepth <= 0)
        {
            return primitive;
        }

        Gen<Node> child = TreeGenerator(maxDepth - 1);

        Gen<Node> arrayGen = child.Array[0, 4]
            .Select(items =>
            {
                List<Node> nodes = new(items.Length);
                foreach(Node node in items)
                {
                    nodes.Add(node);
                }

                return (Node)new ArrayNode(nodes);
            });

        Gen<Node> mapGen =
            (from k in AsciiKeyGenerator() from v in child select (Key: k, Value: v))
            .Array[0, 4]
            .Select(entries =>
            {
                //Deduplicate by key; the writer's canonical mode sorts and rejects duplicates.
                Dictionary<string, Node> firstByKey = new(entries.Length, System.StringComparer.Ordinal);
                List<KeyValuePair<string, Node>> deduped = new(entries.Length);
                foreach((string Key, Node Value) entry in entries)
                {
                    if(firstByKey.TryAdd(entry.Key, entry.Value))
                    {
                        deduped.Add(new KeyValuePair<string, Node>(entry.Key, entry.Value));
                    }
                }

                return (Node)new MapNode(deduped);
            });

        return Gen.Int[0, 6].SelectMany(i => i switch
        {
            0 or 1 or 2 or 3 => primitive,
            4 or 5 => arrayGen,
            _ => mapGen
        });
    }

    private static Gen<Node> PrimitiveGenerator()
    {
        //DAG-CBOR rule 5: no NaN, no infinities. Filter doubles to finite range.
        Gen<Node> intGen = Gen.Long.Select(l => (Node)new IntNode(l));
        Gen<Node> doubleGen = Gen.Double[-1e6, 1e6]
            .Where(d => !double.IsNaN(d) && !double.IsInfinity(d))
            .Select(d => (Node)new DoubleNode(d));
        Gen<Node> textGen = AsciiKeyGenerator().Select(s => (Node)new StringNode(s));
        Gen<Node> bytesGen = Gen.Byte.Array[0, 8].Select(arr => (Node)new BytesNode(arr));
        Gen<Node> boolGen = Gen.Bool.Select(b => (Node)new BoolNode(b));
        Gen<Node> nullGen = Gen.Const((Node)NullNode.Instance);

        return Gen.Int[0, 5].SelectMany(i => i switch
        {
            0 => intGen,
            1 => textGen,
            2 => boolGen,
            3 => nullGen,
            4 => bytesGen,
            _ => doubleGen
        });
    }

    private static Gen<string> AsciiKeyGenerator()
    {
        return Gen.Int[(int)'a', (int)'z'].Select(i => (char)i).Array[1, 6].Select(chars => new string(chars));
    }

    private static bool TreesEqual(Node left, Node right)
    {
        return (left, right) switch
        {
            (NullNode, NullNode) => true,
            (BoolNode lb, BoolNode rb) => lb.Value == rb.Value,
            (IntNode li, IntNode ri) => li.Value == ri.Value,
            (DoubleNode ld, DoubleNode rd) => ld.Value == rd.Value,
            (StringNode ls, StringNode rs) => ls.Value == rs.Value,
            (BytesNode lby, BytesNode rby) => lby.Value.SequenceEqual(rby.Value),
            (ArrayNode la, ArrayNode ra) when la.Items.Count == ra.Items.Count =>
                la.Items.Zip(ra.Items).All(pair => TreesEqual(pair.First, pair.Second)),
            (MapNode lm, MapNode rm) when lm.Entries.Count == rm.Entries.Count =>
                MapEntriesEqual(lm.Entries, rm.Entries),
            _ => false
        };
    }

    private static bool MapEntriesEqual(
        IReadOnlyList<KeyValuePair<string, Node>> left,
        IReadOnlyList<KeyValuePair<string, Node>> right)
    {
        //DAG-CBOR sorts maps by length-first lexical key order. Compare
        //sorted views so generator-side input order doesn't matter.
        IOrderedEnumerable<KeyValuePair<string, Node>> sortLeft = left
            .OrderBy(e => e.Key.Length).ThenBy(e => e.Key, System.StringComparer.Ordinal);
        IOrderedEnumerable<KeyValuePair<string, Node>> sortRight = right
            .OrderBy(e => e.Key.Length).ThenBy(e => e.Key, System.StringComparer.Ordinal);
        return sortLeft.Zip(sortRight).All(pair =>
            pair.First.Key == pair.Second.Key && TreesEqual(pair.First.Value, pair.Second.Value));
    }

    private abstract record Node;
    private sealed record NullNode: Node { public static NullNode Instance { get; } = new(); }
    private sealed record BoolNode(bool Value): Node;
    private sealed record IntNode(long Value): Node;
    private sealed record DoubleNode(double Value): Node;
    private sealed record StringNode(string Value): Node;
    private sealed record BytesNode(byte[] Value): Node;
    private sealed record ArrayNode(List<Node> Items): Node;
    private sealed record MapNode(List<KeyValuePair<string, Node>> Entries): Node;
}
