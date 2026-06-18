using CsCheck;
using System.IO.Pipelines;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.NQuads;

namespace Lumoin.Veritas.ParserTests.NQuads;

/// <summary>
/// CsCheck-driven property tests over the source-aware
/// <see cref="NQuadsReader.ReadWithSourceAsync"/> overload. The
/// property verifies that for any valid quad set written through
/// <see cref="NQuadsWriter"/>, reading back through the source-aware
/// overload produces quads byte-identical to those produced by the
/// bare <see cref="NQuadsReader.ReadAsync"/> overload, with sequential
/// document-node indexes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sync bridge to async reader.</b> CsCheck's <c>Sample</c> takes
/// a synchronous lambda. The reader returns an
/// <see cref="IAsyncEnumerable{T}"/>. The properties bridge with
/// <c>GetAwaiter().GetResult()</c> on the materialisation task. This
/// is the standard sync-over-async idiom for test contexts; the
/// reader has no synchronization context and no meaningful blocking
/// work, so there is no deadlock risk.
/// </para>
/// </remarks>
[TestClass]
internal sealed class NQuadsReaderSourcePropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PropertyBareAndSourceAwareYieldSameQuads()
    {
        //For any randomly generated valid quad set written via
        //NQuadsWriter, both reader overloads produce the same Quads in
        //the same order, and the source-aware overload assigns
        //sequential document-node indexes starting at zero.
        QuadSetGenerator().Sample(quads =>
        {
            using MemoryStream stream = new();

            NQuadsWriter.WriteAsync(quads, PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)), TestContext.CancellationToken)
                .AsTask().GetAwaiter().GetResult();

            byte[] bytes = stream.ToArray();

            List<Quad> bare = ReadBareAsync(bytes).GetAwaiter().GetResult();

            DocumentId documentId = new(0xABCD);
            List<EmittedQuad> sourced = ReadSourcedAsync(bytes, documentId).GetAwaiter().GetResult();

            Assert.HasCount(bare.Count, sourced);

            for(int i = 0; i < bare.Count; i++)
            {
                Assert.AreEqual(bare[i], sourced[i].Quad);
                Assert.AreEqual(i, sourced[i].Source!.Value.Index);
                Assert.AreEqual(documentId, sourced[i].Source!.Value.DocumentId);
            }
        });
    }

    private static async Task<List<Quad>> ReadBareAsync(byte[] bytes)
    {
        List<Quad> result = [];
        await foreach(Quad q in NQuadsReader.ReadAsync(new ReadOnlyMemory<byte>(bytes)).ConfigureAwait(false))
        {
            result.Add(q);
        }
        return result;
    }

    private static async Task<List<EmittedQuad>> ReadSourcedAsync(byte[] bytes, DocumentId documentId)
    {
        List<EmittedQuad> result = [];
        await foreach(EmittedQuad e in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(bytes), documentId).ConfigureAwait(false))
        {
            result.Add(e);
        }
        return result;
    }

    /// <summary>
    /// Generates random arrays of valid <see cref="Quad"/> instances using
    /// simple ASCII-only IRIs, blank-node labels, and literal values to
    /// stay clear of escape-sequence concerns. The point of the property
    /// test is to verify the bare/source-aware reader equivalence across
    /// many shapes of valid input — not to stress the writer's escaping
    /// path. Term-level coverage includes <see cref="NamedNode"/>,
    /// <see cref="BlankNode"/>, plain literals, datatyped literals, and
    /// language-tagged literals; quads vary in whether they carry a
    /// graph component.
    /// </summary>
    private static Gen<Quad[]> QuadSetGenerator()
    {
        Gen<NamedNode> namedNode = Gen.Int[0, 999].Select(i =>
            new NamedNode(Utf8String.FromString($"http://example.org/n{i}")));

        Gen<BlankNode> blankNode = Gen.Int[0, 99].Select(i =>
            new BlankNode(Utf8String.FromString($"b{i}")));

        Gen<Literal> plainLiteral = Gen.Int[0, 99].Select(i =>
            new Literal(
                Utf8String.FromString($"value{i}"),
                new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#string"))));

        Gen<Literal> datatypedLiteral = Gen.Int[0, 99].Select(i =>
            new Literal(
                Utf8String.FromString($"{i}"),
                new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#integer"))));

        Gen<Literal> langLiteral = Gen.Int[0, 99].Select(i =>
            new Literal(
                Utf8String.FromString($"value{i}"),
                new NamedNode(Utf8String.FromString("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                Utf8String.FromString("en")));

        Gen<RdfTerm> subject = Gen.Int[0, 1].SelectMany(i => i == 0
            ? namedNode.Select(n => (RdfTerm)n)
            : blankNode.Select(b => (RdfTerm)b));

        Gen<RdfTerm> @object = Gen.Int[0, 4].SelectMany(i => i switch
        {
            0 => namedNode.Select(n => (RdfTerm)n),
            1 => blankNode.Select(b => (RdfTerm)b),
            2 => plainLiteral.Select(l => (RdfTerm)l),
            3 => datatypedLiteral.Select(l => (RdfTerm)l),
            _ => langLiteral.Select(l => (RdfTerm)l),
        });

        Gen<RdfTerm?> graph = Gen.Int[0, 2].SelectMany(i => i switch
        {
            0 => namedNode.Select(n => (RdfTerm?)n),
            1 => blankNode.Select(b => (RdfTerm?)b),
            _ => Gen.Const((RdfTerm?)null),
        });

        Gen<Quad> quad =
            from s in subject
            from p in namedNode
            from o in @object
            from g in graph
            select new Quad(s, p, o, g);

        return quad.Array[0, 15];
    }
}
