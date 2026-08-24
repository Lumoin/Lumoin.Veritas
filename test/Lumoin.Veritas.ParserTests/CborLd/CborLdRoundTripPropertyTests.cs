using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

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
    public async Task PassthroughRoundTripPreservesTreeStructure()
    {
        await InputTreeGenerator(maxDepth: 3).SampleAsync(async original =>
        {
            ArrayBufferWriter<byte> buffer = new();
            await CborLdEncoder.EncodeAsync(original, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);
            CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
                buffer.WrittenMemory,
                CborLdRegistry.Empty.AsDelegate(),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(0, result.RegistryEntryId);
            Assert.IsTrue(NodesEqual(original, result.Root));
        }, iter: Iterations).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DeterministicProfileEncodingIsStableUnderMapReordering()
    {
        //For any map node, two encodings produced from differently-ordered
        //but key-identical entry lists must produce identical bytes under
        //the Deterministic profile.
        await MapWithDuplicableEntriesGenerator().SampleAsync(async orderedPair =>
        {
            (CborLdInputMap a, CborLdInputMap b) = orderedPair;

            ArrayBufferWriter<byte> bufferA = new();
            await CborLdEncoder.EncodeAsync(a, CborLdRegistryEntry.Passthrough, CborLdProfile.Deterministic, bufferA).ConfigureAwait(false);
            ArrayBufferWriter<byte> bufferB = new();
            await CborLdEncoder.EncodeAsync(b, CborLdRegistryEntry.Passthrough, CborLdProfile.Deterministic, bufferB).ConfigureAwait(false);

            Assert.AreSequenceEqual(bufferA.WrittenSpan.ToArray(), bufferB.WrittenSpan.ToArray());
        }, iter: 1_000).ConfigureAwait(false);
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
                Dictionary<string, CborLdInputNode> firstByKey = new(entries.Length, StringComparer.Ordinal);
                List<KeyValuePair<string, CborLdInputNode>> deduped = new(entries.Length);
                foreach((string Key, CborLdInputNode Value) entry in entries)
                {
                    if(firstByKey.TryAdd(entry.Key, entry.Value))
                    {
                        deduped.Add(new KeyValuePair<string, CborLdInputNode>(entry.Key, entry.Value));
                    }
                }

                return (CborLdInputNode)new CborLdInputMap(deduped);
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
                Dictionary<string, CborLdInputNode> firstByKey = new(entries.Length, StringComparer.Ordinal);
                List<KeyValuePair<string, CborLdInputNode>> deduped = new(entries.Length);
                foreach((string Key, CborLdInputNode Value) entry in entries)
                {
                    if(firstByKey.TryAdd(entry.Key, entry.Value))
                    {
                        deduped.Add(new KeyValuePair<string, CborLdInputNode>(entry.Key, entry.Value));
                    }
                }

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
