using System;
using System.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.MemoryPool;

/// <summary>
/// The poisoning pool itself: a disposed buffer's bytes are overwritten with the sentinel so a stale view reads
/// poison rather than its old contents, and the outstanding-rental count tracks every rent and return so a leak
/// is observable. These are the two properties the owned-buffer pipelines lean on to make a lifetime slip fail a
/// test rather than yield a verified-but-wrong result.
/// </summary>
[TestClass]
internal sealed class PoisoningMemoryPoolTests
{
    /// <summary>A view taken over a rented buffer reads the sentinel, not its written value, once the owner is disposed — the use-after-return the owned-buffer pipelines must never do, made visible.</summary>
    [TestMethod]
    public void DisposingARentedBufferPoisonsItsBytes()
    {
        using PoisoningMemoryPool<byte> pool = new();
        Memory<byte> view;
        using(IMemoryOwner<byte> owner = pool.Rent(16))
        {
            view = owner.Memory[..16];
            view.Span.Fill(0x11);
            Assert.AreEqual((byte)0x11, view.Span[0]);
        }

        bool stillReadsTheWrittenValue = view.Span[0] == 0x11;
        Assert.IsFalse(stillReadsTheWrittenValue, "A returned buffer must be poisoned so a use-after-return is observable.");
        Assert.AreEqual((byte)0xDD, view.Span[0]);
    }

    /// <summary>The outstanding count rises on each rent and falls on each return, so a test can assert balance — an unreturned buffer (a leak) is the count staying above zero.</summary>
    [TestMethod]
    public void OutstandingRentalsTracksRentAndReturn()
    {
        using PoisoningMemoryPool<byte> pool = new();
        Assert.AreEqual(0, pool.OutstandingRentals);

        IMemoryOwner<byte> first = pool.Rent(8);
        IMemoryOwner<byte> second = pool.Rent(8);
        Assert.AreEqual(2, pool.OutstandingRentals);

        first.Dispose();
        Assert.AreEqual(1, pool.OutstandingRentals);

        second.Dispose();
        Assert.AreEqual(0, pool.OutstandingRentals);
    }
}
