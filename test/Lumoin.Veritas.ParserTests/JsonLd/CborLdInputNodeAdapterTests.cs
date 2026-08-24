using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.JsonLd.Adapters;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.JsonLd;

[TestClass]
internal sealed class CborLdInputNodeAdapterTests
{
    private static string[] AbcArray { get; } = ["a", "b", "c"];
    private static string[] NameAgeArray { get; } = ["name", "age"];
    private static string[] OneTwoArray { get; } = ["1", "2"];

    [TestMethod]
    public void PrimitiveStringRoundTrip()
    {
        JsonNode node = ParseJson("\"hello\"");
        CborLdInputNode converted = CborLdInputNodeAdapter.FromJsonLd(node);
        Assert.IsInstanceOfType<CborLdInputString>(converted);
        Assert.AreEqual("hello", ((CborLdInputString)converted).Value);
    }

    [TestMethod]
    public void PrimitiveBooleanAndNull()
    {
        CborLdInputNode trueNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("true"));
        CborLdInputNode falseNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("false"));
        CborLdInputNode nullNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("null"));

        Assert.IsTrue(((CborLdInputBool)trueNode).Value);
        Assert.IsFalse(((CborLdInputBool)falseNode).Value);
        Assert.IsInstanceOfType<CborLdInputNull>(nullNode);
    }

    [TestMethod]
    public void NumberDisambiguation()
    {
        //Integer-shaped JSON number becomes CborLdInputInt.
        CborLdInputNode intNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("42"));
        Assert.IsInstanceOfType<CborLdInputInt>(intNode);
        Assert.AreEqual(42L, ((CborLdInputInt)intNode).Value);

        //Number with fractional part becomes CborLdInputDouble.
        CborLdInputNode doubleNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("3.14"));
        Assert.IsInstanceOfType<CborLdInputDouble>(doubleNode);
        Assert.AreEqual(3.14, ((CborLdInputDouble)doubleNode).Value);

        //Number with exponent also becomes CborLdInputDouble.
        CborLdInputNode expNode = CborLdInputNodeAdapter.FromJsonLd(ParseJson("1e3"));
        Assert.IsInstanceOfType<CborLdInputDouble>(expNode);
    }

    [TestMethod]
    public void ArrayElementOrderPreserved()
    {
        CborLdInputNode root = CborLdInputNodeAdapter.FromJsonLd(ParseJson("[\"a\", \"b\", \"c\"]"));
        CborLdInputArray arr = (CborLdInputArray)root;
        Assert.HasCount(3, arr.Items);
        Assert.AreEqual("a", ((CborLdInputString)arr.Items[0]).Value);
        Assert.AreEqual("b", ((CborLdInputString)arr.Items[1]).Value);
        Assert.AreEqual("c", ((CborLdInputString)arr.Items[2]).Value);
    }

    [TestMethod]
    public void NestedObjectStructureRoundTrip()
    {
        const string json = /*lang=json,strict*/ """
            {
                "name": "Alice",
                "age": 30,
                "tags": ["one", "two"],
                "address": {"city": "Helsinki"}
            }
            """;
        CborLdInputNode root = CborLdInputNodeAdapter.FromJsonLd(ParseJson(json));
        CborLdInputMap map = (CborLdInputMap)root;
        Assert.HasCount(4, map.Entries);
        Assert.AreEqual("name", map.Entries[0].Key);
        Assert.AreEqual("Alice", ((CborLdInputString)map.Entries[0].Value).Value);
        Assert.AreEqual("age", map.Entries[1].Key);
        Assert.AreEqual(30L, ((CborLdInputInt)map.Entries[1].Value).Value);
        CborLdInputArray tags = (CborLdInputArray)map.Entries[2].Value;
        Assert.HasCount(2, tags.Items);
        CborLdInputMap address = (CborLdInputMap)map.Entries[3].Value;
        Assert.AreEqual("Helsinki", ((CborLdInputString)address.Entries[0].Value).Value);
    }

    [TestMethod]
    public void ToJsonLdExposesPrimitiveKinds()
    {
        Assert.AreEqual(JsonNodeKind.Null, CborLdInputNodeAdapter.ToJsonLd(CborLdInputNull.Instance).Kind);
        Assert.AreEqual(JsonNodeKind.True, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputBool(true)).Kind);
        Assert.AreEqual(JsonNodeKind.False, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputBool(false)).Kind);
        Assert.AreEqual(JsonNodeKind.String, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputString("hi")).Kind);
        Assert.AreEqual(JsonNodeKind.Number, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputInt(42)).Kind);
        Assert.AreEqual(JsonNodeKind.Number, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputDouble(3.14)).Kind);
        Assert.AreEqual(JsonNodeKind.Array, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputArray(System.Array.Empty<CborLdInputNode>())).Kind);
        Assert.AreEqual(JsonNodeKind.Object, CborLdInputNodeAdapter.ToJsonLd(new CborLdInputMap(System.Array.Empty<KeyValuePair<string, CborLdInputNode>>())).Kind);
    }

    [TestMethod]
    public void ToJsonLdNumberKindRetainsIntegerForm()
    {
        //CborLdInputInt round-trips through GetRawNumber as an integer-shaped
        //lexical form so a consumer can re-classify it as Int64-compatible.
        JsonNode node = CborLdInputNodeAdapter.ToJsonLd(new CborLdInputInt(42));
        Assert.AreEqual("42", node.GetRawNumber());

        JsonNode big = CborLdInputNodeAdapter.ToJsonLd(new CborLdInputInt(long.MaxValue));
        Assert.AreEqual(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), big.GetRawNumber());
    }

    [TestMethod]
    public void ToJsonLdNumberKindPreservesDoubleForm()
    {
        //Round-trip ("R") format ensures the double reparses to the same value.
        JsonNode node = CborLdInputNodeAdapter.ToJsonLd(new CborLdInputDouble(3.14));
        string raw = node.GetRawNumber();
        Assert.AreEqual(3.14, double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ToJsonLdEnumerateArrayYieldsItems()
    {
        CborLdInputArray array = new(new CborLdInputNode[]
        {
            new CborLdInputString("a"),
            new CborLdInputString("b"),
            new CborLdInputString("c")
        });

        JsonNode root = CborLdInputNodeAdapter.ToJsonLd(array);
        Assert.AreEqual(JsonNodeKind.Array, root.Kind);
        List<string> collected = new();
        foreach(JsonNode item in root.EnumerateArray())
        {
            collected.Add(item.GetString());
        }
        Assert.AreSequenceEqual(AbcArray, collected);
    }

    [TestMethod]
    public void ToJsonLdEnumerateObjectYieldsEntries()
    {
        CborLdInputMap map = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice")),
            new KeyValuePair<string, CborLdInputNode>("age", new CborLdInputInt(30))
        });

        JsonNode root = CborLdInputNodeAdapter.ToJsonLd(map);
        Assert.AreEqual(JsonNodeKind.Object, root.Kind);

        List<string> keys = new();
        foreach(KeyValuePair<string, JsonNode> entry in root.EnumerateObject())
        {
            keys.Add(entry.Key);
        }
        Assert.AreSequenceEqual(NameAgeArray, keys);

        Assert.IsTrue(root.TryGetProperty("name", out JsonNode nameNode));
        Assert.AreEqual("Alice", nameNode.GetString());
        Assert.IsTrue(root.TryGetProperty("age", out JsonNode ageNode));
        Assert.AreEqual("30", ageNode.GetRawNumber());
        Assert.IsFalse(root.TryGetProperty("missing", out _));
    }

    [TestMethod]
    public void ToJsonLdOnByteStringThrowsOnNavigation()
    {
        //Byte strings have no JSON equivalent; navigation raises a clear
        //exception. The ToJsonLd call itself succeeds (handle is wrapped)
        //but the first Kind query fails.
        JsonNode wrapped = CborLdInputNodeAdapter.ToJsonLd(new CborLdInputBytes(new byte[] { 1, 2, 3 }));
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = wrapped.Kind);
    }

    [TestMethod]
    public void RoundTripJsonLdThroughCborLdAndBackPreservesStructure()
    {
        //Full round-trip: JSON string → JsonLd → CborLd → JsonLd → CborLd
        //and verify the second CborLd matches the first. Uses FromJsonLd
        //as the comparison primitive against the post-ToJsonLd tree.
        const string json = /*lang=json,strict*/ """
            {
                "@id": "urn:example:1",
                "name": "Alice",
                "age": 30,
                "active": true,
                "score": 3.14,
                "tags": ["alpha", "beta"],
                "nested": {"k": null}
            }
            """;

        CborLdInputNode first = CborLdInputNodeAdapter.FromJsonLd(ParseJson(json));
        JsonNode reExposed = CborLdInputNodeAdapter.ToJsonLd(first);
        CborLdInputNode second = CborLdInputNodeAdapter.FromJsonLd(reExposed);

        AssertStructurallyEqual(first, second);
    }

    [TestMethod]
    public void RoundTripCborLdConstructedTreePreservesValues()
    {
        //Build a CborLd tree directly (no JSON parse), expose as JsonLd,
        //walk and read each leaf to verify the navigator surfaces values
        //correctly across the type lattice.
        CborLdInputMap root = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("urn:test")),
            new KeyValuePair<string, CborLdInputNode>("count", new CborLdInputInt(7)),
            new KeyValuePair<string, CborLdInputNode>("ratio", new CborLdInputDouble(0.5)),
            new KeyValuePair<string, CborLdInputNode>("present", new CborLdInputBool(true)),
            new KeyValuePair<string, CborLdInputNode>("absent", CborLdInputNull.Instance),
            new KeyValuePair<string, CborLdInputNode>("items", new CborLdInputArray(new CborLdInputNode[]
            {
                new CborLdInputInt(1),
                new CborLdInputInt(2)
            }))
        });

        JsonNode rootJson = CborLdInputNodeAdapter.ToJsonLd(root);
        Assert.AreEqual(JsonNodeKind.Object, rootJson.Kind);

        Assert.IsTrue(rootJson.TryGetProperty("@id", out JsonNode idNode));
        Assert.AreEqual("urn:test", idNode.GetString());

        Assert.IsTrue(rootJson.TryGetProperty("count", out JsonNode countNode));
        Assert.AreEqual("7", countNode.GetRawNumber());

        Assert.IsTrue(rootJson.TryGetProperty("ratio", out JsonNode ratioNode));
        Assert.AreEqual(0.5, double.Parse(ratioNode.GetRawNumber(), System.Globalization.CultureInfo.InvariantCulture));

        Assert.IsTrue(rootJson.TryGetProperty("present", out JsonNode presentNode));
        Assert.AreEqual(JsonNodeKind.True, presentNode.Kind);
        Assert.IsTrue(presentNode.GetBoolean());

        Assert.IsTrue(rootJson.TryGetProperty("absent", out JsonNode absentNode));
        Assert.AreEqual(JsonNodeKind.Null, absentNode.Kind);

        Assert.IsTrue(rootJson.TryGetProperty("items", out JsonNode itemsNode));
        Assert.AreEqual(JsonNodeKind.Array, itemsNode.Kind);
        List<string> rawNumbers = new();
        foreach(JsonNode item in itemsNode.EnumerateArray())
        {
            rawNumbers.Add(item.GetRawNumber());
        }
        Assert.AreSequenceEqual(OneTwoArray, rawNumbers);
    }

    [TestMethod]
    public void ToJsonLdCloneReturnsEquivalentHandle()
    {
        //CborLdInputNode is independent storage; the navigator's Clone is
        //identity over the same handle. The resulting node is still
        //navigable through the same navigator.
        CborLdInputString original = new("hello");
        JsonNode wrapped = CborLdInputNodeAdapter.ToJsonLd(original);
        JsonNode cloned = wrapped.Clone();

        Assert.AreEqual(JsonNodeKind.String, cloned.Kind);
        Assert.AreEqual("hello", cloned.GetString());
    }

    /// <summary>
    /// Recursive structural equality over <see cref="CborLdInputNode"/>
    /// trees. Used to verify a round-trip JsonLd → CborLd → JsonLd → CborLd
    /// produces an equal tree.
    /// </summary>
    private static void AssertStructurallyEqual(CborLdInputNode a, CborLdInputNode b)
    {
        switch(a)
        {
            case CborLdInputNull:
            {
                Assert.IsInstanceOfType<CborLdInputNull>(b);
                break;
            }
            case CborLdInputBool ab:
            {
                Assert.AreEqual(ab.Value, ((CborLdInputBool)b).Value);
                break;
            }
            case CborLdInputInt ai:
            {
                Assert.AreEqual(ai.Value, ((CborLdInputInt)b).Value);
                break;
            }
            case CborLdInputDouble ad:
            {
                Assert.AreEqual(ad.Value, ((CborLdInputDouble)b).Value);
                break;
            }
            case CborLdInputString astr:
            {
                Assert.AreEqual(astr.Value, ((CborLdInputString)b).Value);
                break;
            }
            case CborLdInputArray arr:
            {
                CborLdInputArray brr = (CborLdInputArray)b;
                Assert.HasCount(arr.Items.Count, brr.Items);
                for(int i = 0; i < arr.Items.Count; i++)
                {
                    AssertStructurallyEqual(arr.Items[i], brr.Items[i]);
                }
                break;
            }
            case CborLdInputMap am:
            {
                CborLdInputMap bm = (CborLdInputMap)b;
                Assert.HasCount(am.Entries.Count, bm.Entries);
                for(int i = 0; i < am.Entries.Count; i++)
                {
                    Assert.AreEqual(am.Entries[i].Key, bm.Entries[i].Key);
                    AssertStructurallyEqual(am.Entries[i].Value, bm.Entries[i].Value);
                }
                break;
            }
            default:
            {
                Assert.Fail($"Unexpected node type: {a.GetType().Name}");
                break;
            }
        }
    }

    private static JsonNode ParseJson(string json)
    {
        Utf8String utf8 = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(json));
        return StjJsonAdapter.Parse(utf8);
    }
}
