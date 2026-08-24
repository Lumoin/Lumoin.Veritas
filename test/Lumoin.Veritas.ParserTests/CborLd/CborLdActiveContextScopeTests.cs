using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.Cbor.CborLd.Internal;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Exercises <see cref="CborLdActiveContextScope"/>'s three trigger
/// methods. The struct's contract: each method applies its trigger via
/// <c>ContextProcessing.ApplyEmbeddedContextsAsync</c>, eagerly assigns
/// dynamic ids for newly-added terms, and returns the successor context.
/// </summary>
[TestClass]
internal sealed class CborLdActiveContextScopeTests
{
    public required TestContext TestContext { get; set; }

    private static CborLdActiveContextScope NewScope() => new(null, null, null);

    private static CborLdConversionState NewState() => new();

    [TestMethod]
    public async Task WithEmbeddedContextAddsTermsAndAssignsIds()
    {
        //Inline @context defining one user term.
        CborLdInputMap contextNode = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>(
                "name",
                new CborLdInputString("http://schema.org/name"))
        });

        CborLdConversionState state = NewState();
        int idsBefore = state.TermToId.Count;

        LinkedDataContext next = await NewScope().WithEmbeddedContextAsync(
            LinkedDataContext.Empty,
            contextNode,
            baseUrl: null,
            state,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(next.TryGetTerm("name", out TermDefinition? def));
        Assert.AreEqual("http://schema.org/name", def?.IriMapping);

        Assert.IsTrue(state.TermToId.ContainsKey("name"));
        Assert.HasCount(idsBefore + 1, state.TermToId);

        //Round-trip invariant on the conversion state's two tables.
        int assignedId = state.TermToId["name"];
        Assert.IsTrue(state.IdToTerm.TryGetValue(assignedId, out string? roundTrip));
        Assert.AreEqual("name", roundTrip);
    }

    [TestMethod]
    public async Task WithEmbeddedContextIsIdempotentForExistingTerms()
    {
        //Same context applied twice must not allocate new ids the second time.
        CborLdInputMap contextNode = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>(
                "name",
                new CborLdInputString("http://schema.org/name"))
        });

        CborLdConversionState state = NewState();
        CborLdActiveContextScope scope = NewScope();

        LinkedDataContext first = await scope.WithEmbeddedContextAsync(
            LinkedDataContext.Empty, contextNode, null, state, TestContext.CancellationToken).ConfigureAwait(false);
        int idAfterFirst = state.TermToId["name"];
        int countAfterFirst = state.TermToId.Count;

        //Apply again against the post-first context — "name" is already there.
        LinkedDataContext second = await scope.WithEmbeddedContextAsync(
            first, contextNode, null, state, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(idAfterFirst, state.TermToId["name"]);
        Assert.HasCount(countAfterFirst, state.TermToId);
        //Term still resolves identically.
        Assert.IsTrue(second.TryGetTerm("name", out _));
    }

    [TestMethod]
    public async Task WithTypeScopedAppliesInAlphabeticalOrder()
    {
        //Two type-scoped contexts whose term mappings conflict; the
        //alphabetically-later one wins because it is applied last.
        //
        //"Alpha" defines color → http://example.org/alpha-color
        //"Beta"  defines color → http://example.org/beta-color
        //Applied alphabetical order (Alpha then Beta): final binding is Beta.

        LinkedDataTermSource alphaColor = new("k-alpha-color")
        {
            Iri = "http://example.org/alpha-color",
            IsSimpleString = true
        };
        LinkedDataTermSource betaColor = new("k-beta-color")
        {
            Iri = "http://example.org/beta-color",
            IsSimpleString = true
        };

        LinkedDataContextEntry alphaScoped = new(
            new Dictionary<string, LinkedDataTermSource> { ["color"] = alphaColor },
            baseUrl: null,
            syntheticKey: "alpha-scoped");
        LinkedDataContextEntry betaScoped = new(
            new Dictionary<string, LinkedDataTermSource> { ["color"] = betaColor },
            baseUrl: null,
            syntheticKey: "beta-scoped");

        //Seed context: two type terms each carrying its own ScopedContextEntries.
        LinkedDataContext seed = LinkedDataContext.Empty
            .WithTerm("Alpha", new TermDefinition
            {
                IriMapping = "http://example.org/types/Alpha",
                ScopedContextEntries = new[] { alphaScoped }
            })
            .WithTerm("Beta", new TermDefinition
            {
                IriMapping = "http://example.org/types/Beta",
                ScopedContextEntries = new[] { betaScoped }
            });

        //Reverse declaration order on purpose; method sorts.
        string[] typeIris =
        [
            "http://example.org/types/Beta",
            "http://example.org/types/Alpha"
        ];

        CborLdConversionState state = NewState();
        LinkedDataContext after = await NewScope().WithTypeScopedAsync(
            seed, typeIris, baseUrl: null, state, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(after.TryGetTerm("color", out TermDefinition? colorDef));
        Assert.AreEqual("http://example.org/beta-color", colorDef?.IriMapping);
    }

    private static string[] PlainTypeIris { get; } = ["http://example.org/Plain"];

    [TestMethod]
    public async Task WithTypeScopedSkipsTypesWithoutScopedEntries()
    {
        //Type whose term has no ScopedContextEntries → silently ignored.
        LinkedDataContext seed = LinkedDataContext.Empty.WithTerm(
            "Plain", new TermDefinition { IriMapping = "http://example.org/Plain" });

        CborLdConversionState state = NewState();
        int idsBefore = state.TermToId.Count;

        LinkedDataContext after = await NewScope().WithTypeScopedAsync(
            seed, PlainTypeIris, null, state, TestContext.CancellationToken)
            .ConfigureAwait(false);

        //Same observable terms; no ids allocated.
        Assert.HasCount(idsBefore, state.TermToId);
        Assert.IsTrue(after.TryGetTerm("Plain", out _));
    }

    [TestMethod]
    public async Task WithPropertyScopedAppliesScopedEntries()
    {
        LinkedDataTermSource inner = new("k-inner")
        {
            Iri = "http://example.org/inner",
            IsSimpleString = true
        };
        LinkedDataContextEntry scopedEntry = new(
            new Dictionary<string, LinkedDataTermSource> { ["inner"] = inner },
            baseUrl: null,
            syntheticKey: "prop-scoped");

        TermDefinition propTerm = new()
        {
            IriMapping = "http://example.org/outer",
            ScopedContextEntries = new[] { scopedEntry }
        };

        CborLdConversionState state = NewState();
        LinkedDataContext after = await NewScope().WithPropertyScopedAsync(
            LinkedDataContext.Empty, propTerm, baseUrl: null, state, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(after.TryGetTerm("inner", out TermDefinition? innerDef));
        Assert.AreEqual("http://example.org/inner", innerDef?.IriMapping);

        Assert.IsTrue(state.TermToId.ContainsKey("inner"));
    }

    [TestMethod]
    public async Task WithPropertyScopedReturnsParentWhenNoScopedEntries()
    {
        //A term with no ScopedContextEntries → property-scoped is a no-op
        //and the parent context returns unchanged.
        TermDefinition propTerm = new() { IriMapping = "http://example.org/outer" };

        CborLdConversionState state = NewState();
        LinkedDataContext after = await NewScope().WithPropertyScopedAsync(
            LinkedDataContext.Empty, propTerm, null, state, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreSame(LinkedDataContext.Empty, after);
        Assert.IsEmpty(state.TermToId);
    }

    [TestMethod]
    public async Task EagerAssignmentMatchesEncoderDecoderOrder()
    {
        //Two embedded @contexts applied in sequence. Both encoder and
        //decoder pass through the same sequence, so the id table must
        //populate in the same order on both sides. The test simulates
        //both passes against the same state and asserts identical id
        //assignments.

        CborLdInputMap firstCtx = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("a", new CborLdInputString("http://example.org/a")),
            new KeyValuePair<string, CborLdInputNode>("b", new CborLdInputString("http://example.org/b"))
        });
        CborLdInputMap secondCtx = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("c", new CborLdInputString("http://example.org/c"))
        });

        CborLdConversionState encoderState = NewState();
        CborLdConversionState decoderState = NewState();
        CborLdActiveContextScope scope = NewScope();

        LinkedDataContext encA = await scope.WithEmbeddedContextAsync(
            LinkedDataContext.Empty, firstCtx, null, encoderState, TestContext.CancellationToken).ConfigureAwait(false);
        _ = await scope.WithEmbeddedContextAsync(
            encA, secondCtx, null, encoderState, TestContext.CancellationToken).ConfigureAwait(false);

        LinkedDataContext decA = await scope.WithEmbeddedContextAsync(
            LinkedDataContext.Empty, firstCtx, null, decoderState, TestContext.CancellationToken).ConfigureAwait(false);
        _ = await scope.WithEmbeddedContextAsync(
            decA, secondCtx, null, decoderState, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreSequenceEqual(encoderState.TermToId, decoderState.TermToId, SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual(encoderState.IdToTerm, decoderState.IdToTerm, SequenceOrder.InAnyOrder);
    }
}
