using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the incremental <see cref="SatSolverSession"/> against fresh per-call
/// solving over the pattern the SAT-backed description-logic reasoner produces:
/// one fixed formula interrogated under a long sequence of related assumption
/// sets. The session reuses its learned clauses, variable order, and saved phases
/// across the whole sequence; the fresh arm solves each set from scratch with the
/// same watched-learning engine. The ratio is the amortization the reuse buys, and
/// the soak doubles as a large differential — it compares the two arms' verdicts
/// per call and reports any mismatch loudly.
/// </summary>
/// <remarks>
/// Each scenario's assumption sequence evolves a small set of literals one toggle
/// at a time, mirroring how a reasoner's per-world labels drift; the families are
/// the same random 3-SAT, implication-chain, and pigeonhole formulas the
/// throughput soak uses. Generation is deterministic per seed. Output is
/// line-oriented and prefixed <c>[sat-incremental-soak]</c>.
/// </remarks>
internal static class SatIncrementalSoak
{
    /// <summary>The default per-arm timeout, in milliseconds.</summary>
    private const int DefaultTimeoutMilliseconds = 30_000;

    /// <summary>Runs the incremental-versus-fresh comparison across the scenarios.</summary>
    /// <param name="args">The full command-line arguments; <c>--timeout-ms &lt;int&gt;</c> tunes the per-arm timeout.</param>
    public static void RunIncrementalSoak(string[] args)
    {
        int timeoutMilliseconds = ParseTimeout(args);
        Console.WriteLine($"[sat-incremental-soak] stopwatch frequency={Stopwatch.Frequency:N0} Hz  timeout-ms={timeoutMilliseconds:N0}");

        foreach(Scenario scenario in Ladder())
        {
            RunScenario(scenario, timeoutMilliseconds);
        }
    }

    /// <summary>The scenario ladder: a formula plus an evolving assumption sequence per family.</summary>
    /// <returns>The scenarios.</returns>
    private static IEnumerable<Scenario> Ladder()
    {
        yield return Random3SatScenario(variables: 50, seed: 1, sequenceLength: 400, assumptionCount: 3);
        yield return Random3SatScenario(variables: 80, seed: 2, sequenceLength: 400, assumptionCount: 4);
        yield return ImplicationChainScenario(variables: 4_000, seed: 3, sequenceLength: 300, assumptionCount: 3);
        yield return PigeonholeScenario(holes: 5, seed: 4, sequenceLength: 300, assumptionCount: 3);
    }

    /// <summary>Runs one scenario through the incremental session and then fresh per-call solving, prints the comparison, and cross-checks the verdicts.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="timeoutMilliseconds">The per-arm timeout.</param>
    private static void RunScenario(Scenario scenario, int timeoutMilliseconds)
    {
        int calls = scenario.AssumptionSequence.Count;

        //Warm the JIT and caches before timing: run a short prefix of each arm
        //untimed, the deletion arm at a zero threshold so the deletion path itself
        //compiles. Without this the first timed arm is charged for one-time
        //compilation and cold caches, which biases the deletion-vs-no-deletion ratio.
        int warmupCalls = Math.Min(calls, 64);
        using(SatSolverSession warmDeletion = new(scenario.Clauses, scenario.VariableCount, 0))
        {
            for(int call = 0; call < warmupCalls; call++)
            {
                warmDeletion.Solve(scenario.AssumptionSequence[call]);
            }
        }

        using(SatSolverSession warmNoDeletion = new(scenario.Clauses, scenario.VariableCount, int.MaxValue))
        {
            for(int call = 0; call < warmupCalls; call++)
            {
                warmNoDeletion.Solve(scenario.AssumptionSequence[call]);
            }
        }

        for(int call = 0; call < warmupCalls; call++)
        {
            SatSolver.SolveUnderAssumptions(scenario.Clauses, scenario.VariableCount, scenario.AssumptionSequence[call], pool: null, SatSearchMode.WatchedLearning, CancellationToken.None);
        }

        bool[] incrementalVerdicts = new bool[calls];
        SatSolveStatistics incrementalStatistics = SatSolveStatistics.Empty;
        bool timedOut = false;

        using CancellationTokenSource incrementalTimeout = new(timeoutMilliseconds);
        long incrementalStart = Stopwatch.GetTimestamp();
        try
        {
            using SatSolverSession session = new(scenario.Clauses, scenario.VariableCount);
            for(int call = 0; call < calls; call++)
            {
                SatVerdict verdict = session.Solve(scenario.AssumptionSequence[call], cancellationToken: incrementalTimeout.Token);
                incrementalVerdicts[call] = verdict.IsSatisfiable;
                SatSolveStatistics callStatistics = verdict.Statistics;
                incrementalStatistics = SatSolveStatistics.Combine(in incrementalStatistics, in callStatistics);
            }
        }
        catch(OperationCanceledException)
        {
            timedOut = true;
        }

        double incrementalMs = Stopwatch.GetElapsedTime(incrementalStart).TotalMilliseconds;

        //The same sequence with learned-clause deletion ENABLED (the default session,
        //timed above, has it off), to isolate the cost of deletion under identical
        //conditions. A low-ish threshold so deletion actually fires on the heavy family.
        double deletionMs = 0;
        if(!timedOut)
        {
            using CancellationTokenSource deletionTimeout = new(timeoutMilliseconds);
            long deletionStart = Stopwatch.GetTimestamp();
            try
            {
                using SatSolverSession session = new(scenario.Clauses, scenario.VariableCount, 2000);
                for(int call = 0; call < calls; call++)
                {
                    session.Solve(scenario.AssumptionSequence[call], cancellationToken: deletionTimeout.Token);
                }
            }
            catch(OperationCanceledException)
            {
                timedOut = true;
            }

            deletionMs = Stopwatch.GetElapsedTime(deletionStart).TotalMilliseconds;
        }

        int mismatches = 0;
        double freshMs = 0;
        if(!timedOut)
        {
            using CancellationTokenSource freshTimeout = new(timeoutMilliseconds);
            long freshStart = Stopwatch.GetTimestamp();
            try
            {
                for(int call = 0; call < calls; call++)
                {
                    SatVerdict verdict = SatSolver.SolveUnderAssumptions(scenario.Clauses, scenario.VariableCount, scenario.AssumptionSequence[call], pool: null, SatSearchMode.WatchedLearning, freshTimeout.Token);
                    if(verdict.IsSatisfiable != incrementalVerdicts[call])
                    {
                        mismatches++;
                    }
                }
            }
            catch(OperationCanceledException)
            {
                timedOut = true;
            }

            freshMs = Stopwatch.GetElapsedTime(freshStart).TotalMilliseconds;
        }

        if(timedOut)
        {
            Console.WriteLine($"[sat-incremental-soak] {scenario.Name,-30} calls={calls}: TIMEOUT (> {timeoutMilliseconds:N0} ms)");

            return;
        }

        int satisfiable = 0;
        foreach(bool verdict in incrementalVerdicts)
        {
            if(verdict)
            {
                satisfiable++;
            }
        }

        double speedup = incrementalMs > 1e-9 ? freshMs / incrementalMs : 0;
        double deletionCost = incrementalMs > 1e-9 ? deletionMs / incrementalMs : 0;
        string agreement = mismatches == 0 ? $"agree ({satisfiable}/{calls} SAT)" : $"MISMATCH ({mismatches} of {calls})";
        Console.WriteLine(
            $"[sat-incremental-soak] {scenario.Name,-30} calls={calls}  incremental={incrementalMs,9:F2} ms  deletion-on={deletionMs,9:F2} ms  fresh={freshMs,9:F2} ms  (incremental-vs-fresh {speedup:F2}x, deletion-on-cost {deletionCost:F2}x)  {agreement}  learned={incrementalStatistics.LearnedClauses:N0} restarts={incrementalStatistics.Restarts:N0}");
    }

    /// <summary>Builds a random 3-SAT scenario at the phase-transition ratio with an evolving assumption sequence.</summary>
    /// <param name="variables">The number of variables.</param>
    /// <param name="seed">The deterministic generation seed.</param>
    /// <param name="sequenceLength">The number of assumption sets.</param>
    /// <param name="assumptionCount">The size of each assumption set.</param>
    /// <returns>The scenario.</returns>
    private static Scenario Random3SatScenario(int variables, int seed, int sequenceLength, int assumptionCount)
    {
        int clauseCount = (int)Math.Round(4.26 * variables);
        Random random = new(seed);
        List<IReadOnlyList<SatLiteral>> clauses = new(clauseCount);
        for(int clause = 0; clause < clauseCount; clause++)
        {
            int first = random.Next(variables);
            int second = random.Next(variables);
            while(second == first)
            {
                second = random.Next(variables);
            }

            int third = random.Next(variables);
            while(third == first || third == second)
            {
                third = random.Next(variables);
            }

            clauses.Add(
            [
                new SatLiteral(first, random.Next(2) == 0),
                new SatLiteral(second, random.Next(2) == 0),
                new SatLiteral(third, random.Next(2) == 0),
            ]);
        }

        return new Scenario($"random3sat v={variables}", clauses, variables, EvolvingAssumptions(random, variables, sequenceLength, assumptionCount));
    }

    /// <summary>Builds a satisfiable implication-chain scenario with an evolving assumption sequence.</summary>
    /// <param name="variables">The number of chain variables.</param>
    /// <param name="seed">The deterministic generation seed.</param>
    /// <param name="sequenceLength">The number of assumption sets.</param>
    /// <param name="assumptionCount">The size of each assumption set.</param>
    /// <returns>The scenario.</returns>
    private static Scenario ImplicationChainScenario(int variables, int seed, int sequenceLength, int assumptionCount)
    {
        List<IReadOnlyList<SatLiteral>> clauses = [[new SatLiteral(0, IsPositive: true)]];
        for(int variable = 0; variable < variables - 1; variable++)
        {
            clauses.Add([new SatLiteral(variable, IsPositive: false), new SatLiteral(variable + 1, IsPositive: true)]);
        }

        Random random = new(seed);

        return new Scenario($"chain v={variables}", clauses, variables, EvolvingAssumptions(random, variables, sequenceLength, assumptionCount));
    }

    /// <summary>Builds an unsatisfiable pigeonhole scenario with an evolving assumption sequence.</summary>
    /// <param name="holes">The number of holes; pigeons are one more.</param>
    /// <param name="seed">The deterministic generation seed.</param>
    /// <param name="sequenceLength">The number of assumption sets.</param>
    /// <param name="assumptionCount">The size of each assumption set.</param>
    /// <returns>The scenario.</returns>
    private static Scenario PigeonholeScenario(int holes, int seed, int sequenceLength, int assumptionCount)
    {
        int pigeons = holes + 1;
        int variableCount = pigeons * holes;
        List<IReadOnlyList<SatLiteral>> clauses = [];
        for(int pigeon = 0; pigeon < pigeons; pigeon++)
        {
            SatLiteral[] occupies = new SatLiteral[holes];
            for(int hole = 0; hole < holes; hole++)
            {
                occupies[hole] = new SatLiteral((pigeon * holes) + hole, IsPositive: true);
            }

            clauses.Add(occupies);
        }

        for(int hole = 0; hole < holes; hole++)
        {
            for(int first = 0; first < pigeons; first++)
            {
                for(int second = first + 1; second < pigeons; second++)
                {
                    clauses.Add(
                    [
                        new SatLiteral((first * holes) + hole, IsPositive: false),
                        new SatLiteral((second * holes) + hole, IsPositive: false),
                    ]);
                }
            }
        }

        Random random = new(seed);

        return new Scenario($"php holes={holes}", clauses, variableCount, EvolvingAssumptions(random, variableCount, sequenceLength, assumptionCount));
    }

    /// <summary>Generates an assumption sequence that starts from a random small set and toggles one literal each step, modelling drifting per-world labels.</summary>
    /// <param name="random">The deterministic generator.</param>
    /// <param name="variables">The variable count.</param>
    /// <param name="sequenceLength">The number of sets.</param>
    /// <param name="assumptionCount">The size of each set.</param>
    /// <returns>The assumption sequence.</returns>
    private static List<IReadOnlyList<SatLiteral>> EvolvingAssumptions(Random random, int variables, int sequenceLength, int assumptionCount)
    {
        int count = Math.Min(assumptionCount, variables);
        SatLiteral[] current = new SatLiteral[count];
        for(int i = 0; i < count; i++)
        {
            current[i] = new SatLiteral(random.Next(variables), random.Next(2) == 0);
        }

        List<IReadOnlyList<SatLiteral>> sequence = new(sequenceLength);
        for(int step = 0; step < sequenceLength; step++)
        {
            if(count > 0)
            {
                int slot = random.Next(count);
                current[slot] = new SatLiteral(random.Next(variables), random.Next(2) == 0);
            }

            sequence.Add((SatLiteral[])current.Clone());
        }

        return sequence;
    }

    /// <summary>Parses the per-arm timeout from the command line, defaulting when not given.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The timeout in milliseconds.</returns>
    private static int ParseTimeout(string[] args)
    {
        for(int index = 0; index < args.Length - 1; index++)
        {
            if(args[index] == "--timeout-ms" && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }

        return DefaultTimeoutMilliseconds;
    }

    /// <summary>One incremental scenario: a name, the fixed formula, its variable count, and the assumption sequence interrogating it.</summary>
    /// <param name="Name">The descriptive name.</param>
    /// <param name="Clauses">The fixed formula.</param>
    /// <param name="VariableCount">The number of variables.</param>
    /// <param name="AssumptionSequence">The assumption sets, applied in order.</param>
    private sealed record Scenario(string Name, IReadOnlyList<IReadOnlyList<SatLiteral>> Clauses, int VariableCount, IReadOnlyList<IReadOnlyList<SatLiteral>> AssumptionSequence);
}
