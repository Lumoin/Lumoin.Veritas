using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Tests.LinkedData;

/// <summary>
/// Exercises the inverted namespace-IRI → prefix-term index on
/// <see cref="JsonLdContext"/>. The index is maintained by
/// <see cref="JsonLdContext.WithTerm"/> and
/// <see cref="JsonLdContext.WithoutTerm"/> and consulted by
/// <see cref="JsonLdContext.TryGetPrefixTerm"/>.
/// </summary>
[TestClass]
internal sealed class JsonLdContextPrefixIndexTests
{
    [TestMethod]
    public void EmptyContextHasNoPrefixTerms()
    {
        bool found = JsonLdContext.Empty.TryGetPrefixTerm("http://example.org/", out string? term);

        Assert.IsFalse(found);
        Assert.IsNull(term);
    }

    [TestMethod]
    public void PrefixTermIsIndexedOnWithTerm()
    {
        JsonLdContext context = JsonLdContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        bool found = context.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? term);

        Assert.IsTrue(found);
        Assert.AreEqual("foaf", term);
    }

    [TestMethod]
    public void NonPrefixTermIsNotIndexed()
    {
        JsonLdContext context = JsonLdContext.Empty.WithTerm(
            "name",
            new TermDefinition { IriMapping = "http://schema.org/name", Prefix = false });

        bool found = context.TryGetPrefixTerm("http://schema.org/name", out string? term);

        Assert.IsFalse(found);
        Assert.IsNull(term);
    }

    [TestMethod]
    public void ReplacingPrefixWithNonPrefixRemovesFromIndex()
    {
        JsonLdContext withPrefix = JsonLdContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        JsonLdContext replaced = withPrefix.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = false });

        bool found = replaced.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void ReplacingPrefixIriMovesIndexEntry()
    {
        JsonLdContext initial = JsonLdContext.Empty.WithTerm(
            "ns",
            new TermDefinition { IriMapping = "http://old.example.org/", Prefix = true });

        JsonLdContext moved = initial.WithTerm(
            "ns",
            new TermDefinition { IriMapping = "http://new.example.org/", Prefix = true });

        Assert.IsFalse(moved.TryGetPrefixTerm("http://old.example.org/", out string? _));
        Assert.IsTrue(moved.TryGetPrefixTerm("http://new.example.org/", out string? term));
        Assert.AreEqual("ns", term);
    }

    [TestMethod]
    public void WithoutTermRemovesPrefixIndexEntry()
    {
        JsonLdContext context = JsonLdContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        JsonLdContext removed = context.WithoutTerm("foaf");

        Assert.IsFalse(removed.TryGetPrefixTerm("http://xmlns.com/foaf/0.1/", out string? _));
    }

    [TestMethod]
    public void MultiplePrefixTermsForSameIriPickShortestThenOrdinalLeast()
    {
        //All three terms point to the same IRI; spec compaction prefers
        //shortest, then ordinal-least. "f" < "fo" < "foaf" by length;
        //within length 2, "fo" < "fz" by ordinal.
        JsonLdContext context = JsonLdContext.Empty
            .WithTerm("foaf", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("fz", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("fo", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true })
            .WithTerm("f", new TermDefinition { IriMapping = "http://example.org/v/", Prefix = true });

        Assert.IsTrue(context.TryGetPrefixTerm("http://example.org/v/", out string? term));
        Assert.AreEqual("f", term);

        //After removing "f", the next-best candidate is "fo" (length-2, ordinal-less than "fz").
        JsonLdContext withoutF = context.WithoutTerm("f");
        Assert.IsTrue(withoutF.TryGetPrefixTerm("http://example.org/v/", out string? next));
        Assert.AreEqual("fo", next);

        //After removing "fo", "fz" becomes the best.
        JsonLdContext withoutFo = withoutF.WithoutTerm("fo");
        Assert.IsTrue(withoutFo.TryGetPrefixTerm("http://example.org/v/", out string? finalTerm));
        Assert.AreEqual("fz", finalTerm);
    }

    [TestMethod]
    public void ScalarMutatorsPreservePrefixIndex()
    {
        JsonLdContext withPrefix = JsonLdContext.Empty.WithTerm(
            "foaf",
            new TermDefinition { IriMapping = "http://xmlns.com/foaf/0.1/", Prefix = true });

        JsonLdContext walked = withPrefix
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
        JsonLdContext snapshot = JsonLdContext.Empty.WithTerm(
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
