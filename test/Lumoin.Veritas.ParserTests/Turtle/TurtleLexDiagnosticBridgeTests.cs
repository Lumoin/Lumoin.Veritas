using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies that <see cref="TurtleLexDiagnosticBridge"/> maps every <see cref="TurtleLexErrorCode"/> to
/// a distinct, well-formed <c>LX####</c> diagnostic, so the bridge and the
/// <see cref="WellKnownDiagnostics.Lexer"/> catalogue stay in lock-step (the fine-grained 1:1 contract).
/// </summary>
[TestClass]
internal sealed class TurtleLexDiagnosticBridgeTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Every lexical-error code bridges to an error-severity diagnostic with a non-empty, distinct,
    /// <c>LX</c>-prefixed code and a rendered message; none falls through to the bridge's throwing default.
    /// </summary>
    [TestMethod]
    public void MapsEveryErrorCodeToADistinctLexDiagnostic()
    {
        HashSet<Utf8String> codes = [];

        foreach(TurtleLexErrorCode code in Enum.GetValues<TurtleLexErrorCode>())
        {
            string where = code.ToString();
            LexDiagnostic source = new(code, SourceSpan.SingleLine(0, 1, 0, 0, 1));
            Diagnostic bridged = TurtleLexDiagnosticBridge.ToDiagnostic(source);

            Assert.AreEqual(DiagnosticSeverity.Error, bridged.Severity, where);
            Assert.IsFalse(bridged.Code.IsEmpty, where);
            Assert.IsFalse(bridged.Message.IsEmpty, where);
            Assert.StartsWith("LX", bridged.Code.ToString(), where);
            Assert.IsTrue(codes.Add(bridged.Code), where);
        }
    }
}
