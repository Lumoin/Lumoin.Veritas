using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core.Injection;

/// <summary>
/// Tests for the injected-randomness, -identifier, and -blank-node seams in
/// <see cref="VeritasRandomness"/>, <see cref="VeritasIdentifiers"/>, and
/// <see cref="VeritasBlankNodes"/>: range and zero behaviour, seed and
/// counter determinism, per-pool fresh labels, per-solution correlation, and
/// call-site-derived labels.
/// </summary>
[TestClass]
internal sealed class VeritasInjectionSeamsTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary><see cref="VeritasRandomness.System"/> produces a uniform double in the half-open unit interval.</summary>
    [TestMethod]
    public void RandomnessSystemUniformDoubleIsInUnitInterval()
    {
        for(int i = 0; i < 256; i++)
        {
            RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.UniformDouble, default, 0, default));

            Assert.IsTrue(value.Double >= 0.0 && value.Double < 1.0, $"Expected [0,1) but got {value.Double}.");
        }
    }

    /// <summary><see cref="VeritasRandomness.System"/> fills the requested number of entropy bytes.</summary>
    [TestMethod]
    public void RandomnessSystemBytesHonoursRequestedCount()
    {
        RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.Bytes, default, 24, default));

        Assert.HasCount(24, value.Bytes.ToArray());
    }

    /// <summary><see cref="VeritasRandomness.Zero"/> yields zero across every kind.</summary>
    [TestMethod]
    public void RandomnessZeroIsZeroAcrossKinds()
    {
        Assert.AreEqual(0.0, VeritasRandomness.Zero(new RandomnessRequest(RandomnessKind.UniformDouble, default, 0, default)).Double);
        Assert.AreEqual(Guid.Empty, VeritasRandomness.Zero(new RandomnessRequest(RandomnessKind.Uuid, default, 0, default)).Uuid);

        RandomnessValue bytes = VeritasRandomness.Zero(new RandomnessRequest(RandomnessKind.Bytes, default, 8, default));
        Assert.HasCount(8, bytes.Bytes.ToArray());
        Assert.AreSequenceEqual(new byte[8], bytes.Bytes.ToArray());
    }

    /// <summary><see cref="VeritasRandomness.Seeded"/> is reproducible: the same seed and salt replay the same value, and a different salt diverges.</summary>
    [TestMethod]
    public void RandomnessSeededIsReproducible()
    {
        ReadOnlyMemory<byte> salt = new byte[] { 1, 2, 3, 4 };
        RandomnessRequest request = new(RandomnessKind.UniformDouble, default, 0, salt);

        double first = VeritasRandomness.Seeded(99)(request).Double;
        double again = VeritasRandomness.Seeded(99)(request).Double;
        double otherSalt = VeritasRandomness.Seeded(99)(new RandomnessRequest(RandomnessKind.UniformDouble, default, 0, new byte[] { 9, 9 })).Double;

        Assert.AreEqual(first, again);
        Assert.AreNotEqual(first, otherSalt);
    }

    /// <summary><see cref="VeritasIdentifiers.System"/> yields non-empty, distinct identities.</summary>
    [TestMethod]
    public void IdentifiersSystemIsNonEmptyAndDistinct()
    {
        Guid first = VeritasIdentifiers.System(new IdentifierRequest(IdentifierPurpose.Correlation, default));
        Guid second = VeritasIdentifiers.System(new IdentifierRequest(IdentifierPurpose.Correlation, default));

        Assert.AreNotEqual(Guid.Empty, first);
        Assert.AreNotEqual(first, second);
    }

    /// <summary><see cref="VeritasIdentifiers.Zero"/> returns <see cref="Guid.Empty"/>.</summary>
    [TestMethod]
    public void IdentifiersZeroIsEmpty()
    {
        Assert.AreEqual(Guid.Empty, VeritasIdentifiers.Zero(new IdentifierRequest(IdentifierPurpose.General, default)));
    }

    /// <summary><see cref="VeritasIdentifiers.Sequential"/> hands out a deterministic, increasing sequence, identical across instances.</summary>
    [TestMethod]
    public void IdentifiersSequentialIsDeterministic()
    {
        IdentifierDelegate first = VeritasIdentifiers.Sequential();
        IdentifierDelegate second = VeritasIdentifiers.Sequential();

        Guid firstOne = first(new IdentifierRequest(IdentifierPurpose.General, default));
        Guid firstTwo = first(new IdentifierRequest(IdentifierPurpose.General, default));
        Guid secondOne = second(new IdentifierRequest(IdentifierPurpose.General, default));

        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), firstOne);
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000002"), firstTwo);
        Assert.AreEqual(firstOne, secondOne);
    }

    /// <summary><see cref="VeritasBlankNodes.System"/> hands out fresh <c>b0</c>, <c>b1</c>, … labels counted per pool.</summary>
    [TestMethod]
    public void BlankNodesSystemFreshLabelsCountPerPool()
    {
        using Utf8StringPool pool = new();
        BlankNodeRequest request = new(Guid.Empty, ReadOnlyMemory<byte>.Empty, SourceSpan.None, pool);

        Assert.AreEqual(pool.Intern("b0"), VeritasBlankNodes.System(request));
        Assert.AreEqual(pool.Intern("b1"), VeritasBlankNodes.System(request));
        Assert.AreEqual(pool.Intern("b2"), VeritasBlankNodes.System(request));
    }

    /// <summary><see cref="VeritasBlankNodes.System"/> reuses one label per correlation key within a solution and allocates a new one for a different solution.</summary>
    [TestMethod]
    public void BlankNodesSystemCorrelatesPerSolution()
    {
        using Utf8StringPool pool = new();
        ReadOnlyMemory<byte> key = new byte[] { 7, 7, 7 };
        Guid solutionA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        Guid solutionB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

        Utf8String firstInA = VeritasBlankNodes.System(new BlankNodeRequest(solutionA, key, SourceSpan.None, pool));
        Utf8String againInA = VeritasBlankNodes.System(new BlankNodeRequest(solutionA, key, SourceSpan.None, pool));
        Utf8String inB = VeritasBlankNodes.System(new BlankNodeRequest(solutionB, key, SourceSpan.None, pool));

        Assert.AreEqual(firstInA, againInA);
        Assert.AreNotEqual(firstInA, inB);
    }

    /// <summary><see cref="VeritasBlankNodes.ByCallSite"/> derives the label from the source byte offset, independent of allocation order.</summary>
    [TestMethod]
    public void BlankNodesByCallSiteUsesByteOffset()
    {
        using Utf8StringPool pool = new();
        BlankNodeRequest request = new(Guid.Empty, ReadOnlyMemory<byte>.Empty, SourceSpan.SingleLine(123, 124, 0, 0, 1), pool);

        Assert.AreEqual(pool.Intern("b123"), VeritasBlankNodes.ByCallSite(request));
    }
}
