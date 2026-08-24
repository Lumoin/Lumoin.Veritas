using System;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// A borrowed view of one state's range-labelled out-transitions as parallel spans of labels
/// and targets. It is a ref struct because a value tuple cannot hold spans; positional
/// deconstruction binds the two spans in one statement.
/// </summary>
internal readonly ref struct RangeTransitionView
{
    /// <summary>The range labels.</summary>
    public ReadOnlySpan<CodePointRange> Labels { get; }

    /// <summary>The transition targets, parallel to <see cref="Labels"/>.</summary>
    public ReadOnlySpan<int> Targets { get; }

    /// <summary>Wraps the parallel spans.</summary>
    /// <param name="labels">The range labels.</param>
    /// <param name="targets">The transition targets.</param>
    public RangeTransitionView(ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets)
    {
        Labels = labels;
        Targets = targets;
    }

    /// <summary>The number of transitions in the view.</summary>
    public int Count => Labels.Length;

    /// <summary>Splits the view into its two parallel spans.</summary>
    /// <param name="labels">The range labels.</param>
    /// <param name="targets">The transition targets.</param>
    public void Deconstruct(out ReadOnlySpan<CodePointRange> labels, out ReadOnlySpan<int> targets)
    {
        labels = Labels;
        targets = Targets;
    }
}
