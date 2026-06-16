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
