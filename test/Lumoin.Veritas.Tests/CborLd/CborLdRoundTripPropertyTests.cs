using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.Tests.CborLd;

/// <summary>
/// CsCheck-driven round-trip property test for the CBOR-LD passthrough
/// encoder and decoder. For any randomly generated <see cref="CborLdInputNode"/>
/// tree, encoding then decoding produces a structurally equal tree.
/// </summary>
[TestClass]
internal sealed class CborLdRoundTripPropertyTests
{
    private const long Iterations = 10_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PassthroughRoundTripPreservesTreeStructure()
    {
        InputTreeGenerator(maxDepth: 3).Sample(original =>
        {
            ArrayBufferWriter<byte> buffer = new();
            CborLdEncoder.Encode(original, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer);
            CborLdDecodeResult result = CborLdDecoder.DecodeAsync(
                buffer.WrittenMemory,
                CborLdRegistry.Empty.AsDelegate(),
                TestContext.CancellationToken)
                .AsTask().GetAwaiter().GetResult();

            Assert.AreEqual(0, result.RegistryEntryId);
            Assert.IsTrue(NodesEqual(original, result.Root));
        }, iter: Iterations);
    }

    [TestMethod]
    public void DeterministicProfileEncodingIsStableUnderMapReordering()
    {
        //For any map node, two encodings produced from differently-ordered
        //but key-identical entry lists must produce identical bytes under
        //the Deterministic profile.
        MapWithDuplicableEntriesGenerator().Sample(orderedPair =>
        {
            (CborLdInputMap a, CborLdInputMap b) = orderedPair;

            ArrayBufferWriter<byte> bufferA = new();
            CborLdEncoder.Encode(a, CborLdRegistryEntry.Passthrough, CborLdProfile.Deterministic, bufferA);
            ArrayBufferWriter<byte> bufferB = new();
            CborLdEncoder.Encode(b, CborLdRegistryEntry.Passthrough, CborLdProfile.Deterministic, bufferB);

            CollectionAssert.AreEqual(bufferA.WrittenSpan.ToArray(), bufferB.WrittenSpan.ToArray());
        }, iter: 1_000);
    }

    private static Gen<CborLdInputNode> InputTreeGenerator(int maxDepth)
    {
        Gen<CborLdInputNode> primitive = PrimitiveGenerator();
        if(maxDepth <= 0)
        {
            return primitive;
        }

        Gen<CborLdInputNode> child = InputTreeGenerator(maxDepth - 1);

        Gen<CborLdInputNode> arrayGen = child.Array[0, 5]
            .Select(items => (CborLdInputNode)new CborLdInputArray(items));

        Gen<CborLdInputNode> mapGen =
            (from k in AsciiKeyGenerator() from v in child select (Key: k, Value: v))
            .Array[0, 5]
            .Select(entries =>
            {
                IEnumerable<KeyValuePair<string, CborLdInputNode>> deduped = entries
                    .GroupBy(e => e.Key)
                    .Select(g => new KeyValuePair<string, CborLdInputNode>(g.Key, g.First().Value));
                return (CborLdInputNode)new CborLdInputMap(deduped.ToList());
            });

        return Gen.Int[0, 6].SelectMany(i => i switch
        {
            0 or 1 or 2 or 3 => primitive,
            4 or 5 => arrayGen,
            _ => mapGen
        });
    }

    private static Gen<CborLdInputNode> PrimitiveGenerator()
    {
        Gen<CborLdInputNode> intGen = Gen.Long.Select(l => (CborLdInputNode)new CborLdInputInt(l));
        Gen<CborLdInputNode> textGen = AsciiKeyGenerator().Select(s => (CborLdInputNode)new CborLdInputString(s));
        Gen<CborLdInputNode> boolGen = Gen.Bool.Select(b => (CborLdInputNode)new CborLdInputBool(b));
        Gen<CborLdInputNode> nullGen = Gen.Const((CborLdInputNode)CborLdInputNull.Instance);

        return Gen.Int[0, 3].SelectMany(i => i switch
        {
            0 => intGen,
            1 => textGen,
            2 => boolGen,
            _ => nullGen
        });
    }

    private static Gen<string> AsciiKeyGenerator()
    {
        return Gen.Int[(int)'a', (int)'z'].Select(i => (char)i).Array[1, 6].Select(chars => new string(chars));
    }

    private static Gen<(CborLdInputMap A, CborLdInputMap B)> MapWithDuplicableEntriesGenerator()
    {
        return (from k in AsciiKeyGenerator() from v in PrimitiveGenerator() select (Key: k, Value: v))
            .Array[0, 8]
            .Select(entries =>
            {
                List<KeyValuePair<string, CborLdInputNode>> deduped = entries
                    .GroupBy(e => e.Key)
                    .Select(g => new KeyValuePair<string, CborLdInputNode>(g.Key, g.First().Value))
                    .ToList();
                List<KeyValuePair<string, CborLdInputNode>> reversed = [..deduped];
                reversed.Reverse();
                return (new CborLdInputMap(deduped), new CborLdInputMap(reversed));
            });
    }

    private static bool NodesEqual(CborLdInputNode left, CborLdInputNode right)
    {
        Stack<(CborLdInputNode L, CborLdInputNode R)> work = new();
        work.Push((left, right));
        while(work.Count > 0)
        {
            (CborLdInputNode l, CborLdInputNode r) = work.Pop();
            switch(l, r)
            {
                case (CborLdInputNull, CborLdInputNull):
                {
                    break;
                }
                case (CborLdInputBool lb, CborLdInputBool rb):
                {
                    if(lb.Value != rb.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (CborLdInputInt li, CborLdInputInt ri):
                {
                    if(li.Value != ri.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (CborLdInputDouble ld, CborLdInputDouble rd):
                {
                    if(ld.Value != rd.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (CborLdInputString ls, CborLdInputString rs):
                {
                    if(ls.Value != rs.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (CborLdInputArray la, CborLdInputArray ra):
                {
                    if(la.Items.Count != ra.Items.Count)
                    {
                        return false;
                    }
                    for(int i = 0; i < la.Items.Count; i++)
                    {
                        work.Push((la.Items[i], ra.Items[i]));
                    }
                    break;
                }
                case (CborLdInputMap lm, CborLdInputMap rm):
                {
                    if(lm.Entries.Count != rm.Entries.Count)
                    {
                        return false;
                    }
                    for(int i = 0; i < lm.Entries.Count; i++)
                    {
                        if(lm.Entries[i].Key != rm.Entries[i].Key)
                        {
                            return false;
                        }
                        work.Push((lm.Entries[i].Value, rm.Entries[i].Value));
                    }
                    break;
                }
                default:
                {
                    return false;
                }
            }
        }
        return true;
    }
}
