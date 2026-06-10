using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The per-query join-strategy selector: decides which of the opt-in batched
/// techniques a query should engage. Under the manual policy it passes the
/// <see cref="QueryEnginePolicy"/> flags through verbatim; with
/// <see cref="QueryEnginePolicy.AdaptiveJoinStrategies"/> set it additionally
/// engages the factorising star and chain routes where per-key fan-out
/// estimates — exact group statistics read off the index's compressed-sparse
/// columns — clear the measured time break-evens. Estimation is conservative:
/// a shape or statistic the estimator cannot read keeps the manual decision,
/// and the pipeline's own shape detection revalidates whatever is engaged, so
/// a wrong estimate can cost time but never answers.
/// </summary>
/// <remarks>
/// The break-even thresholds gate on the factorisation's estimated
/// compression — flat rows over stored tuples (<c>∏fan / Σfan</c> for a star
/// over its arms, <c>fanA·S / (fanA + S)</c> with <c>S = fanB·fanC</c> for a
/// chain). Storage break-even sits at ratio 1, but the measured TIME
/// break-even sits higher (grouping overhead is paid per key); the thresholds
/// carry the measured crossovers, characterised by the join-selector soak.
/// Estimates read the base columns; an uncompacted delta skews them slightly,
/// which only moves decisions near the threshold.
/// </remarks>
public static class JoinStrategySelector
{
    /// <summary>The estimated compression — flat rows over stored tuples — a star must clear before the factorising route is engaged; the measured time crossover, set by the join-selector soak.</summary>
    internal const double StarEngageRatio = 4.0;

    /// <summary>The estimated compression a chain must clear before the join-then-nest route is engaged; the measured time crossover, set by the join-selector soak.</summary>
    internal const double ChainEngageRatio = 4.0;

    /// <summary>
    /// Decides the techniques the query should engage on this index: the
    /// policy flags verbatim under the manual policy, plus the adaptive
    /// engagements the index statistics justify when
    /// <see cref="QueryEnginePolicy.AdaptiveJoinStrategies"/> is set.
    /// </summary>
    /// <param name="index">The columnar view the query will run on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="policy">The engine policy.</param>
    /// <returns>The per-query choice.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static JoinStrategyChoice Choose(ColumnarTripleIndex index, BasicGraphPattern query, QueryEnginePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        if(!policy.AdaptiveJoinStrategies)
        {
            return new JoinStrategyChoice(policy.PreferSemijoinReduction, policy.PreferFactorizedStar, policy.PreferFactorizedChain);
        }

        bool star = policy.PreferFactorizedStar || ShouldFactorizeStar(index, query);
        bool chain = !star && (policy.PreferFactorizedChain || ShouldFactorizeChain(index, query));

        return new JoinStrategyChoice(policy.PreferSemijoinReduction, star, chain);
    }

    /// <summary>
    /// Whether the query is a single-key star whose estimated compression
    /// clears <see cref="StarEngageRatio"/>: one variable common to every
    /// pattern, each arm's per-key fan-out estimable, and the product of the
    /// fan-outs exceeding their sum by the threshold.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when the star route should be engaged.</returns>
    private static bool ShouldFactorizeStar(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        if(query.Patterns.Count < 3)
        {
            return false;
        }

        Variable? key = TrySharedKey(query);
        if(key is null)
        {
            return false;
        }

        double sum = 0;
        double product = 1;
        foreach(TriplePattern pattern in query.Patterns)
        {
            if(!TryEstimateKeyFanOut(index, pattern, key.Value, out double fanOut))
            {
                return false;
            }

            sum += fanOut;
            product *= fanOut;
        }

        return product >= sum * StarEngageRatio;
    }

    /// <summary>
    /// Whether the query is a three-pattern chain whose estimated compression
    /// clears <see cref="ChainEngageRatio"/>: a middle pattern sharing one
    /// variable with each arm (the arms sharing nothing), the independent
    /// arm's fan-out against the sub-tree product.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when the chain route should be engaged.</returns>
    private static bool ShouldFactorizeChain(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        if(query.Patterns.Count != 3)
        {
            return false;
        }

        for(int middle = 0; middle < 3; middle++)
        {
            TriplePattern middlePattern = query.Patterns[middle];
            TriplePattern first = query.Patterns[(middle + 1) % 3];
            TriplePattern second = query.Patterns[(middle + 2) % 3];

            Variable? hub = TrySingleSharedVariable(middlePattern, first);
            Variable? nest = TrySingleSharedVariable(middlePattern, second);
            if(hub is null || nest is null || hub.Value == nest.Value || TrySingleSharedVariable(first, second) is not null)
            {
                continue;
            }

            //fanA: the independent arm's matches per hub value; fanB: the
            //middle's nest values per hub; fanC: the leaf arm's matches per
            //nest value. Compression is (fanA·S)/(fanA+S) with S = fanB·fanC.
            if(!TryEstimateKeyFanOut(index, first, hub.Value, out double fanA)
                || !TryEstimateKeyFanOut(index, middlePattern, hub.Value, out double fanB)
                || !TryEstimateKeyFanOut(index, second, nest.Value, out double fanC))
            {
                return false;
            }

            double subTree = fanB * fanC;
            double denominator = fanA + subTree;

            return denominator > 0 && fanA * subTree >= denominator * ChainEngageRatio;
        }

        return false;
    }

    /// <summary>The one variable present in every pattern, or <see langword="null"/> when no or more than one variable is.</summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The shared key, or <see langword="null"/>.</returns>
    private static Variable? TrySharedKey(BasicGraphPattern query)
    {
        Variable? key = null;
        int shared = 0;
        foreach(Variable candidate in VariablesOf(query.Patterns[0]))
        {
            bool inAll = true;
            for(int pattern = 1; pattern < query.Patterns.Count && inAll; pattern++)
            {
                inAll = Binds(query.Patterns[pattern], candidate);
            }

            if(inAll)
            {
                key = candidate;
                shared++;
            }
        }

        return shared == 1 ? key : null;
    }

    /// <summary>The single variable two patterns share, or <see langword="null"/> when they share none or more than one.</summary>
    /// <param name="first">The first pattern.</param>
    /// <param name="second">The second pattern.</param>
    /// <returns>The shared variable, or <see langword="null"/>.</returns>
    private static Variable? TrySingleSharedVariable(TriplePattern first, TriplePattern second)
    {
        Variable? shared = null;
        int count = 0;
        foreach(Variable candidate in VariablesOf(first))
        {
            if(Binds(second, candidate))
            {
                shared = candidate;
                count++;
            }
        }

        return count == 1 ? shared : null;
    }

    /// <summary>The pattern's distinct variables in position order.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The variables.</returns>
    private static List<Variable> VariablesOf(TriplePattern pattern)
    {
        List<Variable> variables = [];
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsVariable && !variables.Contains(pattern.At(position).Variable))
            {
                variables.Add(pattern.At(position).Variable);
            }
        }

        return variables;
    }

    /// <summary>Whether the pattern binds the variable at any position.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="variable">The variable.</param>
    /// <returns><see langword="true"/> when bound.</returns>
    private static bool Binds(TriplePattern pattern, Variable variable)
    {
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsVariable && pattern.At(position).Variable == variable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Estimates the pattern's mean matches per distinct value of
    /// <paramref name="key"/> — exact group statistics read off a materialised
    /// order whose levels put the bound positions first and the key next, so
    /// the answer is two offset-column differences, not a scan. Fails (keeps
    /// the manual decision) when the pattern's shape or the index's order set
    /// does not expose the statistic.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="fanOut">The estimated mean matches per key value; zero when the bound prefix matches nothing.</param>
    /// <returns><see langword="true"/> when the statistic was read.</returns>
    internal static bool TryEstimateKeyFanOut(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out double fanOut)
    {
        fanOut = 0;

        int boundPosition = -1;
        int keyPosition = -1;
        int boundCount = 0;
        int variableCount = 0;
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsBound)
            {
                boundPosition = position;
                boundCount++;
            }
            else
            {
                variableCount++;
                if(pattern.At(position).Variable == key)
                {
                    keyPosition = position;
                }
            }
        }

        if(keyPosition < 0)
        {
            return false;
        }

        //A pattern binding the key as its only variable contributes each key
        //value at most once — a pure semijoin arm, fan-out one.
        if(variableCount == 1)
        {
            fanOut = 1;

            return true;
        }

        if(boundCount != 1 || variableCount != 2)
        {
            return false;
        }

        //The order [bound, key, other] reads the statistic directly: the bound
        //value's level-0 group spans its distinct key values at level 1, and
        //the level-1 offsets accumulate the group's triples.
        for(int permutationIndex = 0; permutationIndex < 6; permutationIndex++)
        {
            if(!index.IsPermutationAvailable(permutationIndex))
            {
                continue;
            }

            ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);
            if(permutation[0] != boundPosition || permutation[1] != keyPosition)
            {
                continue;
            }

            ColumnarOrder order = index.OrderAt(permutationIndex);
            int level0Count = order.ValuesLengthAt(0);
            BlockPackedColumnReader level0Values = new(order.ValuesColumnAt(0));
            uint boundValue = pattern.At(boundPosition).BoundTerm.Encoded;
            int group = level0Values.LowerBound(0, level0Count, boundValue);
            if(group == level0Count || level0Values.ValueAt(group) != boundValue)
            {
                return true;
            }

            BlockPackedColumnReader level0Offsets = new(order.OffsetsColumnAt(0));
            int keysStart = (int)level0Offsets.ValueAt(group);
            int keysEnd = (int)level0Offsets.ValueAt(group + 1);
            if(keysEnd == keysStart)
            {
                return true;
            }

            BlockPackedColumnReader level1Offsets = new(order.OffsetsColumnAt(1));
            long triples = level1Offsets.ValueAt(keysEnd) - (long)level1Offsets.ValueAt(keysStart);
            fanOut = triples / (double)(keysEnd - keysStart);

            return true;
        }

        return false;
    }
}
