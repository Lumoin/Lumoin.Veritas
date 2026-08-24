using System;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The pluggable checksum descriptor: the built-ins resolve by their stable id, the reserved-none
/// and unknown ids resolve to nothing, the construction guards hold, and a built-in checksum is
/// deterministic at its declared width.
/// </summary>
[TestClass]
internal sealed class ChecksumAlgorithmTests
{
    /// <summary>A no-op compute used where only construction is under test.</summary>
    /// <param name="data">Ignored.</param>
    /// <param name="destination">Ignored.</param>
    private static void NoopCompute(ReadOnlySpan<byte> data, Span<byte> destination)
    {
    }

    /// <summary>The default resolver maps the built-in ids to their algorithms and returns null for the reserved and unknown ids.</summary>
    [TestMethod]
    public void DefaultResolverMapsBuiltInsAndRejectsUnknown()
    {
        Assert.AreSame(ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.XxHash3.Id));
        Assert.AreSame(ChecksumAlgorithm.Crc32, ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.Crc32.Id));
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(0));
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(99));
    }

    /// <summary>The reserved keyed ids name a keyed message-authentication tag a host composes with a key; the built-in resolver never resolves them, so a keyless composition refuses a keyed image rather than downgrading it.</summary>
    [TestMethod]
    public void DefaultResolverDoesNotResolveReservedKeyedIds()
    {
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.KeyedHmacSha256Id));
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.KeyedBlake2b256Id));
    }

    /// <summary>Id 0 is reserved for "no checksum" and cannot name an algorithm.</summary>
    [TestMethod]
    public void ReservedIdIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = ChecksumAlgorithm.Create(0, "reserved", sizeof(ulong), NoopCompute); });
    }

    /// <summary>The byte width must lie within the admitted bound so the on-load verify buffer is bounded.</summary>
    [TestMethod]
    public void ByteWidthBoundsAreEnforced()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = ChecksumAlgorithm.Create(50, "too-narrow", 0, NoopCompute); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = ChecksumAlgorithm.Create(50, "too-wide", ChecksumAlgorithm.MaximumByteWidth + 1, NoopCompute); });
    }

    /// <summary>A resolver that answers every id with XxHash3 — the misrouting composition the resolution witness must refuse.</summary>
    /// <param name="id">The requested id; ignored.</param>
    /// <returns>Always <see cref="ChecksumAlgorithm.XxHash3"/>.</returns>
    private static ChecksumAlgorithm? AnswerEverythingWithXxHash3(byte id)
    {
        return ChecksumAlgorithm.XxHash3;
    }

    /// <summary>The reserved keyed ids cannot be minted keyless: the open extension point refuses them and directs to the keyed factory.</summary>
    [TestMethod]
    public void CreateRefusesTheReservedKeyedIds()
    {
        Assert.ThrowsExactly<ArgumentException>(() => { _ = ChecksumAlgorithm.Create(ChecksumAlgorithm.KeyedHmacSha256Id, "keyless-under-keyed", 32, NoopCompute); });
        Assert.ThrowsExactly<ArgumentException>(() => { _ = ChecksumAlgorithm.Create(ChecksumAlgorithm.KeyedBlake2b256Id, "keyless-under-keyed", 32, NoopCompute); });
    }

    /// <summary>The keyed factory marks the algorithm, binds the reserved ids to their permanent tag width, and leaves custom keyed ids width-free; the open keyless factory never marks.</summary>
    [TestMethod]
    public void CreateKeyedMarksTheAlgorithmAndBindsTheReservedWidth()
    {
        ChecksumAlgorithm keyed = ChecksumAlgorithm.CreateKeyed(ChecksumAlgorithm.KeyedHmacSha256Id, "HMAC-SHA-256", ChecksumAlgorithm.ReservedKeyedByteWidth, NoopCompute);
        Assert.IsTrue(keyed.IsKeyed, "The keyed factory must mark its algorithm.");
        Assert.AreEqual(ChecksumAlgorithm.KeyedHmacSha256Id, keyed.Id);
        Assert.AreEqual(ChecksumAlgorithm.ReservedKeyedByteWidth, keyed.ByteWidth);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = ChecksumAlgorithm.CreateKeyed(ChecksumAlgorithm.KeyedHmacSha256Id, "wrong-width", 16, NoopCompute); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = ChecksumAlgorithm.CreateKeyed(ChecksumAlgorithm.KeyedBlake2b256Id, "wrong-width", 31, NoopCompute); });

        Assert.IsTrue(ChecksumAlgorithm.CreateKeyed(200, "custom-keyed", 8, NoopCompute).IsKeyed, "A custom keyed id takes any admitted width under the deployment's own policy.");
        Assert.IsFalse(ChecksumAlgorithm.Create(50, "custom-keyless", 8, NoopCompute).IsKeyed, "The open extension point never marks keyed.");
        Assert.IsFalse(ChecksumAlgorithm.XxHash3.IsKeyed);
        Assert.IsFalse(ChecksumAlgorithm.Crc32.IsKeyed);
    }

    /// <summary>The resolution witness resolves honest compositions exactly as the raw resolver did, and keeps the unknown-id and keyless-keyed refusals as the unsupported-image exception.</summary>
    [TestMethod]
    public void TheResolutionWitnessResolvesHonestCompositions()
    {
        Assert.AreSame(ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.ResolveForRead(ChecksumAlgorithm.XxHash3.Id, null, "test artifact"));
        Assert.AreSame(ChecksumAlgorithm.Crc32, ChecksumAlgorithm.ResolveForRead(ChecksumAlgorithm.Crc32.Id, ChecksumAlgorithm.DefaultResolver, "test artifact"));
        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ChecksumAlgorithm.ResolveForRead(99, null, "test artifact"); });
        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ChecksumAlgorithm.ResolveForRead(ChecksumAlgorithm.KeyedHmacSha256Id, null, "test artifact"); });
    }

    /// <summary>The resolution witness refuses a resolver whose answer's identity differs from the requested id, before any verification could run under the wrong algorithm.</summary>
    [TestMethod]
    public void TheResolutionWitnessRefusesAMisroutedAnswer()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = ChecksumAlgorithm.ResolveForRead(ChecksumAlgorithm.Crc32.Id, AnswerEverythingWithXxHash3, "test artifact"); });
        Assert.AreSame(ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.ResolveForRead(ChecksumAlgorithm.XxHash3.Id, AnswerEverythingWithXxHash3, "test artifact"), "An answer whose identity happens to match is honest for that id.");
    }

    /// <summary>A built-in checksum is deterministic and writes its declared width.</summary>
    [TestMethod]
    public void BuiltInChecksumIsDeterministicAtDeclaredWidth()
    {
        Assert.AreEqual(sizeof(ulong), ChecksumAlgorithm.XxHash3.ByteWidth);
        Assert.AreEqual(sizeof(uint), ChecksumAlgorithm.Crc32.ByteWidth);

        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Span<byte> first = stackalloc byte[ChecksumAlgorithm.XxHash3.ByteWidth];
        Span<byte> second = stackalloc byte[ChecksumAlgorithm.XxHash3.ByteWidth];
        ChecksumAlgorithm.XxHash3.Compute(data, first);
        ChecksumAlgorithm.XxHash3.Compute(data, second);

        Assert.IsTrue(first.SequenceEqual(second), "A checksum must be a deterministic function of its input.");
    }
}
