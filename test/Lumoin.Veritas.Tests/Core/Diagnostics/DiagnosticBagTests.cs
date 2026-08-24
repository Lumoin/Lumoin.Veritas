using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core.Diagnostics;

/// <summary>
/// Tests for <see cref="DiagnosticBag"/>: emptiness, the error flag tracking
/// <see cref="DiagnosticSeverity.Error"/>, and append-only no-dedup behaviour.
/// </summary>
[TestClass]
internal sealed class DiagnosticBagTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Builds a diagnostic with the given code and severity over an empty span.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="severity">The severity.</param>
    /// <returns>The diagnostic.</returns>
    private static Diagnostic Make(Utf8String code, DiagnosticSeverity severity)
    {
        return new Diagnostic(code, severity, SourceSpan.None, new Utf8String("message"u8.ToArray()));
    }

    /// <summary>A fresh bag is empty and error-free.</summary>
    [TestMethod]
    public void EmptyBagHasNoErrors()
    {
        DiagnosticBag bag = new();

        Assert.IsEmpty(bag.Diagnostics);
        Assert.IsFalse(bag.HasErrors);
    }

    /// <summary>A warning does not set the error flag; an error does.</summary>
    [TestMethod]
    public void HasErrorsReflectsErrorSeverity()
    {
        DiagnosticBag bag = new();
        bag.Add(Make(WellKnownDiagnostics.Sparql.UnboundPrefix, DiagnosticSeverity.Warning));
        Assert.IsFalse(bag.HasErrors);

        bag.Add(Make(WellKnownDiagnostics.Sparql.UnexpectedToken, DiagnosticSeverity.Error));
        Assert.IsTrue(bag.HasErrors);
        Assert.HasCount(2, bag.Diagnostics);
    }

    /// <summary>The bag preserves emission order and does not deduplicate identical diagnostics.</summary>
    [TestMethod]
    public void BagIsAppendOnlyAndDoesNotDeduplicate()
    {
        DiagnosticBag bag = new();
        bag.Add(Make(WellKnownDiagnostics.Sparql.UnexpectedToken, DiagnosticSeverity.Error));
        bag.Add(Make(WellKnownDiagnostics.Sparql.UnexpectedToken, DiagnosticSeverity.Error));

        Assert.HasCount(2, bag.Diagnostics);
    }
}
