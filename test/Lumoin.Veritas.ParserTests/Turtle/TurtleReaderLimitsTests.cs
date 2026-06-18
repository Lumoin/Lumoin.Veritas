using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleReaderLimitsTests
{
    [TestMethod]
    public void TokenGrowthGuardRejectsTokenBeyondCustomCap()
    {
        const int cap = 256;
        TurtleReaderLimits limits = new()
        {
            OnTokenGrowth = (in TokenGrowthContext context) =>
            {
                if(context.ProposedByteLength > cap)
                {
                    throw new TurtleLimitExceededException(
                        "token exceeds custom cap",
                        SourceSpan.SingleLine(context.StartByte, context.StartByte, context.StartLine, context.StartColumn, context.StartColumn));
                }
            }
        };

        string literal = "\"" + new string('a', 1024) + "\"";
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(literal), pool, limits);

        Assert.Throws<TurtleLimitExceededException>(() =>
        {
            foreach(TurtleToken token in lexer.Tokenize())
            {
                _ = token;
            }
        });
    }

    [TestMethod]
    public void TokenGrowthGuardCarriesTokenKindAndStart()
    {
        TurtleTokenKind observedKind = TurtleTokenKind.EndOfInput;
        int observedStartByte = -1;
        TurtleReaderLimits limits = new()
        {
            OnTokenGrowth = (in TokenGrowthContext context) =>
            {
                observedKind = context.Kind;
                observedStartByte = context.StartByte;

                if(context.ProposedByteLength > 64)
                {
                    throw new TurtleLimitExceededException("over cap");
                }
            }
        };

        //A leading space shifts the IRI start off byte zero so the reported offset is meaningful.
        string source = " <http://example.org/" + new string('x', 256) + ">";
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool, limits);

        Assert.Throws<TurtleLimitExceededException>(() =>
        {
            foreach(TurtleToken token in lexer.Tokenize())
            {
                _ = token;
            }
        });

        Assert.AreEqual(TurtleTokenKind.Iri, observedKind);
        Assert.AreEqual(1, observedStartByte);
    }

    [TestMethod]
    public void DefaultLimitsAllowLargeButBoundedLiteral()
    {
        //Forces several buffer growths but stays well under the 64 MiB default cap.
        string literal = "\"" + new string('a', 200_000) + "\"";
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(literal), pool);

        List<TurtleToken> tokens = [];
        foreach(TurtleToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        Assert.AreEqual(TurtleTokenKind.StringLiteral, tokens[0].Kind);
        Assert.AreEqual(200_000, tokens[0].Value.Length);
    }
}
