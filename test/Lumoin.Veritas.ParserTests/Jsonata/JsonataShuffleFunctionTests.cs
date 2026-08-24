using System;
using System.Buffers.Binary;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the JSONata <c>$shuffle</c> array built-in: the shuffle-invariant assertions (the count and the
/// sorted order are unchanged), the undefined / singleton edge cases that make no random draw, and — the point
/// of injection — that the Fisher-Yates permutation is the exact, deterministic function of an injected
/// <see cref="RandomnessDelegate"/>, including that the per-position swap index is read from a distinct draw
/// (the loop position is threaded into the request salt).
/// </summary>
[TestClass]
internal sealed class JsonataShuffleFunctionTests
{
    /// <summary>The fixed instant the evaluation clock is pinned to in these tests; <c>$shuffle</c> ignores it, so any fixed value serves.</summary>
    private const long PinnedMillis = 1577836800000L;

    /// <summary><c>$shuffle</c> over an array preserves the element count: the result has the same length as the input.</summary>
    [TestMethod]
    public void ShufflePreservesCount()
    {
        Assert.AreEqual(10d, Evaluate("$count($shuffle([1..10]))", VeritasRandomness.Seeded(1)).AsNumber);
    }

    /// <summary><c>$shuffle</c> is a permutation: sorting the shuffled array recovers the original ordered array.</summary>
    [TestMethod]
    public void ShuffleSortRecoversTheOriginal()
    {
        JsonataValue sorted = Evaluate("$sort($shuffle([1..10]))", VeritasRandomness.Seeded(1));

        Assert.AreEqual(JsonataValueKind.Array, sorted.Kind);
        Assert.HasCount(10, sorted.AsArray);
        for(int i = 0; i < 10; i++)
        {
            Assert.AreEqual((double)(i + 1), sorted.AsArray[i].AsNumber);
        }
    }

    /// <summary><c>$shuffle</c> of nothing (an undefined input) yields undefined, drawing no randomness.</summary>
    [TestMethod]
    public void ShuffleOfNothingYieldsUndefined()
    {
        Assert.IsTrue(Evaluate("$shuffle(nothing)", ThrowingRandomness).IsUndefined);
    }

    /// <summary><c>$shuffle</c> of a single-element array returns it unchanged, drawing no randomness (proved by a source that throws if consulted).</summary>
    [TestMethod]
    public void ShuffleOfSingletonIsUnchangedAndDrawsNothing()
    {
        JsonataValue result = Evaluate("$shuffle([1])", ThrowingRandomness);

        Assert.AreEqual(JsonataValueKind.Array, result.Kind);
        Assert.HasCount(1, result.AsArray);
        Assert.AreEqual(1d, result.AsArray[0].AsNumber);
    }

    /// <summary>
    /// With a source that draws <c>0.0</c> at every position the Fisher-Yates loop swaps each element with
    /// index 0, so <c>[1,2,3,4,5]</c> rotates to the known permutation <c>[2,3,4,5,1]</c>; this proves the
    /// loop genuinely runs (it is not the identity) and that the permutation is the exact function of the
    /// injected source.
    /// </summary>
    [TestMethod]
    public void ShuffleIsTheExactFunctionOfAZeroDrawSource()
    {
        JsonataValue result = Evaluate("$shuffle([1,2,3,4,5])", VeritasRandomness.Zero);

        AssertNumberArray(result, [2d, 3d, 4d, 5d, 1d]);
    }

    /// <summary>The same injected source replays the same permutation across two evaluations: the shuffle is deterministic under injection.</summary>
    [TestMethod]
    public void ShuffleIsDeterministicUnderTheSameSource()
    {
        JsonataValue first = Evaluate("$shuffle([1,2,3,4,5])", VeritasRandomness.Zero);
        JsonataValue second = Evaluate("$shuffle([1,2,3,4,5])", VeritasRandomness.Zero);

        Assert.IsTrue(JsonataValue.DeepEquals(first, second));
    }

    /// <summary>
    /// A source that reads the request salt and returns a per-position value yields a different swap index at
    /// each Fisher-Yates position (here <c>j = i - 1</c>), proving the loop position is threaded into the draw
    /// rather than the same value being reused: <c>[1,2,3,4,5]</c> becomes the known <c>[5,1,2,3,4]</c>.
    /// </summary>
    [TestMethod]
    public void ShuffleReadsADistinctDrawPerPosition()
    {
        JsonataValue result = Evaluate("$shuffle([1,2,3,4,5])", PerPositionRandomness);

        AssertNumberArray(result, [5d, 1d, 2d, 3d, 4d]);
    }

    /// <summary>Asserts a value is a number array deep-equal to an expected sequence of numbers.</summary>
    /// <param name="value">The value under test.</param>
    /// <param name="expected">The expected numbers, in order.</param>
    private static void AssertNumberArray(JsonataValue value, double[] expected)
    {
        Assert.AreEqual(JsonataValueKind.Array, value.Kind);
        Assert.HasCount(expected.Length, value.AsArray);
        for(int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], value.AsArray[i].AsNumber);
        }
    }

    /// <summary>
    /// A randomness source that throws if consulted, so a test can assert a path draws no randomness at all
    /// (the undefined and singleton <c>$shuffle</c> edges).
    /// </summary>
    /// <param name="request">The (unexpected) randomness request.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always, because no draw was expected.</exception>
    private static RandomnessValue ThrowingRandomness(in RandomnessRequest request)
    {
        throw new InvalidOperationException("randomness was drawn where none was expected.");
    }

    /// <summary>
    /// A randomness source that maps the Fisher-Yates loop position (read from the request salt) to a uniform
    /// double yielding the swap index <c>j = position - 1</c>: it returns <c>(position - 0.5) / (position + 1)</c>,
    /// whose floor over the bound <c>position + 1</c> is <c>position - 1</c>. Reading the salt is what proves the
    /// engine threads a distinct draw per position.
    /// </summary>
    /// <param name="request">The randomness request; its salt carries the little-endian loop position.</param>
    /// <returns>The per-position uniform double.</returns>
    private static RandomnessValue PerPositionRandomness(in RandomnessRequest request)
    {
        int position = BinaryPrimitives.ReadInt32LittleEndian(request.CallSiteSalt.Span);
        double unit = (position - 0.5) / (position + 1);

        return new RandomnessValue(RandomnessKind.UniformDouble, unit, default, default);
    }

    /// <summary>Evaluates an expression against an empty input under a fixed clock and the supplied randomness source.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="randomness">The randomness source <c>$shuffle</c> draws from.</param>
    /// <returns>The normalized result value.</returns>
    private static JsonataValue Evaluate(string expression, RandomnessDelegate randomness)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes("{}")));
        FixedTimeProvider clock = new(DateTimeOffset.FromUnixTimeMilliseconds(PinnedMillis));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input, pool: null, timeProvider: clock, randomness: randomness);
    }

    /// <summary>A <see cref="TimeProvider"/> whose <see cref="GetUtcNow"/> always returns one fixed instant, so the evaluation clock is deterministic under test.</summary>
    private sealed class FixedTimeProvider: TimeProvider
    {
        /// <summary>The fixed instant returned by every clock read.</summary>
        private readonly DateTimeOffset instant;

        /// <summary>Initializes the provider with the instant it always reports.</summary>
        /// <param name="instant">The fixed instant.</param>
        public FixedTimeProvider(DateTimeOffset instant)
        {
            this.instant = instant;
        }

        /// <summary>Returns the fixed instant.</summary>
        /// <returns>The fixed instant.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            return instant;
        }
    }
}
