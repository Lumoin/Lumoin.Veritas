using System.Text;
using Lumoin.Veritas.Core.Sourcing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sourcing;

/// <summary>
/// Verifies <see cref="ByteSourceMap"/> line/column resolution, and that pruning the consumed prefix (the byte-bounded
/// streaming path's reclaim) leaves every still-live offset resolving to the same absolute line and column it would
/// against the whole document — including across the physical compaction that pruning triggers once the dead prefix is
/// the majority.
/// </summary>
[TestClass]
internal sealed class ByteSourceMapTests
{
    /// <summary>Builds a map over the whole text in one append.</summary>
    /// <param name="text">The source text.</param>
    /// <returns>The populated map.</returns>
    private static ByteSourceMap MapOf(string text)
    {
        ByteSourceMap map = new();
        map.Append(Encoding.UTF8.GetBytes(text));

        return map;
    }

    /// <summary>A multi-line document resolves each line's start to that line at column zero.</summary>
    [TestMethod]
    public void ResolvesLineAndColumn()
    {
        ByteSourceMap map = MapOf("ab\ncde\nf\n");

        Assert.AreEqual(new SourceSpan(0, 0, 0, 0, 0, 0), map.Span(0, 0));
        Assert.AreEqual(new SourceSpan(4, 4, 1, 1, 1, 1), map.Span(4, 4), "the 5th byte is the 2nd character of line 1");
        Assert.AreEqual(new SourceSpan(7, 7, 2, 0, 2, 0), map.Span(7, 7), "line 2 begins after the second newline");
    }

    /// <summary>After pruning the prefix, an offset on or after the prune frontier resolves to the same absolute line and column as before — the line number stays absolute, not rebased.</summary>
    [TestMethod]
    public void PruneKeepsLiveOffsetsAbsolute()
    {
        //Two-byte lines ("x\n"), so line k begins at byte 2k and byte 2k+1 is column 1 of line k.
        StringBuilder builder = new();
        for(int i = 0; i < 20_000; i++)
        {
            builder.Append("x\n");
        }

        string text = builder.ToString();
        ByteSourceMap whole = MapOf(text);
        ByteSourceMap pruned = MapOf(text);

        //Prune in advancing steps (as a streaming reader reclaims its prefix), crossing the dead-majority threshold so
        //the physical compaction path runs; then assert every live offset still resolves like the whole-document map.
        foreach(int frontierLine in new[] { 3_000, 9_000, 15_000, 19_000 })
        {
            pruned.PruneBefore(2 * frontierLine);

            foreach(int line in new[] { frontierLine, frontierLine + 1, frontierLine + 250, 19_999 })
            {
                int lineStart = 2 * line;
                Assert.AreEqual(whole.Span(lineStart, lineStart), pruned.Span(lineStart, lineStart), $"line {line} start after pruning before line {frontierLine}");
                Assert.AreEqual(whole.Span(lineStart + 1, lineStart + 1), pruned.Span(lineStart + 1, lineStart + 1), $"line {line} column 1 after pruning before line {frontierLine}");
            }
        }
    }

    /// <summary>An offset below the prune frontier (only the long-lived streaming container/root queries here) resolves to a defined, non-negative location anchored at the oldest live line rather than an underflowed column — the same pre-base-offset hardening the scanner buffer window has.</summary>
    [TestMethod]
    public void LocatingBelowThePruneFrontierIsNonNegative()
    {
        //Pre-guard repro: a map over "a\nb\nc\n" has line starts [0,2,4,6]; PruneBefore(4) sets the live origin to the
        //"c" line (line 2), and Locate(0) would have subtracted that line's start from 0, yielding column -4. The guard
        //anchors a pre-frontier offset to the oldest live line at column zero.
        ByteSourceMap map = MapOf("a\nb\nc\n");
        map.PruneBefore(4);

        Assert.AreEqual(new SourceSpan(0, 0, 2, 0, 2, 0), map.Span(0, 0), "a pre-frontier offset anchors to the oldest live line at a non-negative column");
    }

    /// <summary>Pruning at an offset within the oldest live line (not at a line boundary) keeps that line, so the line still resolves.</summary>
    [TestMethod]
    public void PruneKeepsTheLineContainingTheFrontier()
    {
        ByteSourceMap whole = MapOf("aaaa\nbbbb\ncccc\n");
        ByteSourceMap pruned = MapOf("aaaa\nbbbb\ncccc\n");

        //Byte 7 is column 2 of line 1 ("bbbb"); pruning there must keep line 1 so byte 5 (its start) still resolves.
        pruned.PruneBefore(7);

        Assert.AreEqual(whole.Span(5, 5), pruned.Span(5, 5), "line 1 start");
        Assert.AreEqual(whole.Span(7, 7), pruned.Span(7, 7), "line 1 column 2 (the frontier)");
        Assert.AreEqual(whole.Span(10, 10), pruned.Span(10, 10), "line 2 start");
    }
}
