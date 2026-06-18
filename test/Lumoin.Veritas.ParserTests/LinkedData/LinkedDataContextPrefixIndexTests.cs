using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.ParserTests.LinkedData;

/// <summary>
/// Exercises the inverted namespace-IRI → prefix-term index on
/// <see cref="LinkedDataContext{TNode}"/>. The index is maintained by
/// <see cref="LinkedDataContext{TNode}.WithTerm"/> and
/// <see cref="LinkedDataContext{TNode}.WithoutTerm"/> and consulted by
/// <see cref="LinkedDataContext{TNode}.TryGetPrefixTerm"/>. The
/// behaviour is independent of <c>TNode</c>; the tests pick
/// <see cref="JsonNode"/> for the parameter.
/// </summary>
[TestClass]
internal sealed class LinkedDataContextPrefixIndexTests
{
    [TestMethod]
    public void EmptyContextHasNoPrefixTerms()
    {
        bool found = LinkedDataContext.Empty.TryGetPrefixTerm("http://example.org/", out string? term);

        Assert.IsFalse(found);
        Assert.IsNull(term);
    }

    [TestMethod]
    public void PrefixTermIsIndexedOnWithTerm()
    {
        LinkedDataContext context = LinkedDataContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        bool found = context.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? term);

        Assert.IsTrue(found);
        Assert.AreEqual("foaf", term);
    }

    [TestMethod]
    public void NonPrefixTermIsNotIndexed()
    {
        LinkedDataContext context = LinkedDataContext.Empty.WithTerm(
            "name",
            new TermDefinition { IriMapping = "http://schema.org/name", Prefix = false });

        bool found = context.TryGetPrefixTerm("http://schema.org/name", out string? term);

        Assert.IsFalse(found);
        Assert.IsNull(term);
    }

    [TestMethod]
    public void ReplacingPrefixWithNonPrefixRemovesFromIndex()
    {
        LinkedDataContext withPrefix = LinkedDataContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        LinkedDataContext replaced = withPrefix.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = false });

        bool found = replaced.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void ReplacingPrefixIriMovesIndexEntry()
    {
        LinkedDataContext initial = LinkedDataContext.Empty.WithTerm(
            "ns",
            new TermDefinition { IriMapping = "http://old.example.org/", Prefix = true });

        LinkedDataContext moved = initial.WithTerm(
            "ns",
            new TermDefinition { IriMapping = "http://new.example.org/", Prefix = true });

        Assert.IsFalse(moved.TryGetPrefixTerm("http://old.example.org/", out string? _));
        Assert.IsTrue(moved.TryGetPrefixTerm("http://new.example.org/", out string? term));
        Assert.AreEqual("ns", term);
    }

    [TestMethod]
    public void WithoutTermRemovesPrefixIndexEntry()
    {
        LinkedDataContext context = LinkedDataContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        LinkedDataContext removed = context.WithoutTerm("foaf");

        Assert.IsFalse(removed.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? _));
    }

    [TestMethod]
    public void MultiplePrefixTermsForSameIriPickShortestThenOrdinalLeast()
    {
        //All three terms point to the same IRI; spec compaction prefers
        //shortest, then ordinal-least. "f" < "fo" < "foaf" by length;
        //within length 2, "fo" < "fz" by ordinal.
        LinkedDataContext context = LinkedDataContext.Empty
            .WithTerm("foaf", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("fz", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("fo", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("f", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true });

        Assert.IsTrue(context.TryGetPrefixTerm("http://example.org/v/", out string? term));
        Assert.AreEqual("f", term);

        //After removing "f", the next-best candidate is "fo" (length-2, ordinal-less than "fz").
        LinkedDataContext withoutF = context.WithoutTerm("f");
        Assert.IsTrue(withoutF.TryGetPrefixTerm("http://example.org/v/", out string? next));
        Assert.AreEqual("fo", next);

        //After removing "fo", "fz" becomes the best.
        LinkedDataContext withoutFo = withoutF.WithoutTerm("fo");
        Assert.IsTrue(withoutFo.TryGetPrefixTerm("http://example.org/v/", out string? finalTerm));
        Assert.AreEqual("fz", finalTerm);
    }

    [TestMethod]
    public void ScalarMutatorsPreservePrefixIndex()
    {
        LinkedDataContext withPrefix = LinkedDataContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        LinkedDataContext walked = withPrefix
            .WithBaseIri("http://doc.example/")
            .WithVocabularyMapping("http://vocab.example/")
            .WithDefaultLanguage("en")
            .WithPropagate(false);

        Assert.IsTrue(walked.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? term));
        Assert.AreEqual("foaf", term);
    }

    [TestMethod]
    public void PrefixIndexIsImmutableUnderSnapshot()
    {
        //Persistent semantics: extending a context must not mutate the
        //prior snapshot's index. This is the property that makes a
        //frame-stack push/pop cheap and correct.
        LinkedDataContext snapshot = LinkedDataContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        _ = snapshot.WithTerm(
            "schema",
            new TermDefinition { IriMapping = "http://schema.org/", Prefix = true });

        Assert.IsTrue(snapshot.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? foaf));
        Assert.AreEqual("foaf", foaf);

        //The snapshot must not see the term added to its descendant.
        Assert.IsFalse(snapshot.TryGetPrefixTerm("http://schema.org/", out string? _));
    }
}
