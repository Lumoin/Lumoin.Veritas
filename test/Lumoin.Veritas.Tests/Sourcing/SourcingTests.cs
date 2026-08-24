using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Tests.Sourcing;

/// <summary>
/// Surface contract tests for the <see cref="Lumoin.Veritas.Core.Sourcing"/>
/// types. These tests exist to lock the public contract of the substrate
/// types — return shapes, equality semantics, value carriage — so
/// downstream parallel work can rely on them. They are not behavioural
/// tests of any particular parser or evaluator.
/// </summary>
[TestClass]
internal sealed class SourcingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DocumentIdEqualityIsValueBased()
    {
        DocumentId a = new(0xDEADBEEFCAFEBABE);
        DocumentId b = new(0xDEADBEEFCAFEBABE);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void DocumentIdDistinguishesDifferentHashValues()
    {
        DocumentId a = new(0xAAAA);
        DocumentId b = new(0xBBBB);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void DocumentIdHashIsReadable()
    {
        DocumentId id = new(0x1234567890ABCDEF);

        Assert.AreEqual(0x1234567890ABCDEFUL, id.Hash);
    }

    [TestMethod]
    public void DocumentIdFollowsContentAddressingConvention()
    {
        //Demonstrates the convention. The type does not enforce it, but
        //two parties applying the same VeritasHash to the same bytes
        //produce the same DocumentId. Parser sites construct identifiers
        //this way; persistence reload sites construct directly from a
        //ulong loaded from storage.
        ReadOnlySpan<byte> bytes = "test"u8;

        DocumentId first = new(VeritasHashing.Default(bytes));
        DocumentId second = new(VeritasHashing.Default(bytes));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void DocumentIdConventionDistinguishesDifferentBytes()
    {
        DocumentId a = new(VeritasHashing.Default("alpha"u8));
        DocumentId b = new(VeritasHashing.Default("beta"u8));

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void DocumentIdentityCarriesIdAndMetadata()
    {
        DocumentId id = new(0x1234);
        Uri originUri = new("http://example.org/doc.ttl");

        DocumentIdentity identity = new(id, OriginUri: originUri, MediaType: "text/turtle");

        Assert.AreEqual(id, identity.Id);
        Assert.AreEqual(originUri, identity.OriginUri);
        Assert.AreEqual("text/turtle", identity.MediaType);
    }

    [TestMethod]
    public void DocumentIdentityEqualityCoversAllFields()
    {
        DocumentId id = new(0x5678);

        DocumentIdentity a = new(id, OriginUri: new Uri("http://example.org/a"), MediaType: "text/turtle");
        DocumentIdentity b = new(id, OriginUri: new Uri("http://example.org/b"), MediaType: "text/turtle");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void DocumentIdentityAcceptsNullOriginUri()
    {
        DocumentId id = new(0x5678);

        DocumentIdentity identity = new(id, OriginUri: null, MediaType: "text/turtle");

        Assert.IsNull(identity.OriginUri);
    }

    [TestMethod]
    public void SourceSpanCarriesByteAndLineColumn()
    {
        SourceSpan span = new(
            StartByte: 10,
            EndByte: 14,
            StartLine: 1,
            StartColumn: 0,
            EndLine: 1,
            EndColumn: 4);

        Assert.AreEqual(10, span.StartByte);
        Assert.AreEqual(14, span.EndByte);
        Assert.AreEqual(1, span.StartLine);
        Assert.AreEqual(0, span.StartColumn);
        Assert.AreEqual(1, span.EndLine);
        Assert.AreEqual(4, span.EndColumn);
    }

    [TestMethod]
    public void SourceSpanSingleLineFactoryProducesEqualEndpointsLines()
    {
        SourceSpan span = SourceSpan.SingleLine(
            startByte: 5,
            endByte: 9,
            line: 2,
            startColumn: 4,
            endColumn: 8);

        Assert.AreEqual(span.StartLine, span.EndLine);
        Assert.AreEqual(2, span.StartLine);
        Assert.AreEqual(5, span.StartByte);
        Assert.AreEqual(9, span.EndByte);
    }

    [TestMethod]
    public void SourceSpanSingleLineRejectsBackwardsRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SourceSpan.SingleLine(startByte: 10, endByte: 5, line: 0, startColumn: 0, endColumn: 0));
    }

    [TestMethod]
    public void DocumentNodeRefIsValueEquatable()
    {
        DocumentId docId = new(0x9999);

        DocumentNodeRef a = new(docId, Index: 42);
        DocumentNodeRef b = new(docId, Index: 42);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void DocumentNodeRefDistinguishesDifferentDocuments()
    {
        DocumentNodeRef a = new(new DocumentId(0x1111), Index: 42);
        DocumentNodeRef b = new(new DocumentId(0x2222), Index: 42);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void EmittedQuadAcceptsNullSource()
    {
        Quad quad = CreateExampleQuad();

        EmittedQuad emitted = new(quad, Source: null);

        Assert.IsNull(emitted.Source);
        Assert.AreEqual(quad, emitted.Quad);
    }

    [TestMethod]
    public void EmittedQuadCarriesSourceWhenSupplied()
    {
        Quad quad = CreateExampleQuad();
        DocumentNodeRef source = new(new DocumentId(0xABCD), Index: 7);

        EmittedQuad emitted = new(quad, Source: source);

        Assert.AreEqual(source, emitted.Source);
    }

    [TestMethod]
    public void SolutionDefaultsWitnessesToNull()
    {
        Solution solution = new(bindings: []);

        Assert.IsNull(solution.Witnesses);
    }

    [TestMethod]
    public void SolutionAcceptsExplicitWitnesses()
    {
        Quad quad = CreateExampleQuad();
        EmittedQuad witness = new(quad, Source: null);

        Solution solution = new(bindings: [], witnesses: [witness]);

        Assert.IsNotNull(solution.Witnesses);
        Assert.HasCount(1, solution.Witnesses);
    }

    [TestMethod]
    public void SolutionRejectsNullBindings()
    {
        IReadOnlyList<VariableBinding> nullBindings = null!;

        Assert.Throws<ArgumentNullException>(() => new Solution(nullBindings));
    }

    /// <summary>
    /// Builds a minimal example <see cref="Quad"/> for use in EmittedQuad
    /// and Solution surface tests. The values are arbitrary; the tests
    /// exercise type shape, not graph semantics.
    /// </summary>
    private static Quad CreateExampleQuad()
    {
        NamedNode subject = new(Utf8Strings.From("http://example.org/s"));
        NamedNode predicate = new(Utf8Strings.From("http://example.org/p"));
        NamedNode obj = new(Utf8Strings.From("http://example.org/o"));

        return new Quad(subject, predicate, obj);
    }
}
