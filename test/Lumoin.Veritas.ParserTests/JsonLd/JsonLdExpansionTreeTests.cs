using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.JsonLd;

/// <summary>
/// Tests for <see cref="JsonLdExpansionTree.ExpandAsync"/>. Verifies that
/// expansion produces JSON-LD 1.1 expanded form as an object graph,
/// suitable for re-compaction.
/// </summary>
[TestClass]
internal sealed class JsonLdExpansionTreeTests
{
    public required TestContext TestContext { get; set; }

    private static JsonNode ParseJson(string json) =>
        StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(json)));

    private static ContextResolverDelegate NullResolver { get; } =
        (_, _) => ValueTask.FromResult<Utf8String?>(null);

    [TestMethod]
    public async Task ExpandsBareNodeObjectToExpandedForm()
    {
        //Compact: {"@context": {"name": "http://schema.org/name"}, "name": "Alice"}
        //Expanded: [{"http://schema.org/name": [{"@value": "Alice"}]}]
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {"name": "http://schema.org/name"},
              "name": "Alice"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, result);
        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.IsTrue(node.ContainsKey("http://schema.org/name"));
        IReadOnlyList<object?> values = (IReadOnlyList<object?>)node["http://schema.org/name"]!;
        Assert.HasCount(1, values);
        IReadOnlyDictionary<string, object?> valueObject = (IReadOnlyDictionary<string, object?>)values[0]!;
        Assert.AreEqual("Alice", valueObject["@value"]);
    }

    [TestMethod]
    public async Task ExpandsIdAndType()
    {
        //@id document-relative-resolved (with no @base, stays as given).
        //@type vocab-relative.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {"@vocab": "http://schema.org/"},
              "@id": "http://example.org/p/1",
              "@type": "Person",
              "name": "Alice"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.AreEqual("http://example.org/p/1", node["@id"]);
        //A node object's @type expands to an array of IRIs.
        IReadOnlyList<object?> types = (IReadOnlyList<object?>)node["@type"]!;
        Assert.HasCount(1, types);
        Assert.AreEqual("http://schema.org/Person", types[0]);
        Assert.IsTrue(node.ContainsKey("http://schema.org/name"));
    }

    [TestMethod]
    public async Task ScalarUnderTypedTermBecomesValueObjectWithType()
    {
        //Term "birthDate" coerces values to xsd:date. The scalar string
        //becomes a value object preserving the type.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "birthDate": {
                  "@id": "http://schema.org/birthDate",
                  "@type": "http://www.w3.org/2001/XMLSchema#date"
                }
              },
              "birthDate": "1990-01-15"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> values = (IReadOnlyList<object?>)node["http://schema.org/birthDate"]!;
        IReadOnlyDictionary<string, object?> valueObject = (IReadOnlyDictionary<string, object?>)values[0]!;
        Assert.AreEqual("1990-01-15", valueObject["@value"]);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#date", valueObject["@type"]);
    }

    [TestMethod]
    public async Task ScalarUnderLanguagedTermBecomesValueObjectWithLanguage()
    {
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "greeting": {
                  "@id": "http://example.org/greeting",
                  "@language": "fr"
                }
              },
              "greeting": "Bonjour"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> values = (IReadOnlyList<object?>)node["http://example.org/greeting"]!;
        IReadOnlyDictionary<string, object?> valueObject = (IReadOnlyDictionary<string, object?>)values[0]!;
        Assert.AreEqual("Bonjour", valueObject["@value"]);
        Assert.AreEqual("fr", valueObject["@language"]);
    }

    [TestMethod]
    public async Task WrapsBareValueInArray()
    {
        //A single-valued property in compact form becomes an array in
        //expanded form, per JSON-LD 1.1 §5.2.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {"@context": {"name": "http://ex/name"}, "name": "Alice"}
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.IsInstanceOfType<IReadOnlyList<object?>>(node["http://ex/name"]);
    }

    [TestMethod]
    public async Task ExpandsNestedNodeObjectRecursively()
    {
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "name": "http://schema.org/name",
                "knows": "http://schema.org/knows"
              },
              "@id": "http://ex/alice",
              "name": "Alice",
              "knows": {
                "@id": "http://ex/bob",
                "name": "Bob"
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> alice = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.AreEqual("http://ex/alice", alice["@id"]);
        IReadOnlyList<object?> knows = (IReadOnlyList<object?>)alice["http://schema.org/knows"]!;
        IReadOnlyDictionary<string, object?> bob = (IReadOnlyDictionary<string, object?>)knows[0]!;
        Assert.AreEqual("http://ex/bob", bob["@id"]);
        IReadOnlyList<object?> bobName = (IReadOnlyList<object?>)bob["http://schema.org/name"]!;
        IReadOnlyDictionary<string, object?> bobNameValue = (IReadOnlyDictionary<string, object?>)bobName[0]!;
        Assert.AreEqual("Bob", bobNameValue["@value"]);
    }

    [TestMethod]
    public async Task ExpandsListContainerToListObject()
    {
        //@container=@list: compact array becomes {"@list": [...]} in
        //expanded form.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "steps": {"@id": "http://ex/steps", "@container": "@list"}
              },
              "steps": ["first", "second"]
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> values = (IReadOnlyList<object?>)node["http://ex/steps"]!;
        IReadOnlyDictionary<string, object?> listObject = (IReadOnlyDictionary<string, object?>)values[0]!;
        IReadOnlyList<object?> items = (IReadOnlyList<object?>)listObject["@list"]!;
        Assert.HasCount(2, items);
        IReadOnlyDictionary<string, object?> first = (IReadOnlyDictionary<string, object?>)items[0]!;
        Assert.AreEqual("first", first["@value"]);
    }

    [TestMethod]
    public async Task PreservesAlreadyExpandedValueObjects()
    {
        //Input is already in value-object form ({"@value": ..., "@type": ...}).
        //Expansion is idempotent: passes through.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {"birthDate": "http://schema.org/birthDate"},
              "birthDate": {
                "@value": "1990-01-15",
                "@type": "http://www.w3.org/2001/XMLSchema#date"
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> values = (IReadOnlyList<object?>)node["http://schema.org/birthDate"]!;
        IReadOnlyDictionary<string, object?> valueObject = (IReadOnlyDictionary<string, object?>)values[0]!;
        Assert.AreEqual("1990-01-15", valueObject["@value"]);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#date", valueObject["@type"]);
    }

    [TestMethod]
    public async Task ExpandsLanguageContainerToValueObjects()
    {
        //@container=@language: compact {lang: value} map → expanded array
        //of {"@value", "@language"} objects.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "label": {
                  "@id": "http://www.w3.org/2000/01/rdf-schema#label",
                  "@container": "@language"
                }
              },
              "label": {
                "en": "Hello",
                "fr": "Bonjour"
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> labels = (IReadOnlyList<object?>)node["http://www.w3.org/2000/01/rdf-schema#label"]!;
        Assert.HasCount(2, labels);
        //Find the English and French value objects.
        bool foundEn = false;
        bool foundFr = false;
        foreach(object? labelObj in labels)
        {
            IReadOnlyDictionary<string, object?> entry = (IReadOnlyDictionary<string, object?>)labelObj!;
            string lang = (string)entry["@language"]!;
            string value = (string)entry["@value"]!;
            if(lang == "en")
            {
                foundEn = true;
                Assert.AreEqual("Hello", value);
            }
            else if(lang == "fr")
            {
                foundFr = true;
                Assert.AreEqual("Bonjour", value);
            }
        }
        Assert.IsTrue(foundEn && foundFr);
    }

    [TestMethod]
    public async Task ExpandsIndexContainerAttachingIndexToEachItem()
    {
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "items": {"@id": "http://ex/items", "@container": "@index"},
                "name": "http://ex/name"
              },
              "items": {
                "sku-1": {"name": "Widget"},
                "sku-2": {"name": "Gadget"}
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> items = (IReadOnlyList<object?>)node["http://ex/items"]!;
        Assert.HasCount(2, items);
        bool foundSku1 = false;
        foreach(object? itemObj in items)
        {
            IReadOnlyDictionary<string, object?> item = (IReadOnlyDictionary<string, object?>)itemObj!;
            if((string)item["@index"]! == "sku-1")
            {
                foundSku1 = true;
                IReadOnlyList<object?> names = (IReadOnlyList<object?>)item["http://ex/name"]!;
                IReadOnlyDictionary<string, object?> nameValue = (IReadOnlyDictionary<string, object?>)names[0]!;
                Assert.AreEqual("Widget", nameValue["@value"]);
            }
        }
        Assert.IsTrue(foundSku1);
    }

    [TestMethod]
    public async Task ExpandsIdContainerAttachingIdToEachItem()
    {
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "people": {"@id": "http://ex/people", "@container": "@id"},
                "name": "http://ex/name"
              },
              "people": {
                "http://ex/alice": {"name": "Alice"},
                "http://ex/bob":   {"name": "Bob"}
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> people = (IReadOnlyList<object?>)node["http://ex/people"]!;
        Assert.HasCount(2, people);
        bool foundAlice = false;
        foreach(object? p in people)
        {
            IReadOnlyDictionary<string, object?> person = (IReadOnlyDictionary<string, object?>)p!;
            if((string)person["@id"]! == "http://ex/alice")
            {
                foundAlice = true;
            }
        }
        Assert.IsTrue(foundAlice);
    }

    [TestMethod]
    public async Task ExpandsTypeContainerAttachingTypeToEachItem()
    {
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "@vocab": "http://schema.org/",
                "members": {"@id": "http://ex/members", "@container": "@type"},
                "name": "http://ex/name"
              },
              "members": {
                "Person":       {"name": "Alice"},
                "Organization": {"name": "Acme"}
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> members = (IReadOnlyList<object?>)node["http://ex/members"]!;
        Assert.HasCount(2, members);
        bool foundPerson = false;
        foreach(object? m in members)
        {
            IReadOnlyDictionary<string, object?> member = (IReadOnlyDictionary<string, object?>)m!;
            //Type is vocab-expanded, in the expanded-form @type array.
            IReadOnlyList<object?> typeIris = (IReadOnlyList<object?>)member["@type"]!;
            if(typeIris.Contains("http://schema.org/Person"))
            {
                foundPerson = true;
            }
        }
        Assert.IsTrue(foundPerson);
    }

    [TestMethod]
    public async Task ExpandsTypeScopedContext()
    {
        //Type-scoped context: a Person term carries an inner @context
        //defining "name". When @type=Person is encountered on a node,
        //the inner context applies for that node's properties.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "Person": {
                  "@id": "http://schema.org/Person",
                  "@context": {"name": "http://schema.org/name"}
                }
              },
              "@type": "http://schema.org/Person",
              "name": "Alice"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        //After type-scoped context applied, "name" expands to the
        //schema.org IRI.
        Assert.IsTrue(node.ContainsKey("http://schema.org/name"));
    }

    [TestMethod]
    public async Task ExpandsPropertyScopedContext()
    {
        //Property-scoped context: a Container term carries an inner
        //@context defining "inner". The inner context applies only
        //when descending into Container's value.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "Container": {
                  "@id": "http://example.org/container",
                  "@context": {"inner": "http://example.org/inner"}
                }
              },
              "Container": {
                "inner": "nested-value"
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        IReadOnlyList<object?> containerValues = (IReadOnlyList<object?>)node["http://example.org/container"]!;
        IReadOnlyDictionary<string, object?> innerObject = (IReadOnlyDictionary<string, object?>)containerValues[0]!;
        //The property-scoped context resolves "inner" to its IRI.
        Assert.IsTrue(innerObject.ContainsKey("http://example.org/inner"));
    }

    [TestMethod]
    public async Task ExpandsAtReverseEntries()
    {
        //@reverse: properties under it expand normally but stay grouped
        //under @reverse in the output, so RDF projection can swap S/O.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {
                "parent": "http://example.org/parent",
                "child":  "http://example.org/child"
              },
              "@id": "http://ex/alice",
              "@reverse": {
                "child": {"@id": "http://ex/parent-of-alice"}
              }
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.IsTrue(node.ContainsKey("@reverse"));
        IReadOnlyDictionary<string, object?> reverse = (IReadOnlyDictionary<string, object?>)node["@reverse"]!;
        Assert.IsTrue(reverse.ContainsKey("http://example.org/child"));
        IReadOnlyList<object?> children = (IReadOnlyList<object?>)reverse["http://example.org/child"]!;
        IReadOnlyDictionary<string, object?> parentRef = (IReadOnlyDictionary<string, object?>)children[0]!;
        Assert.AreEqual("http://ex/parent-of-alice", parentRef["@id"]);
    }

    [TestMethod]
    public async Task DropsUnmappedTerms()
    {
        //A property whose name is neither a JSON-LD keyword nor a known
        //term and doesn't look like an IRI is dropped per JSON-LD 1.1 §5.
        JsonNode input = ParseJson(/*lang=json,strict*/ """
            {
              "@context": {"name": "http://ex/name"},
              "name": "Alice",
              "stranger": "ignored"
            }
            """);

        IReadOnlyList<object?> result = await JsonLdExpansionTree.ExpandAsync(
            input, baseUrl: null, NullResolver, StjJsonAdapter.Parse, TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?> node = (IReadOnlyDictionary<string, object?>)result[0]!;
        Assert.IsTrue(node.ContainsKey("http://ex/name"));
        Assert.IsFalse(node.ContainsKey("stranger"));
    }
}
