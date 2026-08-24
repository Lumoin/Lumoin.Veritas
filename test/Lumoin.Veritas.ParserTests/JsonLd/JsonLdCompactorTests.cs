using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.JsonLd;

[TestClass]
internal sealed class JsonLdCompactorTests
{
    public required TestContext TestContext { get; set; }

    private static JsonNode ParseJson(string json) =>
        StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(json)));

    private static string[] SetContainer { get; } = ["@set"];
    private static string[] ListContainer { get; } = ["@list"];
    private static string[] LanguageContainer { get; } = ["@language"];
    private static string[] IndexContainer { get; } = ["@index"];
    private static string[] IdContainer { get; } = ["@id"];
    private static string[] TypeContainer { get; } = ["@type"];
    private static string[] GraphContainer { get; } = ["@graph"];
    private static string[] GraphIdContainer { get; } = ["@graph", "@id"];
    private static string[] GraphIndexContainer { get; } = ["@graph", "@index"];

    [TestMethod]
    public void CompactIriReturnsTermForDirectMatch()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty.WithTerm(
            "name", new TermDefinition { IriMapping = "http://schema.org/name" });

        Assert.AreEqual("name", JsonLdCompactor.CompactIri(ctx, "http://schema.org/name"));
    }

    [TestMethod]
    public void CompactIriPicksShortestThenOrdinalLeastTerm()
    {
        //All three terms point to the same IRI; shortest wins, ties broken
        //by ordinal compare.
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("longerName", new TermDefinition { IriMapping = "http://schema.org/name" })
            .WithTerm("zz", new TermDefinition { IriMapping = "http://schema.org/name" })
            .WithTerm("aa", new TermDefinition { IriMapping = "http://schema.org/name" });

        Assert.AreEqual("aa", JsonLdCompactor.CompactIri(ctx, "http://schema.org/name"));
    }

    [TestMethod]
    public void CompactIriReturnsKeywordsUnchanged()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty;
        Assert.AreEqual("@id", JsonLdCompactor.CompactIri(ctx, "@id"));
        Assert.AreEqual("@type", JsonLdCompactor.CompactIri(ctx, "@type"));
        Assert.AreEqual("@value", JsonLdCompactor.CompactIri(ctx, "@value"));
    }

    [TestMethod]
    public void CompactIriBuildsCompactIriFromPrefixTerm()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty.WithTerm(
            "schema", new TermDefinition { IriMapping = "http://schema.org/", Prefix = true });

        Assert.AreEqual("schema:name", JsonLdCompactor.CompactIri(ctx, "http://schema.org/name"));
    }

    [TestMethod]
    public void CompactIriBoundaryWalkPicksLongestNamespace()
    {
        //Two prefix terms; the more-specific one should win because the
        //boundary walk visits boundaries right-to-left, finding the longest
        //matching namespace first.
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("schema", new TermDefinition { IriMapping = "http://schema.org/", Prefix = true })
            .WithTerm("ext", new TermDefinition { IriMapping = "http://schema.org/Person/", Prefix = true });

        Assert.AreEqual("ext:name", JsonLdCompactor.CompactIri(ctx, "http://schema.org/Person/name"));
    }

    [TestMethod]
    public void CompactIriDoesNotEmitCompactIriThatCollidesWithExistingTerm()
    {
        //Prefix term "ex" with namespace "http://example.org/" would yield
        //"ex:taken". But "ex:taken" is itself defined as a term with a
        //different IRI — the compactor must avoid the ambiguity and fall
        //through.
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("ex", new TermDefinition { IriMapping = "http://example.org/", Prefix = true })
            .WithTerm("ex:taken", new TermDefinition { IriMapping = "http://other.example/something-else" });

        string result = JsonLdCompactor.CompactIri(ctx, "http://example.org/taken");
        Assert.AreNotEqual("ex:taken", result);
        Assert.AreEqual("http://example.org/taken", result);
    }

    [TestMethod]
    public void CompactIriVocabRelativeStripsVocabPrefix()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty.WithVocabularyMapping("http://schema.org/");

        Assert.AreEqual("name", JsonLdCompactor.CompactIri(ctx, "http://schema.org/name", vocab: true));
    }

    [TestMethod]
    public void CompactIriVocabRelativeIgnoredWhenVocabFalse()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty.WithVocabularyMapping("http://schema.org/");

        Assert.AreEqual(
            "http://schema.org/name",
            JsonLdCompactor.CompactIri(ctx, "http://schema.org/name", vocab: false));
    }

    [TestMethod]
    public void CompactIriFallsThroughWhenNoMatch()
    {
        LinkedDataContext ctx = LinkedDataContext.Empty;

        Assert.AreEqual(
            "http://schema.org/name",
            JsonLdCompactor.CompactIri(ctx, "http://schema.org/name"));
    }

    [TestMethod]
    public void CompactIriPrefersTermOverCompactIri()
    {
        //Both a direct term and a prefix term apply. Direct term wins
        //because Phase 1 runs before Phase 2.
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("schema", new TermDefinition { IriMapping = "http://schema.org/", Prefix = true })
            .WithTerm("name", new TermDefinition { IriMapping = "http://schema.org/name" });

        Assert.AreEqual("name", JsonLdCompactor.CompactIri(ctx, "http://schema.org/name"));
    }

    [TestMethod]
    public void CompactValueUnwrapsPlainLiteral()
    {
        JsonNode valueObj = ParseJson(/*lang=json,strict*/ """
            {"@value": "Alice"}
            """);

        object? compacted = JsonLdCompactor.CompactValue(valueObj, termDefinition: null);

        Assert.AreEqual("Alice", compacted);
    }

    [TestMethod]
    public void CompactValueUnwrapsWhenTypeCoercionMatches()
    {
        //Value has @type=xsd:date; term's TypeMapping matches.
        JsonNode valueObj = ParseJson(/*lang=json,strict*/ """
            {"@value": "2024-01-15", "@type": "http://www.w3.org/2001/XMLSchema#date"}
            """);
        TermDefinition term = new()
        {
            IriMapping = "http://example.org/birthDate",
            TypeMapping = "http://www.w3.org/2001/XMLSchema#date"
        };

        object? compacted = JsonLdCompactor.CompactValue(valueObj, term);

        Assert.AreEqual("2024-01-15", compacted);
    }

    [TestMethod]
    public void CompactValuePreservesValueObjectWhenTypeMismatched()
    {
        JsonNode valueObj = ParseJson(/*lang=json,strict*/ """
            {"@value": "2024-01-15", "@type": "http://www.w3.org/2001/XMLSchema#date"}
            """);
        TermDefinition term = new()
        {
            IriMapping = "http://example.org/note",
            TypeMapping = "http://www.w3.org/2001/XMLSchema#string"
        };

        object? compacted = JsonLdCompactor.CompactValue(valueObj, term);

        Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(compacted);
        IReadOnlyDictionary<string, object?> map = (IReadOnlyDictionary<string, object?>)compacted;
        Assert.AreEqual("2024-01-15", map["@value"]);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#date", map["@type"]);
    }

    [TestMethod]
    public void CompactValueUnwrapsWhenLanguageMatches()
    {
        JsonNode valueObj = ParseJson(/*lang=json,strict*/ """
            {"@value": "Bonjour", "@language": "fr"}
            """);
        TermDefinition term = new()
        {
            IriMapping = "http://example.org/greeting",
            LanguageMapping = "fr",
            HasLanguageMapping = true
        };

        object? compacted = JsonLdCompactor.CompactValue(valueObj, term);

        Assert.AreEqual("Bonjour", compacted);
    }

    [TestMethod]
    public async Task CompactWalksNodeObjectAndEmitsContext()
    {
        //Expanded form: one property with a value-object array.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "@id": "http://example.org/person/1",
              "http://schema.org/name": [{"@value": "Alice"}]
            }
            """);

        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithVocabularyMapping("http://schema.org/")
            .WithTerm("name", new TermDefinition { IriMapping = "http://schema.org/name" });

        object? result = await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(result);
        IReadOnlyDictionary<string, object?> map = (IReadOnlyDictionary<string, object?>)result;
        Assert.IsTrue(map.ContainsKey("@context"));
        Assert.AreEqual("http://example.org/person/1", map["@id"]);
        Assert.AreEqual("Alice", map["name"]);
    }

    [TestMethod]
    public async Task CompactCompactsTypeViaVocab()
    {
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "@id": "http://example.org/p/1",
              "@type": "http://schema.org/Person"
            }
            """);

        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithVocabularyMapping("http://schema.org/");

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        //Type is compacted vocab-relative; vocab-relative compaction strips
        //the schema.org/ prefix and leaves "Person".
        Assert.AreEqual("Person", result["@type"]);
    }

    [TestMethod]
    public async Task CompactUnwrapsSingleItemArrayByDefault()
    {
        //Expanded form always wraps property values in an array. Compaction
        //unwraps single-item arrays per the default compactArrays=true.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/name": [{"@value": "Alice"}]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("name", new TermDefinition { IriMapping = "http://schema.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.AreEqual("Alice", result["name"]);
    }

    [TestMethod]
    public async Task CompactPreservesMultiItemArrays()
    {
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/tags": [
                {"@value": "alpha"},
                {"@value": "beta"}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("tags", new TermDefinition { IriMapping = "http://schema.org/tags" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsInstanceOfType<IReadOnlyList<object?>>(result["tags"]);
        IReadOnlyList<object?> tags = (IReadOnlyList<object?>)result["tags"]!;
        Assert.HasCount(2, tags);
        Assert.AreEqual("alpha", tags[0]);
        Assert.AreEqual("beta", tags[1]);
    }

    [TestMethod]
    public async Task CompactEmptyContextSkipsAtContext()
    {
        //An empty active context has nothing meaningful to emit at the top;
        //the result is just the compacted body.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {"@id": "http://example.org/x"}
            """);

        object? result = await JsonLdCompactor.CompactAsync(expanded, LinkedDataContext.Empty, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(result);
        IReadOnlyDictionary<string, object?> map = (IReadOnlyDictionary<string, object?>)result;
        Assert.IsFalse(map.ContainsKey("@context"));
    }

    [TestMethod]
    public async Task CompactNestedNodeObjectRecurses()
    {
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "@id": "http://example.org/p/1",
              "http://schema.org/knows": [{
                "@id": "http://example.org/p/2",
                "http://schema.org/name": [{"@value": "Bob"}]
              }]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("name", new TermDefinition { IriMapping = "http://schema.org/name" })
            .WithTerm("knows", new TermDefinition { IriMapping = "http://schema.org/knows" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.AreEqual("http://example.org/p/1", result["@id"]);
        Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(result["knows"]);
        IReadOnlyDictionary<string, object?> bob = (IReadOnlyDictionary<string, object?>)result["knows"]!;
        Assert.AreEqual("http://example.org/p/2", bob["@id"]);
        Assert.AreEqual("Bob", bob["name"]);
    }

    [TestMethod]
    public async Task CompactEmitsCompactIriForUntermedProperty()
    {
        //A property whose IRI has no direct term but matches a registered
        //prefix term compacts to the prefix:suffix form.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/unmapped": [{"@value": "x"}]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("schema", new TermDefinition { IriMapping = "http://schema.org/", Prefix = true });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsTrue(result.ContainsKey("schema:unmapped"));
        Assert.AreEqual("x", result["schema:unmapped"]);
    }

    [TestMethod]
    public async Task CompactSetContainerForcesArrayOnSingleValue()
    {
        //Without @set, a single-item array unwraps. With @set, the array
        //shape is preserved even for one value.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/tag": [{"@value": "alpha"}]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("tag", new TermDefinition
            {
                IriMapping = "http://schema.org/tag",
                ContainerMapping = SetContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsInstanceOfType<IReadOnlyList<object?>>(result["tag"]);
        IReadOnlyList<object?> list = (IReadOnlyList<object?>)result["tag"]!;
        Assert.HasCount(1, list);
        Assert.AreEqual("alpha", list[0]);
    }

    [TestMethod]
    public async Task CompactListContainerUnwrapsToBareArray()
    {
        //Expanded form for a @list-typed value is {"@list": [...]}, wrapped
        //in the property's array. Compact form under @container=@list is
        //the bare JSON array — order preserved (RDF projection emits as
        //rdf:List).
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/steps": [{
                "@list": [
                  {"@value": "first"},
                  {"@value": "second"},
                  {"@value": "third"}
                ]
              }]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("steps", new TermDefinition
            {
                IriMapping = "http://schema.org/steps",
                ContainerMapping = ListContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsInstanceOfType<IReadOnlyList<object?>>(result["steps"]);
        IReadOnlyList<object?> steps = (IReadOnlyList<object?>)result["steps"]!;
        Assert.HasCount(3, steps);
        Assert.AreEqual("first", steps[0]);
        Assert.AreEqual("second", steps[1]);
        Assert.AreEqual("third", steps[2]);
    }

    [TestMethod]
    public async Task CompactListContainerWithEmptyListYieldsEmptyArray()
    {
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/steps": [{ "@list": [] }]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("steps", new TermDefinition
            {
                IriMapping = "http://schema.org/steps",
                ContainerMapping = ListContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsInstanceOfType<IReadOnlyList<object?>>(result["steps"]);
        Assert.IsEmpty((IReadOnlyList<object?>)result["steps"]!);
    }

    [TestMethod]
    public async Task CompactLanguageContainerCollectsTaggedValuesIntoMap()
    {
        //Expanded form: array of {@value, @language}. Compact under
        //@container=@language: a single object keyed by language tag.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://www.w3.org/2000/01/rdf-schema#label": [
                {"@value": "Hello", "@language": "en"},
                {"@value": "Bonjour", "@language": "fr"},
                {"@value": "こんにちは", "@language": "ja"}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("label", new TermDefinition
            {
                IriMapping = "http://www.w3.org/2000/01/rdf-schema#label",
                ContainerMapping = LanguageContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(result["label"]);
        IReadOnlyDictionary<string, object?> labelMap = (IReadOnlyDictionary<string, object?>)result["label"]!;
        Assert.AreEqual("Hello", labelMap["en"]);
        Assert.AreEqual("Bonjour", labelMap["fr"]);
        Assert.AreEqual("こんにちは", labelMap["ja"]);
    }

    [TestMethod]
    public async Task CompactLanguageContainerCollectsMultipleValuesPerLanguageIntoArray()
    {
        //Two English values: the map value at "en" becomes an array.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://www.w3.org/2000/01/rdf-schema#label": [
                {"@value": "Hello", "@language": "en"},
                {"@value": "Hi",    "@language": "en"},
                {"@value": "Bonjour","@language": "fr"}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("label", new TermDefinition
            {
                IriMapping = "http://www.w3.org/2000/01/rdf-schema#label",
                ContainerMapping = LanguageContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> labelMap = (IReadOnlyDictionary<string, object?>)result["label"]!;
        Assert.IsInstanceOfType<IReadOnlyList<object?>>(labelMap["en"]);
        IReadOnlyList<object?> en = (IReadOnlyList<object?>)labelMap["en"]!;
        Assert.HasCount(2, en);
        Assert.AreEqual("Hello", en[0]);
        Assert.AreEqual("Hi", en[1]);
        Assert.AreEqual("Bonjour", labelMap["fr"]);
    }

    [TestMethod]
    public async Task CompactIndexContainerCollectsItemsByIndexKey()
    {
        //Expanded form carries an @index property on each item; compact
        //form is a map keyed by the index strings. The @index property is
        //removed from the inner value because it's now in the map key.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/items": [
                {"@index": "sku-1", "http://example.org/name": [{"@value": "Widget"}]},
                {"@index": "sku-2", "http://example.org/name": [{"@value": "Gadget"}]}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("items", new TermDefinition
            {
                IriMapping = "http://example.org/items",
                ContainerMapping = IndexContainer
            })
            .WithTerm("name", new TermDefinition { IriMapping = "http://example.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> items = (IReadOnlyDictionary<string, object?>)result["items"]!;
        Assert.HasCount(2, items);
        IReadOnlyDictionary<string, object?> sku1 = (IReadOnlyDictionary<string, object?>)items["sku-1"]!;
        Assert.AreEqual("Widget", sku1["name"]);
        //The @index key must be stripped from the inner value.
        Assert.IsFalse(sku1.ContainsKey("@index"));
        IReadOnlyDictionary<string, object?> sku2 = (IReadOnlyDictionary<string, object?>)items["sku-2"]!;
        Assert.AreEqual("Gadget", sku2["name"]);
    }

    [TestMethod]
    public async Task CompactIndexContainerCollectsRepeatsIntoArray()
    {
        //Two items with the same @index → array under that key.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/items": [
                {"@index": "shared", "http://example.org/name": [{"@value": "Alpha"}]},
                {"@index": "shared", "http://example.org/name": [{"@value": "Beta"}]}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("items", new TermDefinition
            {
                IriMapping = "http://example.org/items",
                ContainerMapping = IndexContainer
            })
            .WithTerm("name", new TermDefinition { IriMapping = "http://example.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> items = (IReadOnlyDictionary<string, object?>)result["items"]!;
        Assert.IsInstanceOfType<IReadOnlyList<object?>>(items["shared"]);
        IReadOnlyList<object?> bucket = (IReadOnlyList<object?>)items["shared"]!;
        Assert.HasCount(2, bucket);
    }

    [TestMethod]
    public async Task CompactIdContainerKeysByNodeId()
    {
        //@id container: each node-object item is keyed by its @id IRI;
        //the @id property is removed from the value.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/people": [
                {"@id": "http://example.org/alice", "http://example.org/name": [{"@value": "Alice"}]},
                {"@id": "http://example.org/bob",   "http://example.org/name": [{"@value": "Bob"}]}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("people", new TermDefinition
            {
                IriMapping = "http://example.org/people",
                ContainerMapping = IdContainer
            })
            .WithTerm("name", new TermDefinition { IriMapping = "http://example.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> people = (IReadOnlyDictionary<string, object?>)result["people"]!;
        Assert.HasCount(2, people);
        IReadOnlyDictionary<string, object?> alice = (IReadOnlyDictionary<string, object?>)people["http://example.org/alice"]!;
        Assert.AreEqual("Alice", alice["name"]);
        Assert.IsFalse(alice.ContainsKey("@id"));
        IReadOnlyDictionary<string, object?> bob = (IReadOnlyDictionary<string, object?>)people["http://example.org/bob"]!;
        Assert.AreEqual("Bob", bob["name"]);
    }

    [TestMethod]
    public async Task CompactTypeContainerKeysByVocabRelativeType()
    {
        //@type container: items keyed by their @type, which compacts
        //vocab-relative when @vocab is set. @type is removed from the
        //inner value (single-string case).
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/members": [
                {"@type": "http://schema.org/Person",       "http://example.org/name": [{"@value": "Alice"}]},
                {"@type": "http://schema.org/Organization", "http://example.org/name": [{"@value": "Acme"}]}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithVocabularyMapping("http://schema.org/")
            .WithTerm("members", new TermDefinition
            {
                IriMapping = "http://example.org/members",
                ContainerMapping = TypeContainer
            })
            .WithTerm("name", new TermDefinition { IriMapping = "http://example.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> members = (IReadOnlyDictionary<string, object?>)result["members"]!;
        Assert.HasCount(2, members);
        IReadOnlyDictionary<string, object?> person = (IReadOnlyDictionary<string, object?>)members["Person"]!;
        Assert.AreEqual("Alice", person["name"]);
        Assert.IsFalse(person.ContainsKey("@type"));
        IReadOnlyDictionary<string, object?> org = (IReadOnlyDictionary<string, object?>)members["Organization"]!;
        Assert.AreEqual("Acme", org["name"]);
    }

    [TestMethod]
    public async Task CompactGraphContainerAloneInlinesSimpleGraph()
    {
        //@graph container alone (W3C JSON-LD 1.1 §4.1): a simple graph object's
        //contents inline directly under the term — the @graph wrapper is implied
        //by the container and dropped from the compact form. The expanded input
        //is a graph object, the shape the expansion algorithm produces for a
        //@graph-container term.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/snapshot": [
                {
                  "@graph": [
                    {
                      "@id": "http://example.org/p/1",
                      "http://example.org/name": [{"@value": "Alice"}]
                    }
                  ]
                }
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("snapshot", new TermDefinition
            {
                IriMapping = "http://example.org/snapshot",
                ContainerMapping = GraphContainer
            })
            .WithTerm("name", new TermDefinition { IriMapping = "http://example.org/name" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        //The node inlines under "snapshot" with no explicit @graph wrapper.
        IReadOnlyDictionary<string, object?> snapshot = (IReadOnlyDictionary<string, object?>)result["snapshot"]!;
        Assert.IsFalse(snapshot.ContainsKey("@graph"));
        Assert.AreEqual("http://example.org/p/1", snapshot["@id"]);
        Assert.AreEqual("Alice", snapshot["name"]);
    }

    [TestMethod]
    public async Task CompactGraphIdContainerKeysGraphsById()
    {
        //@graph + @id combination: a map keyed by the named graph's @id, whose
        //value is the inlined graph contents (the @graph and @id keys are
        //carried by the container and map key, so neither appears in the value).
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/versions": [
                {
                  "@id": "http://example.org/v/1",
                  "@graph": [{"http://example.org/note": [{"@value": "first cut"}]}]
                },
                {
                  "@id": "http://example.org/v/2",
                  "@graph": [{"http://example.org/note": [{"@value": "second pass"}]}]
                }
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("versions", new TermDefinition
            {
                IriMapping = "http://example.org/versions",
                ContainerMapping = GraphIdContainer
            })
            .WithTerm("note", new TermDefinition { IriMapping = "http://example.org/note" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> versions = (IReadOnlyDictionary<string, object?>)result["versions"]!;
        Assert.HasCount(2, versions);
        IReadOnlyDictionary<string, object?> v1 = (IReadOnlyDictionary<string, object?>)versions["http://example.org/v/1"]!;
        //The graph @id became the map key, so neither @id nor an explicit @graph remain.
        Assert.IsFalse(v1.ContainsKey("@graph"));
        Assert.IsFalse(v1.ContainsKey("@id"));
        Assert.AreEqual("first cut", v1["note"]);
    }

    [TestMethod]
    public async Task CompactGraphIndexContainerKeysGraphsByIndex()
    {
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://example.org/chapters": [
                {"@index": "ch1", "@graph": [{"http://example.org/title": [{"@value": "Intro"}]}]},
                {"@index": "ch2", "@graph": [{"http://example.org/title": [{"@value": "Method"}]}]}
              ]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("chapters", new TermDefinition
            {
                IriMapping = "http://example.org/chapters",
                ContainerMapping = GraphIndexContainer
            })
            .WithTerm("title", new TermDefinition { IriMapping = "http://example.org/title" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> chapters = (IReadOnlyDictionary<string, object?>)result["chapters"]!;
        IReadOnlyDictionary<string, object?> ch1 = (IReadOnlyDictionary<string, object?>)chapters["ch1"]!;
        //The @index became the map key; the value is the inlined graph contents.
        Assert.IsFalse(ch1.ContainsKey("@graph"));
        Assert.IsFalse(ch1.ContainsKey("@index"));
        Assert.AreEqual("Intro", ch1["title"]);
    }

    [TestMethod]
    public async Task CompactAtReverseEntries()
    {
        //Compactor counterpart to expander @reverse: each predicate under
        //@reverse compacts to its term/compact form while staying grouped.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "@id": "http://ex/alice",
              "@reverse": {
                "http://schema.org/parent": [{"@id": "http://ex/mom"}]
              }
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("parent", new TermDefinition { IriMapping = "http://schema.org/parent" });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        Assert.AreEqual("http://ex/alice", result["@id"]);
        IReadOnlyDictionary<string, object?> reverse = (IReadOnlyDictionary<string, object?>)result["@reverse"]!;
        //"http://schema.org/parent" should compact to the term "parent".
        Assert.IsTrue(reverse.ContainsKey("parent"));
    }

    [TestMethod]
    public async Task CompactContextEmitsContainerOnTermDefinition()
    {
        //A term with a container mapping must emit "@container" in the
        //compacted @context so re-expansion sees the same shape.
        JsonNode expanded = ParseJson(/*lang=json,strict*/ """
            {
              "http://schema.org/tag": [{"@value": "alpha"}]
            }
            """);
        LinkedDataContext ctx = LinkedDataContext.Empty
            .WithTerm("tag", new TermDefinition
            {
                IriMapping = "http://schema.org/tag",
                ContainerMapping = SetContainer
            });

        IReadOnlyDictionary<string, object?> result = (IReadOnlyDictionary<string, object?>)
            (await JsonLdCompactor.CompactAsync(expanded, ctx, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))!;

        IReadOnlyDictionary<string, object?> context = (IReadOnlyDictionary<string, object?>)result["@context"]!;
        IReadOnlyDictionary<string, object?> termDef = (IReadOnlyDictionary<string, object?>)context["tag"]!;
        Assert.AreEqual("http://schema.org/tag", termDef["@id"]);
        Assert.AreEqual("@set", termDef["@container"]);
    }
}
