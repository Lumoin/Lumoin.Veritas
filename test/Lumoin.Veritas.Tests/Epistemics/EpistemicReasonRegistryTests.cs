using System;
using Lumoin.Veritas.Core.Epistemics;

namespace Lumoin.Veritas.Tests.Epistemics;

/// <summary>
/// The epistemic-reason registry acceptance ladder red/green: the three rungs (duplicate/collision,
/// shape sanity, self-test) rejecting each malformed registration by name, the deferred-versus-undeclared
/// coverage distinction, the declared-projection resolution, the empty-registry singleton, the miss-and-hit
/// lookups, the null-checked chainable <see cref="EpistemicReasonRegistryBuilder.Add"/>, and a multi-family
/// build resolving every registered code.
/// </summary>
[TestClass]
internal sealed class EpistemicReasonRegistryTests
{
    /// <summary>The class family reserving band index 1.</summary>
    private static EpistemicReasonClassFamily FamilyOne => new(1, "FamilyOne"u8.ToArray());

    /// <summary>A second class family reserving band index 2.</summary>
    private static EpistemicReasonClassFamily FamilyTwo => new(2, "FamilyTwo"u8.ToArray());

    /// <summary>Rung 1 rejects two registrations sharing one code int, naming the duplicate/collision rung.</summary>
    [TestMethod]
    public void DuplicateCodeIntThrowsNamingRungOne()
    {
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "First"u8.ToArray(), "Why one."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Add(Registration(FamilyOne, 10000, "Second"u8.ToArray(), "Why two."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 1", error.Message);
        Assert.Contains("already registered", error.Message);
    }

    /// <summary>Rung 1 rejects two registrations sharing one canonical name.</summary>
    [TestMethod]
    public void DuplicateCanonicalNameThrows()
    {
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "SameName"u8.ToArray(), "Why one."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Add(Registration(FamilyOne, 10001, "SameName"u8.ToArray(), "Why two."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("canonical name is already registered", error.Message);
    }

    /// <summary>Rung 1 rejects two different family names reserving the same band index.</summary>
    [TestMethod]
    public void TwoFamilyNamesSharingABandIndexThrow()
    {
        EpistemicReasonClassFamily otherName = new(1, "DifferentName"u8.ToArray());
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "First"u8.ToArray(), "Why one."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Add(Registration(otherName, 10001, "Second"u8.ToArray(), "Why two."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("reserved by a different family name", error.Message);
    }

    /// <summary>Rung 2 rejects a code that falls outside its own family's band (a single-registration shape check, not a pairwise collision).</summary>
    [TestMethod]
    public void CodeOutsideItsFamilyBandThrows()
    {
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 20000, "Stray"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("falls outside its family band", error.Message);
    }

    /// <summary>Rung 2 rejects a band-0 family with the reserved-invalid default code.</summary>
    [TestMethod]
    public void BandZeroDefaultCodeThrowsRungTwo()
    {
        EpistemicReasonClassFamily bandZero = new(0, "Reserved"u8.ToArray());
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(bandZero, 0, "Invalid"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("reserved-invalid", error.Message);
    }

    /// <summary>Rung 2 rejects a family whose canonical name is empty (a band reservation naming no family).</summary>
    [TestMethod]
    public void EmptyFamilyNameThrowsRungTwo()
    {
        EpistemicReasonClassFamily blankName = new(1, ReadOnlyMemory<byte>.Empty);
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(blankName, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("family name is empty", error.Message);
    }

    /// <summary>Rung 2 rejects an empty canonical name.</summary>
    [TestMethod]
    public void EmptyCanonicalNameThrowsRungTwo()
    {
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, ReadOnlyMemory<byte>.Empty, "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("canonical name is empty", error.Message);
    }

    /// <summary>Rung 2 rejects an empty explanation.</summary>
    [TestMethod]
    public void EmptyExplanationThrowsRungTwo()
    {
        EpistemicReasonRegistryBuilder builder = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), ReadOnlyMemory<byte>.Empty, EpistemicProjectionCoverage.Deferred));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => builder.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("explanation is empty", error.Message);
    }

    /// <summary>Rung 2 rejects the undeclared default coverage, while an explicit deferred declaration passes.</summary>
    [TestMethod]
    public void UndeclaredCoverageThrowsRungTwoWhileDeferredPasses()
    {
        EpistemicReasonRegistryBuilder undeclared = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), default));

        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => undeclared.Build());
        Assert.Contains("Rung 2", error.Message);
        Assert.Contains("undeclared", error.Message);

        EpistemicReasonRegistry deferred = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Build();
        Assert.HasCount(1, deferred.Registrations);
    }

    /// <summary>Rung 3 rejects a declared projection name not added through <see cref="EpistemicReasonRegistryBuilder.AddProjection"/>, while an added one passes.</summary>
    [TestMethod]
    public void DeclaredProjectionNameNotAddedThrowsRungThreeWhileAddedPasses()
    {
        EpistemicProjectionCoverage covered = EpistemicProjectionCoverage.Declare(["DecisionTraceEvent"u8.ToArray()]);

        EpistemicReasonRegistryBuilder missing = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), covered));
        EpistemicRegistrationException error = Assert.ThrowsExactly<EpistemicRegistrationException>(() => missing.Build());
        Assert.Contains("Rung 3", error.Message);
        Assert.Contains("declared projection name", error.Message);

        EpistemicReasonRegistry resolved = new EpistemicReasonRegistryBuilder()
            .AddProjection("DecisionTraceEvent"u8.ToArray())
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), covered))
            .Build();
        Assert.HasCount(1, resolved.Registrations);
    }

    /// <summary><see cref="EpistemicProjectionCoverage.Declare"/> rejects a null names list.</summary>
    [TestMethod]
    public void DeclareRejectsNullNames()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EpistemicProjectionCoverage.Declare(null!));
    }

    /// <summary>An empty builder freezes to the process-wide <see cref="EpistemicReasonRegistry.Empty"/> singleton.</summary>
    [TestMethod]
    public void EmptyBuilderFreezesToTheSharedSingleton()
    {
        Assert.AreSame(EpistemicReasonRegistry.Empty, new EpistemicReasonRegistryBuilder().Build());
    }

    /// <summary>The empty registry reports itself empty.</summary>
    [TestMethod]
    public void EmptyRegistryIsEmpty()
    {
        Assert.IsTrue(EpistemicReasonRegistry.Empty.IsEmpty);
        Assert.HasCount(0, EpistemicReasonRegistry.Empty.Registrations);
    }

    /// <summary>A lookup miss returns false without throwing, leaving no registration and no explanation.</summary>
    [TestMethod]
    public void LookupMissReturnsFalseWithoutThrowing()
    {
        EpistemicReasonRegistry registry = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Named"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Build();

        Assert.IsFalse(registry.TryFind(EpistemicReasonCode.Create(19999), out EpistemicReasonRegistration? registration));
        Assert.IsNull(registration);
        Assert.IsFalse(registry.TryGetExplanation(EpistemicReasonCode.Create(19999), out ReadOnlyMemory<byte> explanation));
        Assert.IsTrue(explanation.IsEmpty, "A lookup miss yields the empty explanation memory.");
    }

    /// <summary>A lookup hit resolves the registration and its explanation.</summary>
    [TestMethod]
    public void LookupHitReturnsTheRegistrationAndExplanation()
    {
        EpistemicReasonRegistry registry = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10000, "Hit"u8.ToArray(), "Why hit."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Build();

        Assert.IsTrue(registry.TryFind(EpistemicReasonCode.Create(10000), out EpistemicReasonRegistration? registration));
        Assert.IsNotNull(registration);
        Assert.AreEqual(10000, registration.Code.Code);
        Assert.IsTrue(registration.CanonicalName.Span.SequenceEqual("Hit"u8));

        Assert.IsTrue(registry.TryGetExplanation(EpistemicReasonCode.Create(10000), out ReadOnlyMemory<byte> explanation));
        Assert.IsTrue(explanation.Span.SequenceEqual("Why hit."u8));
    }

    /// <summary><see cref="EpistemicReasonRegistryBuilder.Add"/> returns the same builder for chaining and rejects a null registration.</summary>
    [TestMethod]
    public void AddIsChainableAndNullChecked()
    {
        EpistemicReasonRegistryBuilder builder = new();
        Assert.AreSame(builder, builder.Add(Registration(FamilyOne, 10000, "First"u8.ToArray(), "Why."u8.ToArray(), EpistemicProjectionCoverage.Deferred)));

        Assert.ThrowsExactly<ArgumentNullException>(() => new EpistemicReasonRegistryBuilder().Add(null!));
    }

    /// <summary>A multi-family build accepts every registration and resolves every registered code, preserving registration order.</summary>
    [TestMethod]
    public void MultiFamilyBuildResolvesEveryCode()
    {
        EpistemicReasonRegistry registry = new EpistemicReasonRegistryBuilder()
            .Add(Registration(FamilyOne, 10001, "OneB"u8.ToArray(), "Why one b."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Add(Registration(FamilyOne, 10000, "OneA"u8.ToArray(), "Why one a."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Add(Registration(FamilyTwo, 20000, "TwoA"u8.ToArray(), "Why two a."u8.ToArray(), EpistemicProjectionCoverage.Deferred))
            .Build();

        Assert.HasCount(3, registry.Registrations);

        //Registrations preserve registration order; the sorted arrays serve only the lookups.
        Assert.AreEqual(10001, registry.Registrations[0].Code.Code);
        Assert.AreEqual(10000, registry.Registrations[1].Code.Code);
        Assert.AreEqual(20000, registry.Registrations[2].Code.Code);

        int[] codes = [10000, 10001, 20000];
        foreach(int code in codes)
        {
            Assert.IsTrue(registry.TryFind(EpistemicReasonCode.Create(code), out EpistemicReasonRegistration? found), "Every registered code resolves.");
            Assert.IsNotNull(found);
            Assert.AreEqual(code, found.Code.Code);
        }
    }

    /// <summary>Builds a registration binding the given code under the given family with the given name, explanation, and coverage.</summary>
    /// <param name="family">The class family whose block owns the code.</param>
    /// <param name="code">The reason code int.</param>
    /// <param name="canonicalName">The canonical name as <c>u8</c> bytes.</param>
    /// <param name="explanation">The cold WHY-text as <c>u8</c> bytes.</param>
    /// <param name="coverage">The projection-coverage declaration.</param>
    /// <returns>The registration.</returns>
    private static EpistemicReasonRegistration Registration(EpistemicReasonClassFamily family, int code, ReadOnlyMemory<byte> canonicalName, ReadOnlyMemory<byte> explanation, EpistemicProjectionCoverage coverage)
    {
        return new EpistemicReasonRegistration(family, EpistemicReasonCode.Create(code), canonicalName, explanation, coverage);
    }
}
