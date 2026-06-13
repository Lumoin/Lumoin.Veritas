using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Tests.ContentAddressing;

/// <summary>
/// The 128-bit content-key kernel: the single-register Vector128 path and
/// the two-step word-parallel portable path agree on every vector, XOR
/// obeys its reconciliation algebra (self-inverse, identity), equality is
/// exact across both words, and the GUID and byte conversions round-trip.
/// Vectors are fixed (no entropy source) so the agreement is deterministic.
/// </summary>
[TestClass]
internal sealed class ContentKey128KernelTests
{
    /// <summary>A spread of fixed key pairs: equal, all-bits, low-only and high-only differences, and arbitrary words.</summary>
    private static IEnumerable<(ContentKey128 Left, ContentKey128 Right)> Pairs()
    {
        yield return (new ContentKey128(0, 0), new ContentKey128(0, 0));
        yield return (new ContentKey128(ulong.MaxValue, ulong.MaxValue), new ContentKey128(0, 0));
        yield return (new ContentKey128(0x0123456789ABCDEF, 0xFEDCBA9876543210), new ContentKey128(0x1111111111111111, 0x2222222222222222));
        yield return (new ContentKey128(1, 0), new ContentKey128(0, 0));
        yield return (new ContentKey128(0, 1), new ContentKey128(0, 0));
        yield return (new ContentKey128(0xDEADBEEFCAFEF00D, 0x8000000000000001), new ContentKey128(0xDEADBEEFCAFEF00D, 0x8000000000000001));
    }

    [TestMethod]
    public void TheVectorAndPortablePathsAgreeOnEveryVector()
    {
        foreach((ContentKey128 left, ContentKey128 right) in Pairs())
        {
            //XOR agrees across paths, and the public entry agrees with both.
            ContentKey128 vectorXor = ContentKey128Kernel.XorVector128(left, right);
            Assert.AreEqual(ContentKey128Kernel.XorPortable(left, right), vectorXor);
            Assert.AreEqual(vectorXor, ContentKey128Kernel.Xor(left, right));

            //Equality agrees across paths, and the public entry agrees with both.
            bool vectorEqual = ContentKey128Kernel.AreEqualVector128(left, right);
            Assert.AreEqual(ContentKey128Kernel.AreEqualPortable(left, right), vectorEqual);
            Assert.AreEqual(vectorEqual, ContentKey128Kernel.AreEqual(left, right));
        }
    }

    [TestMethod]
    public void XorObeysItsReconciliationAlgebra()
    {
        ContentKey128 key = new(0x0123456789ABCDEF, 0xFEDCBA9876543210);
        ContentKey128 other = new(0xAABBCCDDEEFF0011, 0x2233445566778899);

        //Self-inverse: a key XORed with itself cancels to zero — matched reconciliation items vanish.
        Assert.AreEqual(ContentKey128.Zero, ContentKey128Kernel.Xor(key, key));

        //Identity: XOR with zero is a no-op.
        Assert.AreEqual(key, ContentKey128Kernel.Xor(key, ContentKey128.Zero));

        //Involution: XORing the same operand twice restores the original.
        Assert.AreEqual(key, ContentKey128Kernel.Xor(ContentKey128Kernel.Xor(key, other), other));
    }

    [TestMethod]
    public void EqualityIsExactAcrossBothWords()
    {
        ContentKey128 key = new(0x0123456789ABCDEF, 0xFEDCBA9876543210);

        Assert.IsTrue(ContentKey128Kernel.AreEqual(key, key));

        //A difference confined to either word alone is detected.
        Assert.IsFalse(ContentKey128Kernel.AreEqual(key, key with { Low = key.Low ^ 1 }));
        Assert.IsFalse(ContentKey128Kernel.AreEqual(key, key with { High = key.High ^ 1 }));
    }

    [TestMethod]
    public void GuidAndByteConversionsRoundTrip()
    {
        Guid guid = new("0f9e8d7c-6b5a-4938-2716-fedcba987654");

        ContentKey128 key = ContentKey128.FromGuid(guid);
        Assert.AreEqual(guid, key.ToGuid());

        Span<byte> bytes = stackalloc byte[ContentKey128.ByteWidth];
        key.WriteBytes(bytes);
        Assert.AreEqual(key, ContentKey128.FromBytes(bytes));
    }

    [TestMethod]
    public void ShortBuffersAreRejected()
    {
        ContentKey128 key = new(1, 2);

        Assert.ThrowsExactly<ArgumentException>(() => ContentKey128.FromBytes(new byte[8]));
        Assert.ThrowsExactly<ArgumentException>(() => key.WriteBytes(new byte[8]));
    }
}
