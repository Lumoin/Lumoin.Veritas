using CsCheck;
using Lumoin.Veritas.Cid;

namespace Lumoin.Veritas.Tests.Cid;

/// <summary>
/// CsCheck-driven property tests over the CID parser and formatter. The
/// invariant is that <c>Parse(Format(cid))</c> equals <c>cid</c> for every
/// well-formed pair of (codec, 32-byte digest).
/// </summary>
[TestClass]
internal sealed class CidPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void StringFormRoundTripPreservesCid()
    {
        CidGenerator().Sample(cid =>
        {
            string text = CidFormatter.ToCanonicalString(cid);
            Veritas.Cid.Cid parsed = CidParser.Parse(text);

            Assert.AreEqual(cid.Codec, parsed.Codec);
            Assert.AreEqual(cid.Digest, parsed.Digest);
        });
    }

    [TestMethod]
    public void BinaryFormRoundTripPreservesCid()
    {
        CidGenerator().Sample(cid =>
        {
            byte[] bytes = CidFormatter.ToBytes(cid);
            Veritas.Cid.Cid parsed = CidParser.Parse(bytes);

            Assert.AreEqual(cid.Codec, parsed.Codec);
            Assert.AreEqual(cid.Digest, parsed.Digest);
        });
    }

    private static Gen<Veritas.Cid.Cid> CidGenerator()
    {
        Gen<CidCodec> codec = Gen.Bool.Select(b => b ? CidCodec.Raw : CidCodec.Drisl);
        Gen<byte[]> digest = Gen.Int[0, 255].Select(i => (byte)i).Array[32];

        return
            from c in codec
            from d in digest
            select new Veritas.Cid.Cid { Codec = c, Digest = Digest32.FromSpan(d) };
    }
}
