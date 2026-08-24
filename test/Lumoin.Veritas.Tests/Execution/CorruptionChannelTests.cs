using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The corruption-channel descriptor: the built-in identities, the memory-error-correction
/// applicability flag the scrub-cadence estimator keys on, equality by code, and consumer extension.
/// </summary>
[TestClass]
internal sealed class CorruptionChannelTests
{
    /// <summary>The memory channel is error-correctable; the storage channel is not.</summary>
    [TestMethod]
    public void BuiltInChannelsCarryTheirErrorCorrectionApplicability()
    {
        Assert.IsTrue(CorruptionChannel.Memory.IsReducedByMemoryErrorCorrection);
        Assert.IsFalse(CorruptionChannel.Storage.IsReducedByMemoryErrorCorrection);
    }

    /// <summary>The built-ins have distinct, stable codes.</summary>
    [TestMethod]
    public void BuiltInChannelsHaveDistinctCodes()
    {
        Assert.AreNotEqual(CorruptionChannel.Memory.Code, CorruptionChannel.Storage.Code);
    }

    /// <summary>Equality is by code.</summary>
    [TestMethod]
    public void EqualityIsByCode()
    {
        CorruptionChannel memoryFirst = CorruptionChannel.Memory;
        CorruptionChannel memorySecond = CorruptionChannel.Memory;

        Assert.AreEqual(memoryFirst, memorySecond);
        Assert.AreNotEqual(CorruptionChannel.Memory, CorruptionChannel.Storage);
    }

    /// <summary>A consumer can create a custom channel with its own code, name, and applicability.</summary>
    [TestMethod]
    public void CreateMakesACustomChannel()
    {
        CorruptionChannel network = CorruptionChannel.Create(100, "Network", isReducedByMemoryErrorCorrection: false);

        Assert.AreEqual(100, network.Code);
        Assert.AreEqual("Network", network.Name);
        Assert.IsFalse(network.IsReducedByMemoryErrorCorrection);
    }

    /// <summary>The reserved zero code is rejected.</summary>
    [TestMethod]
    public void CreateRejectsTheReservedZeroCode()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CorruptionChannel.Create(0, "Invalid", isReducedByMemoryErrorCorrection: false); });
    }
}
