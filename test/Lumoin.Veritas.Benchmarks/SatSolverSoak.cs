using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// A throughput baseline for <see cref="SatSolver"/>, the propositional engine
/// the SAT-backed description-logic reasoner calls per world. The OWL
/// cost-breakdown found this solver dominates non-EL reasoning time (up to 98%);
/// this soak measures it directly — propagation throughput and solve time over
/// standard instance families — so a later watched-literal / activity-heuristic
/// rework has a clean before-and-after number.
/// </summary>
/// <remarks>
/// <para>
/// Families: random 3-SAT at the satisfiability phase transition (clause-to-variable
/// ratio 4.26, the hardest random instances), the pigeonhole principle (provably
/// hard unsatisfiable cores that stress conflict analysis), and a long implication
/// chain (a propagation-heavy satisfiable instance that exposes the per-propagation
/// cost of the scan-based unit propagation directly — every propagation rescans the
/// whole clause set today). Both search modes are measured. Generation is
/// deterministic per seed so the before-and-after compares like for like.
/// </para>
/// <para>
/// Output is line-oriented and prefixed <c>[sat-soak]</c>. A single solve that
/// exceeds the timeout is reported rather than blocking the soak — the deterministic
/// lowest-index branching makes the hard families slow.
/// </para>
/// </remarks>
internal static class SatSolverSoak
{
    /// <summary>The default per-solve timeout, in milliseconds.</summary>
    private const int DefaultTimeoutMilliseconds = 8_000;

    /// <summary>Runs the SAT throughput baseline across the instance families and both search modes.</summary>
    /// <param name="args">The full command-line arguments; <c>--timeout-ms &lt;int&gt;</c> tunes the per-solve timeout, <c>--restart-unit &lt;int&gt;</c> overrides the watched engine's Luby restart base unit (0 disables restarts, for the comparand), <c>--no-minimize</c> turns off watched-engine learned-clause minimization (the comparand), <c>--dynamic-restart</c> restarts the watched engine on the LBD trend, <c>--trail-blocking</c> adds trail-based restart blocking to the dynamic policy (the comparand), and <c>--watched-only</c> skips the scan modes.</param>
    public static void RunSatSoak(string[] args)
    {
        int timeoutMilliseconds = ParseTimeout(args);
        int restartUnit = ParseRestartUnit(args);
        bool minimize = Array.IndexOf(args, "--no-minimize") < 0;
        bool dynamicRestart = Array.IndexOf(args, "--dynamic-restart") >= 0;
        bool trailBlocking = Array.IndexOf(args, "--trail-blocking") >= 0;
        bool watchedOnly = Array.IndexOf(args, "--watched-only") >= 0;
        Console.WriteLine($"[sat-soak] stopwatch frequency={Stopwatch.Frequency:N0} Hz  timeout-ms={timeoutMilliseconds:N0}  restart-unit={restartUnit:N0}  minimize={minimize}  dynamic-restart={dynamicRestart}  trail-blocking={trailBlocking}");

        foreach(Instance instance in Ladder())
        {
            if(!watchedOnly)
            {
                RunInstance(instance, SatSearchMode.PropagationOnly, timeoutMilliseconds, restartUnit, minimize, dynamicRestart, trailBlocking);
                RunInstance(instance, SatSearchMode.ConflictLearning, timeoutMilliseconds, restartUnit, minimize, dynamicRestart, trailBlocking);
            }

            RunInstance(instance, SatSearchMode.WatchedLearning, timeoutMilliseconds, restartUnit, minimize, dynamicRestart, trailBlocking);
        }
    }

    /// <summary>The instance ladder, ordered easy to hard within each family.</summary>
    /// <returns>The instances.</returns>
    private static IEnumerable<Instance> Ladder()
    {
        //Propagation-heavy chain: a satisfiable implication chain forcing one
        //propagation per variable, where the scan-based engine rescans the whole
        //clause set each propagation — the clearest exposure of BCP throughput.
        yield return ImplicationChain(variables: 2_000);
        yield return ImplicationChain(variables: 8_000);
        yield return ImplicationChain(variables: 20_000);

        //Random 3-SAT at the phase transition (ratio 4.26): the hardest random
        //instances, a mix of satisfiable and unsatisfiable.
        yield return Random3Sat(variables: 50, seed: 1);
        yield return Random3Sat(variables: 100, seed: 2);
        yield return Random3Sat(variables: 150, seed: 3);

        //Pigeonhole: provably hard unsatisfiable cores (n+1 pigeons into n holes),
        //exponential for resolution, the stress test for conflict analysis.
        yield return Pigeonhole(holes: 5);
        yield return Pigeonhole(holes: 6);
        yield return Pigeonhole(holes: 7);
    }

    /// <summary>Solves one instance under one mode with a timeout, and prints the throughput line.</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="mode">The search mode.</param>
    /// <param name="timeoutMilliseconds">The per-solve timeout.</param>
    /// <param name="restartUnit">The Luby restart base unit applied only to the watched-learning engine; the scan modes never restart.</param>
    /// <param name="minimize">Whether the watched-learning engine minimizes learned clauses; the scan modes follow the production default.</param>
    /// <param name="dynamicRestart">Whether the watched-learning engine restarts on the LBD trend instead of the Luby schedule.</param>
    /// <param name="trailBlocking">Whether the watched-learning engine's dynamic policy blocks a restart while the assignment grows toward a model.</param>
    private static void RunInstance(Instance instance, SatSearchMode mode, int timeoutMilliseconds, int restartUnit, bool minimize, bool dynamicRestart, bool trailBlocking)
    {
        using CancellationTokenSource timeout = new(timeoutMilliseconds);
        long start = Stopwatch.GetTimestamp();
        SatVerdict verdict;
        try
        {
            verdict = mode == SatSearchMode.WatchedLearning
                ? SatSolver.SolveWatchedLearningForTest(instance.Clauses, instance.VariableCount, assumptions: [], restartUnit, timeout.Token, minimize, dynamicRestart, trailBlocking)
                : SatSolver.Solve(instance.Clauses, instance.VariableCount, pool: null, mode, timeout.Token);
        }
        catch(OperationCanceledException)
        {
            Console.WriteLine($"[sat-soak] {instance.Name,-28} {ModeLabel(mode)}: TIMEOUT (> {timeoutMilliseconds:N0} ms)  vars={instance.VariableCount:N0} clauses={instance.Clauses.Count:N0}");

            return;
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        SatSolveStatistics statistics = verdict.Statistics;
        double seconds = Math.Max(elapsed.TotalSeconds, 1e-9);
        double propagationsPerSecond = statistics.Propagations / seconds;
        Console.WriteLine(
            $"[sat-soak] {instance.Name,-28} {ModeLabel(mode)}: {elapsed.TotalMilliseconds,9:F2} ms  {(verdict.IsSatisfiable ? "SAT  " : "UNSAT")}  vars={instance.VariableCount:N0} clauses={instance.Clauses.Count:N0}  props={statistics.Propagations:N0} ({propagationsPerSecond / 1_000_000.0:F2} M/s)  decisions={statistics.Decisions:N0} conflicts={statistics.Conflicts:N0} learned={statistics.LearnedClauses:N0} restarts={statistics.Restarts:N0}");
    }

    /// <summary>A short label for a search mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The label.</returns>
    private static string ModeLabel(SatSearchMode mode)
    {
        return mode switch
        {
            SatSearchMode.WatchedLearning => "watched  ",
            SatSearchMode.ConflictLearning => "learning ",
            _ => "prop-only",
        };
    }

    /// <summary>Builds a satisfiable implication chain: a unit on the first variable and clauses <c>¬xᵢ ∨ xᵢ₊₁</c> forcing every variable true in sequence.</summary>
    /// <param name="variables">The number of chain variables.</param>
    /// <returns>The instance.</returns>
    private static Instance ImplicationChain(int variables)
    {
        List<IReadOnlyList<SatLiteral>> clauses = [[new SatLiteral(0, IsPositive: true)]];
        for(int variable = 0; variable < variables - 1; variable++)
        {
            clauses.Add([new SatLiteral(variable, IsPositive: false), new SatLiteral(variable + 1, IsPositive: true)]);
        }

        return new Instance($"chain v={variables}", clauses, variables);
    }

    /// <summary>Builds a random 3-SAT instance at the phase-transition ratio (4.26 clauses per variable), each clause three distinct variables with random polarity.</summary>
    /// <param name="variables">The number of variables.</param>
    /// <param name="seed">The deterministic generation seed.</param>
    /// <returns>The instance.</returns>
    private static Instance Random3Sat(int variables, int seed)
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

        return new Instance($"random3sat v={variables} m={clauseCount}", clauses, variables);
    }

    /// <summary>Builds the pigeonhole instance: <paramref name="holes"/>+1 pigeons into <paramref name="holes"/> holes, unsatisfiable, each pigeon in some hole and no two pigeons sharing a hole.</summary>
    /// <param name="holes">The number of holes.</param>
    /// <returns>The instance.</returns>
    private static Instance Pigeonhole(int holes)
    {
        int pigeons = holes + 1;
        int variableCount = pigeons * holes;
        List<IReadOnlyList<SatLiteral>> clauses = [];

        //Every pigeon occupies some hole.
        for(int pigeon = 0; pigeon < pigeons; pigeon++)
        {
            SatLiteral[] occupies = new SatLiteral[holes];
            for(int hole = 0; hole < holes; hole++)
            {
                occupies[hole] = new SatLiteral((pigeon * holes) + hole, IsPositive: true);
            }

            clauses.Add(occupies);
        }

        //No hole holds two pigeons.
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

        return new Instance($"php holes={holes} pigeons={pigeons}", clauses, variableCount);
    }

    /// <summary>Parses the per-solve timeout from the command line, defaulting when not given.</summary>
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

    /// <summary>Parses the watched engine's Luby restart base unit from the command line, defaulting to the engine's production unit; a value of zero disables restarts, the comparand.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The restart base unit.</returns>
    private static int ParseRestartUnit(string[] args)
    {
        for(int index = 0; index < args.Length - 1; index++)
        {
            if(args[index] == "--restart-unit" && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }

        return SatSolver.DefaultRestartUnit;
    }

    /// <summary>One CNF instance: a name, its clauses, and its variable count.</summary>
    /// <param name="Name">The descriptive name carrying the family and parameters.</param>
    /// <param name="Clauses">The CNF clauses.</param>
    /// <param name="VariableCount">The number of variables.</param>
    private sealed record Instance(string Name, IReadOnlyList<IReadOnlyList<SatLiteral>> Clauses, int VariableCount);
}
