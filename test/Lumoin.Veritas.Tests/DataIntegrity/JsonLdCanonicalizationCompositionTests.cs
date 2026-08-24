using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Tests.DataIntegrity;

/// <summary>
/// Proves Veritas already provides the RDF primitive a Data Integrity <c>CanonicalizationDelegate</c>
/// needs for selective disclosure (<c>ecdsa-sd-2023</c>): JSON-LD expand → toRdf quads →
/// RDFC-1.0 canonicalization, yielding canonical N-Quads <em>and</em> the issued-identifier (blank-node)
/// label map that joins the reduced and full canonicalizations. A consumer wraps exactly this chain in
/// its delegate without any further RDF library.
/// </summary>
[TestClass]
internal sealed class JsonLdCanonicalizationCompositionTests
{
    /// <summary>Gets or sets the test execution context (supplies the cancellation token).</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string CredentialJson = """
        {
          "@context": {
            "ex": "http://example.org/vocab#",
            "name": "ex:name",
            "knows": { "@id": "ex:knows", "@type": "@id" },
            "Person": "ex:Person"
          },
          "@type": "Person",
          "name": "Alice",
          "ex:friend": { "@type": "Person", "name": "Bob" }
        }
        """;

    /// <summary>A JSON-LD document expands, serializes to RDF, and canonicalizes to N-Quads with a non-empty blank-node label map — the <c>(CanonicalForm, LabelMap)</c> a Data Integrity canonicalization delegate returns.</summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task JsonLdDocumentCanonicalizesToNQuadsWithLabelMap()
    {
        JsonNode document = StjJsonAdapter.Parse(Utf8Strings.From(CredentialJson));
        ContextResolverDelegate noRemoteContexts = (uri, cancellationToken) => ValueTask.FromResult<Utf8String?>(null);

        IReadOnlyList<object?> expanded = await JsonLdExpansionTree.ExpandAsync(
            document,
            baseUrl: "https://example.org/credential",
            noRemoteContexts,
            StjJsonAdapter.Parse,
            TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        List<Quad> quads = JsonLdRdfSerializer.Serialize(expanded, pool);

        HashDelegate sha256 = SHA256.HashData;
        RdfCanonicalizationResult result = RdfCanonicalizer.CanonicalizeWithMap(quads, sha256);

        //Two anonymous nodes (the subject and the nested friend) become canonical blank nodes, so the
        //canonical N-Quads carry c14n labels and the issued-identifier map — what ecdsa-sd joins on — is non-empty.
        Assert.IsGreaterThan(0, quads.Count);
        Assert.Contains("_:c14n", result.Canonical);
        Assert.IsGreaterThan(0, result.IssuedIdentifiers.Count);
    }
}
