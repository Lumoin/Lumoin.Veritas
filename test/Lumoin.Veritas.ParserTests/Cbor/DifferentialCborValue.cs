using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Shared sum type describing the CBOR-value grammar both the writer and
/// reader differential property tests sample over. The grammar is limited
/// to data shapes both this project and the BCL <c>System.Formats.Cbor</c>
/// implementation encode/decode consistently under canonical and CTAP2
/// canonical conformance modes: integers, text strings, byte strings,
/// booleans, null, arrays, and string-keyed maps. Doubles are excluded
/// because BCL canonical mode's float reduction follows rules subtly
/// different from this writer's; that gap is tracked separately.
/// </summary>
internal abstract record DifferentialCborValue;

internal sealed record DifferentialCborInt(long Value): DifferentialCborValue;
internal sealed record DifferentialCborText(string Value): DifferentialCborValue;
internal sealed record DifferentialCborBytes(byte[] Value): DifferentialCborValue;
internal sealed record DifferentialCborBool(bool Value): DifferentialCborValue;
internal sealed record DifferentialCborNull: DifferentialCborValue;
internal sealed record DifferentialCborArray(DifferentialCborValue[] Items): DifferentialCborValue;
internal sealed record DifferentialCborStringMap((string Key, DifferentialCborValue Value)[] Entries): DifferentialCborValue;

internal static class DifferentialCborValueGenerator
{
    /// <summary>
    /// Returns a <see cref="Gen{T}"/> over <see cref="DifferentialCborValue"/>
    /// trees of at most <paramref name="maxDepth"/> nesting levels.
    /// </summary>
    /// <param name="maxDepth">The maximum container nesting depth. Zero produces leaves only.</param>
    public static Gen<DifferentialCborValue> ValueGenerator(int maxDepth)
    {
        Gen<DifferentialCborValue> primitive = PrimitiveGenerator();
        if(maxDepth <= 0)
        {
            return primitive;
        }

        Gen<DifferentialCborValue> child = ValueGenerator(maxDepth - 1);

        Gen<DifferentialCborValue> arrayGen = child.Array[0, 6].Select(items => (DifferentialCborValue)new DifferentialCborArray(items));
        Gen<DifferentialCborValue> mapGen =
            (from k in AsciiKeyGenerator() from v in child select (Key: k, Value: v))
            .Array[0, 6]
            .Select(entries =>
            {
                //Deduplicate by key; BCL Ctap2Canonical rejects duplicate keys.
                Dictionary<string, DifferentialCborValue> firstByKey = new(entries.Length, StringComparer.Ordinal);
                List<(string Key, DifferentialCborValue Value)> dedupedList = new(entries.Length);
                foreach((string Key, DifferentialCborValue Value) entry in entries)
                {
                    if(firstByKey.TryAdd(entry.Key, entry.Value))
                    {
                        dedupedList.Add(entry);
                    }
                }

                (string Key, DifferentialCborValue Value)[] deduped = new (string Key, DifferentialCborValue Value)[dedupedList.Count];
                dedupedList.CopyTo(deduped);
                return (DifferentialCborValue)new DifferentialCborStringMap(deduped);
            });

        return Gen.Int[0, 7].SelectMany(i => i switch
        {
            0 or 1 or 2 or 3 or 4 => primitive,
            5 or 6 => arrayGen,
            _ => mapGen
        });
    }

    private static Gen<DifferentialCborValue> PrimitiveGenerator()
    {
        //Doubles are excluded from the grammar. See file header.
        Gen<DifferentialCborValue> intGen = Gen.Long.Select(l => (DifferentialCborValue)new DifferentialCborInt(l));
        Gen<DifferentialCborValue> textGen = AsciiKeyGenerator().Select(s => (DifferentialCborValue)new DifferentialCborText(s));
        Gen<DifferentialCborValue> bytesGen = Gen.Int[0, 255].Select(i => (byte)i).Array[0, 16].Select(b => (DifferentialCborValue)new DifferentialCborBytes(b));
        Gen<DifferentialCborValue> boolGen = Gen.Bool.Select(b => (DifferentialCborValue)new DifferentialCborBool(b));
        Gen<DifferentialCborValue> nullGen = Gen.Const((DifferentialCborValue)new DifferentialCborNull());

        return Gen.Int[0, 4].SelectMany(i => i switch
        {
            0 => intGen,
            1 => textGen,
            2 => bytesGen,
            3 => boolGen,
            _ => nullGen
        });
    }

    private static Gen<string> AsciiKeyGenerator()
    {
        //Lowercase ASCII keys, length 0..8. Avoids escape-sequence concerns
        //and keeps key sorting bytewise-meaningful.
        return Gen.Int[(int)'a', (int)'z'].Select(i => (char)i).Array[0, 8].Select(chars => new string(chars));
    }
}

/// <summary>
/// Helper for comparing two <see cref="DifferentialCborValue"/> trees by
/// structural value. Records' synthesised equality would treat byte arrays
/// and array-of-values fields as reference-equal, which is not what the
/// differential tests want; this helper performs the necessary deep
/// comparison via an explicit stack to honour the project's no-recursion
/// rule even in test code.
/// </summary>
internal static class DifferentialCborValueComparer
{
    public static bool AreEqual(DifferentialCborValue left, DifferentialCborValue right)
    {
        System.Collections.Generic.Stack<(DifferentialCborValue Left, DifferentialCborValue Right)> work = new();
        work.Push((left, right));
        while(work.Count > 0)
        {
            (DifferentialCborValue l, DifferentialCborValue r) = work.Pop();
            switch(l, r)
            {
                case (DifferentialCborInt li, DifferentialCborInt ri):
                {
                    if(li.Value != ri.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (DifferentialCborText lt, DifferentialCborText rt):
                {
                    if(lt.Value != rt.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (DifferentialCborBytes lb, DifferentialCborBytes rb):
                {
                    if(!lb.Value.AsSpan().SequenceEqual(rb.Value))
                    {
                        return false;
                    }
                    break;
                }
                case (DifferentialCborBool lbool, DifferentialCborBool rbool):
                {
                    if(lbool.Value != rbool.Value)
                    {
                        return false;
                    }
                    break;
                }
                case (DifferentialCborNull, DifferentialCborNull):
                {
                    break;
                }
                case (DifferentialCborArray la, DifferentialCborArray ra):
                {
                    if(la.Items.Length != ra.Items.Length)
                    {
                        return false;
                    }
                    for(int i = 0; i < la.Items.Length; i++)
                    {
                        work.Push((la.Items[i], ra.Items[i]));
                    }
                    break;
                }
                case (DifferentialCborStringMap lm, DifferentialCborStringMap rm):
                {
                    if(lm.Entries.Length != rm.Entries.Length)
                    {
                        return false;
                    }
                    for(int i = 0; i < lm.Entries.Length; i++)
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
