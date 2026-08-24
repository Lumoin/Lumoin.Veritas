using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The worlds wire-document rows: the listing carries names, sixteen-hex-digit state identifiers, and
/// fork lineage; the diff document is bounded with exact totals beside capped listings; the outcome
/// documents carry their tokens; and terms cross as JSON-escaped lexical forms. The discriminating row
/// is the cap one — a diff document that listed more triples than its cap, or lied about its totals,
/// fails it.
/// </summary>
[TestClass]
internal sealed partial class WorldsJsonTests
{
    /// <summary>The example-namespace prefix the data and updates share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The what-if world name the rows fork under.</summary>
    private const string WhatIf = "what-if";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The compiled pattern matching a listing entry's sixteen-lowercase-hex-digit state identifier (source-generated, so no per-call regex compilation).</summary>
    [GeneratedRegex("\"stateId\":\"([0-9a-f]{16})\"")]
    private static partial Regex StateIdRegex();

    /// <summary>The listing document carries each world's name, its state identifier as sixteen hex digits, and its fork parent — and an undiverged fork shares the primary world's identifier on the wire, the content addressing crossing intact.</summary>
    [TestMethod]
    public async Task TheWorldsListingCarriesNameStateIdAndParent()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));

        string listing = WorldsJson.WriteWorlds(engine.DescribeWorlds());
        Assert.Contains("\"name\":\"main\"", listing);
        Assert.Contains("\"parent\":null", listing);
        Assert.Contains("\"name\":\"what-if\"", listing);
        Assert.Contains("\"parent\":\"main\"", listing);
        Assert.IsLessThan(listing.IndexOf("\"name\":\"what-if\"", StringComparison.Ordinal), listing.IndexOf("\"name\":\"main\"", StringComparison.Ordinal), "The primary world leads the listing: " + listing);

        MatchCollection stateIds = StateIdRegex().Matches(listing);
        Assert.HasCount(2, stateIds, "Every entry carries a sixteen-hex-digit state identifier: " + listing);
        Assert.AreEqual(stateIds[0].Groups[1].Value, stateIds[1].Groups[1].Value, "An undiverged fork shares the primary world's state identifier on the wire.");
    }

    /// <summary>The diff document tells truncated-at-N-of-M truth: with a cap below the triple count it lists exactly the cap's worth and keeps the exact totals, and with the default cap it lists everything and marks itself untruncated.</summary>
    [TestMethod]
    public async Task TheDiffDocumentKeepsTotalsExactBeyondTheCap()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s> <{Ex}p> \"h1\" , \"h2\" , \"h3\" }}"), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        WorldDiff diff = engine.DiffWorlds(WhatIf, WellKnownWorlds.Primary);
        Assert.AreEqual(WorldDiffOutcome.Diffed, diff.Outcome);

        string capped = WorldsJson.WriteDiff(in diff, tripleCap: 2);
        Assert.Contains("\"outcome\":\"diffed\"", capped);
        Assert.Contains("\"cap\":2", capped);
        Assert.Contains("\"totalTransitions\":1", capped);
        Assert.Contains("\"totalTriples\":3", capped);
        Assert.Contains("\"truncated\":true", capped);
        Assert.Contains("\"totalAdditions\":3", capped);
        Assert.AreEqual(2, CountOccurrences(capped, "{\"s\":"), "A capped document lists exactly the cap's worth of triples: " + capped);

        string whole = WorldsJson.WriteDiff(in diff);
        Assert.Contains("\"truncated\":false", whole);
        Assert.AreEqual(3, CountOccurrences(whole, "{\"s\":"), "The default cap lists the whole delta: " + whole);
    }

    /// <summary>The outcome documents carry exactly their wire tokens, and an unknown world diffs to the bare outcome document.</summary>
    [TestMethod]
    public async Task TheOutcomeDocumentsCarryTheirTokens()
    {
        Assert.AreEqual("{\"outcome\":\"forked\"}", WorldsJson.WriteForkOutcome(WorldForkOutcome.Forked));
        Assert.AreEqual("{\"outcome\":\"unknownSource\"}", WorldsJson.WriteForkOutcome(WorldForkOutcome.UnknownSource));
        Assert.AreEqual("{\"outcome\":\"duplicateName\"}", WorldsJson.WriteForkOutcome(WorldForkOutcome.DuplicateName));
        Assert.AreEqual("{\"outcome\":\"dropped\"}", WorldsJson.WriteDropOutcome(WorldDropOutcome.Dropped));
        Assert.AreEqual("{\"outcome\":\"unknownWorld\"}", WorldsJson.WriteDropOutcome(WorldDropOutcome.UnknownWorld));
        Assert.AreEqual("{\"outcome\":\"primaryWorld\"}", WorldsJson.WriteDropOutcome(WorldDropOutcome.PrimaryWorld));

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        WorldDiff unknown = engine.DiffWorlds("missing", WellKnownWorlds.Primary);
        Assert.AreEqual("{\"outcome\":\"unknownWorld\"}", WorldsJson.WriteDiff(in unknown));
    }

    /// <summary>Terms cross as their lexical forms with JSON escaping applied: a literal value carrying a quote and a newline reaches the document escaped, never raw.</summary>
    [TestMethod]
    public async Task DiffTermsCrossAsEscapedLexicalForms()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s> <{Ex}p> \"say \\\"hi\\\"\\n\" }}"), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        WorldDiff diff = engine.DiffWorlds(WhatIf, WellKnownWorlds.Primary);
        Assert.AreEqual(WorldDiffOutcome.Diffed, diff.Outcome);

        string document = WorldsJson.WriteDiff(in diff);
        Assert.Contains("\"s\":\"<" + Ex + "s>\"", document);
        Assert.Contains("say \\\"hi\\\"\\n", document, "The quote and the newline cross escaped: " + document);
    }

    /// <summary>Counts the non-overlapping occurrences of a marker in a document.</summary>
    /// <param name="document">The document text.</param>
    /// <param name="marker">The marker to count.</param>
    /// <returns>The occurrence count.</returns>
    private static int CountOccurrences(string document, string marker)
    {
        int count = 0;
        int index = document.IndexOf(marker, StringComparison.Ordinal);
        while(index >= 0)
        {
            count++;
            index = document.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>The base default-graph data: one value under the path.</summary>
    /// <returns>The triples.</returns>
    private static List<DataTriple> BaseData()
    {
        return [new DataTriple(Iri(Ex + "s"), Iri(Ex + "p"), Str("real"))];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds an <c>xsd:string</c> literal.</summary>
    /// <param name="value">The literal value.</param>
    /// <returns>The literal term.</returns>
    private static Literal Str(string value)
    {
        return new Literal(Utf8Strings.From(value), Iri("http://www.w3.org/2001/XMLSchema#string"));
    }
}
