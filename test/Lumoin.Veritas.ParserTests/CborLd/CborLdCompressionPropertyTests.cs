using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// CsCheck-driven round-trip property test for the CBOR-LD compression
/// pipeline. For any randomly generated <see cref="CborLdInputNode"/>
/// tree paired with a randomly generated registry entry's term map,
/// encoding then decoding through the compression path produces a
/// structurally equal tree.
/// </summary>
[TestClass]
internal sealed class CborLdCompressionPropertyTests
{
    private const long Iterations = 10_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CompressionRoundTripPreservesTreeStructure()
    {
        Gen<(CborLdInputNode Tree, CborLdRegistryEntry Entry)> testCase =
            from termNames in TermNameGenerator()
            from tree in TreeGenerator(termNames, maxDepth: 3)
            select (Tree: tree, Entry: BuildRegistryEntry(termNames));

        await testCase.SampleAsync(async pair =>
        {
            (CborLdInputNode original, CborLdRegistryEntry entry) = pair;

            ArrayBufferWriter<byte> buffer = new();
            await CborLdEncoder.EncodeAsync(original, entry, CborLdProfile.Default, buffer).ConfigureAwait(false);

            LoadCborLdRegistryEntryDelegate loader = (id, ct) =>
                ValueTask.FromResult<CborLdRegistryEntry?>(id == entry.RegistryEntryId ? entry : null);

            CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
                buffer.WrittenMemory,
                loader,
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(entry.RegistryEntryId, result.RegistryEntryId);
            Assert.IsTrue(NodesEqual(original, result.Root));
        }, iter: Iterations).ConfigureAwait(false);
    }

    private static Gen<List<string>> TermNameGenerator()
    {
        return AsciiKeyGenerator().Array[3, 7].Select(arr =>
        {
            HashSet<string> seen = new(arr.Length, StringComparer.Ordinal);
            List<string> distinct = new(arr.Length);
            foreach(string s in arr)
            {
                if(seen.Add(s))
                {
                    distinct.Add(s);
                }
            }

            return distinct;
        });
    }

    private static CborLdRegistryEntry BuildRegistryEntry(List<string> termNames)
    {
        Dictionary<string, CborLdTermCodec> terms = new(termNames.Count);
        int nextId = 100;
        foreach(string term in termNames)
        {
            terms[term] = new CborLdTermCodec(term, nextId);
            nextId += 2;
        }
        return new CborLdRegistryEntry(
            registryEntryId: 1,
            keywords: new Dictionary<string, CborLdKeywordCodec>(),
            terms: terms);
    }

    private static Gen<CborLdInputNode> TreeGenerator(List<string> registeredTerms, int maxDepth)
    {
        Gen<CborLdInputNode> primitive = PrimitiveGenerator();
        if(maxDepth <= 0)
        {
            return primitive;
        }

        Gen<CborLdInputNode> child = TreeGenerator(registeredTerms, maxDepth - 1);

        Gen<CborLdInputNode> arrayGen = child.Array[0, 4]
            .Select(items => (CborLdInputNode)new CborLdInputArray(items));

        Gen<string> keyGen = Gen.Int[0, 4].SelectMany(i =>
            i < 3 && registeredTerms.Count > 0
                ? Gen.OneOfConst(registeredTerms.ToArray())
                : AsciiKeyGenerator());

        Gen<CborLdInputNode> mapGen =
            (from k in keyGen from v in child select (Key: k, Value: v))
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
