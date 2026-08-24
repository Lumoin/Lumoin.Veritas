using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>Whether a Thompson construction produced an automaton or hit the state ceiling.</summary>
internal enum ThompsonStatus
{
    /// <summary>The automaton was built within budget.</summary>
    Compiled,

    /// <summary>The state ceiling was crossed; the caller consumes this as an abstention.</summary>
    BudgetExceeded,
}

/// <summary>The value-based result of a Thompson construction.</summary>
/// <param name="Status">Whether construction completed.</param>
/// <param name="Automaton">The built automaton, on success.</param>
internal readonly record struct ThompsonResult(ThompsonStatus Status, NondeterministicAutomaton? Automaton)
{
    /// <summary>A completed construction.</summary>
    /// <param name="automaton">The built automaton.</param>
    /// <returns>The result.</returns>
    public static ThompsonResult Compiled(NondeterministicAutomaton automaton)
    {
        return new ThompsonResult(ThompsonStatus.Compiled, automaton);
    }

    /// <summary>A construction that crossed the state ceiling.</summary>
    /// <returns>The result.</returns>
    public static ThompsonResult BudgetExceeded()
    {
        return new ThompsonResult(ThompsonStatus.BudgetExceeded, null);
    }
}

/// <summary>
/// Compiles a parsed <see cref="RegexProgram"/> into an epsilon-NFA by Thompson's
/// construction, iteratively. The syntax tree is walked with an explicit task stack and an
/// explicit fragment stack — no method recurses. Bounded repetitions expand by rebuilding
/// the child fragment the required number of times; the shared state ceiling stops any
/// pathological expansion with a value-based budget-exceeded result.
/// </summary>
internal static class ThompsonBuilder
{
    /// <summary>A partially built sub-automaton with a single entry and a single exit state.</summary>
    /// <param name="Start">The entry state.</param>
    /// <param name="Accept">The exit state.</param>
    private readonly record struct Fragment(int Start, int Accept);

    /// <summary>The phase of a syntax-tree build task.</summary>
    private enum TaskPhase
    {
        /// <summary>Visit the node, emitting a leaf fragment or scheduling its children.</summary>
        Enter,

        /// <summary>Combine the two child fragments by concatenation.</summary>
        ConcatCombine,

        /// <summary>Combine the two child fragments by alternation.</summary>
        AltCombine,

        /// <summary>Combine the expanded copy fragments by the node's repetition bounds.</summary>
        RepeatCombine,
    }

    /// <summary>A unit of build work.</summary>
    /// <param name="Node">The syntax-tree node index.</param>
    /// <param name="Phase">The task phase.</param>
    /// <param name="CopyCount">The number of copy fragments a repeat combine consumes.</param>
    private readonly record struct BuildTask(int Node, TaskPhase Phase, int CopyCount);

    /// <summary>Compiles a program into an NFA within the given state ceiling.</summary>
    /// <param name="program">The parsed program.</param>
    /// <param name="maxStates">The most states the construction may mint.</param>
    /// <returns>The construction result.</returns>
    public static ThompsonResult Build(RegexProgram program, int maxStates)
    {
        NondeterministicAutomatonBuilder builder = new(maxStates);
        Stack<Fragment> fragments = new();
        Stack<BuildTask> tasks = new();
        tasks.Push(new BuildTask(program.Root, TaskPhase.Enter, 0));

        while(tasks.Count > 0)
        {
            if(builder.BudgetExceeded)
            {
                return ThompsonResult.BudgetExceeded();
            }

            BuildTask task = tasks.Pop();
            switch(task.Phase)
            {
                case TaskPhase.Enter:
                    ScheduleEnter(program, task.Node, builder, fragments, tasks);
                    break;

                case TaskPhase.ConcatCombine:
                {
                    Fragment right = fragments.Pop();
                    Fragment left = fragments.Pop();
                    fragments.Push(Concat(builder, left, right));
                    break;
                }

                case TaskPhase.AltCombine:
                {
                    Fragment right = fragments.Pop();
                    Fragment left = fragments.Pop();
                    fragments.Push(Alternate(builder, left, right));
                    break;
                }

                case TaskPhase.RepeatCombine:
                {
                    RegexNode node = program.Nodes[task.Node];
                    fragments.Push(CombineRepeat(builder, fragments, node.Min, node.Max, task.CopyCount));
                    break;
                }

                default:
                    break;
            }
        }

        if(builder.BudgetExceeded)
        {
            return ThompsonResult.BudgetExceeded();
        }

        Fragment root = fragments.Pop();
        builder.MarkInitial(root.Start);
        builder.MarkAccepting(root.Accept);

        return ThompsonResult.Compiled(builder.Build());
    }

    /// <summary>Emits a leaf fragment for a leaf node, or schedules the combine and child tasks of an internal node.</summary>
    /// <param name="program">The parsed program.</param>
    /// <param name="nodeIndex">The node index.</param>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="fragments">The fragment stack.</param>
    /// <param name="tasks">The task stack.</param>
    private static void ScheduleEnter(RegexProgram program, int nodeIndex, NondeterministicAutomatonBuilder builder, Stack<Fragment> fragments, Stack<BuildTask> tasks)
    {
        RegexNode node = program.Nodes[nodeIndex];
        switch(node.Kind)
        {
            case RegexNodeKind.Atom:
                fragments.Push(Atom(builder, program.Sets[node.SetIndex]));
                break;

            case RegexNodeKind.Empty:
                fragments.Push(Epsilon(builder));
                break;

            case RegexNodeKind.Concatenation:
                tasks.Push(new BuildTask(nodeIndex, TaskPhase.ConcatCombine, 0));
                tasks.Push(new BuildTask(node.Right, TaskPhase.Enter, 0));
                tasks.Push(new BuildTask(node.Left, TaskPhase.Enter, 0));
                break;

            case RegexNodeKind.Alternation:
                tasks.Push(new BuildTask(nodeIndex, TaskPhase.AltCombine, 0));
                tasks.Push(new BuildTask(node.Right, TaskPhase.Enter, 0));
                tasks.Push(new BuildTask(node.Left, TaskPhase.Enter, 0));
                break;

            case RegexNodeKind.Repeat:
            {
                int copyCount = node.Max < 0 ? node.Min + 1 : node.Max;
                tasks.Push(new BuildTask(nodeIndex, TaskPhase.RepeatCombine, copyCount));
                for(int i = 0; i < copyCount; i++)
                {
                    tasks.Push(new BuildTask(node.Left, TaskPhase.Enter, 0));
                }

                break;
            }

            default:
                break;
        }
    }

    /// <summary>Assembles the copy fragments of a repetition into one fragment per the node bounds.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="fragments">The fragment stack the copies sit on top of.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound, or -1 for unbounded.</param>
    /// <param name="copyCount">The number of copy fragments to consume.</param>
    /// <returns>The assembled fragment.</returns>
    private static Fragment CombineRepeat(NondeterministicAutomatonBuilder builder, Stack<Fragment> fragments, int min, int max, int copyCount)
    {
        if(copyCount == 0)
        {
            return Epsilon(builder);
        }

        Fragment[] copies = new Fragment[copyCount];
        for(int i = 0; i < copyCount; i++)
        {
            copies[i] = fragments.Pop();
        }

        if(max < 0)
        {
            if(min == 0)
            {
                return Star(builder, copies[0]);
            }

            Fragment unbounded = copies[0];
            for(int i = 1; i < min; i++)
            {
                unbounded = Concat(builder, unbounded, copies[i]);
            }

            return Concat(builder, unbounded, Star(builder, copies[min]));
        }

        Fragment accumulated = default;
        bool started = false;
        for(int i = 0; i < copyCount; i++)
        {
            Fragment part = i < min ? copies[i] : Optional(builder, copies[i]);
            accumulated = started ? Concat(builder, accumulated, part) : part;
            started = true;
        }

        return accumulated;
    }

    /// <summary>Builds a leaf fragment matching one code point of a set (no transition when the set is empty).</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="set">The set.</param>
    /// <returns>The fragment.</returns>
    private static Fragment Atom(NondeterministicAutomatonBuilder builder, CodePointSet set)
    {
        int start = builder.AddState();
        int accept = builder.AddState();
        builder.AddTransitions(start, set, accept);

        return new Fragment(start, accept);
    }

    /// <summary>Builds a leaf fragment matching the empty word.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <returns>The fragment.</returns>
    private static Fragment Epsilon(NondeterministicAutomatonBuilder builder)
    {
        int start = builder.AddState();
        int accept = builder.AddState();
        builder.AddEpsilon(start, accept);

        return new Fragment(start, accept);
    }

    /// <summary>Concatenates two fragments by linking the first's exit to the second's entry.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="first">The first fragment.</param>
    /// <param name="second">The second fragment.</param>
    /// <returns>The concatenated fragment.</returns>
    private static Fragment Concat(NondeterministicAutomatonBuilder builder, Fragment first, Fragment second)
    {
        builder.AddEpsilon(first.Accept, second.Start);

        return new Fragment(first.Start, second.Accept);
    }

    /// <summary>Alternates two fragments through a fresh entry and exit.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="first">The first fragment.</param>
    /// <param name="second">The second fragment.</param>
    /// <returns>The alternated fragment.</returns>
    private static Fragment Alternate(NondeterministicAutomatonBuilder builder, Fragment first, Fragment second)
    {
        int start = builder.AddState();
        int accept = builder.AddState();
        builder.AddEpsilon(start, first.Start);
        builder.AddEpsilon(start, second.Start);
        builder.AddEpsilon(first.Accept, accept);
        builder.AddEpsilon(second.Accept, accept);

        return new Fragment(start, accept);
    }

    /// <summary>Wraps a fragment in a Kleene star.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="inner">The inner fragment.</param>
    /// <returns>The starred fragment.</returns>
    private static Fragment Star(NondeterministicAutomatonBuilder builder, Fragment inner)
    {
        int start = builder.AddState();
        int accept = builder.AddState();
        builder.AddEpsilon(start, inner.Start);
        builder.AddEpsilon(start, accept);
        builder.AddEpsilon(inner.Accept, inner.Start);
        builder.AddEpsilon(inner.Accept, accept);

        return new Fragment(start, accept);
    }

    /// <summary>Wraps a fragment in an optional (zero-or-one) repetition.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="inner">The inner fragment.</param>
    /// <returns>The optional fragment.</returns>
    private static Fragment Optional(NondeterministicAutomatonBuilder builder, Fragment inner)
    {
        int start = builder.AddState();
        int accept = builder.AddState();
        builder.AddEpsilon(start, inner.Start);
        builder.AddEpsilon(start, accept);
        builder.AddEpsilon(inner.Accept, accept);

        return new Fragment(start, accept);
    }
}
