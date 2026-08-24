using System.Collections.Generic;

namespace Lumoin.Veritas.Sparql.Execution.Interception;

/// <summary>One named entry in the interception registry's ordered list.</summary>
/// <param name="Name">The entry's unique name — the label its <c>InterceptionApplied</c> trace events carry.</param>
/// <param name="Interception">The entry delegate, bound as a method group.</param>
internal readonly record struct SparqlInterceptionEntry(string Name, SparqlInterceptionDelegate Interception);

/// <summary>
/// The evaluation interception registry: the ordered, frozen list of fast-path entries the driver consults
/// per expand-phase operator, default-on and disabled as a whole by
/// <see cref="SparqlEnginePolicy.DisableInterceptions"/> (the differential-isolation switch). The order is
/// semantic where entries share a trigger shape: the leaf-cap entry precedes the streaming window entry, so
/// a cappable chain takes the cheaper mechanism and the window only answers shapes the cap cannot reach —
/// the shipped preference, now explicit list order. This registry is the evaluation seam's single extension
/// point; the algebraic rewrite pipeline is its own seam with its own acceptance rules.
/// </summary>
internal sealed class SparqlInterceptionRegistry
{
    /// <summary>Constructs the frozen registry; reachable only through <see cref="Default"/> in this increment.</summary>
    /// <param name="entries">The ordered entries.</param>
    private SparqlInterceptionRegistry(SparqlInterceptionEntry[] entries)
    {
        Entries = entries;
    }

    /// <summary>The registry every evaluation consults: the shipped expand-phase fast paths in their shipped preference order, then the default-OFF value-index probe (ordered after them, so the established fast paths keep their precedence on any shared trigger).</summary>
    public static SparqlInterceptionRegistry Default { get; } = new(
    [
        new SparqlInterceptionEntry(SparqlInterceptions.CountStarName, SparqlInterceptions.CountStar),
        new SparqlInterceptionEntry(SparqlInterceptions.DistinctStarKeysName, SparqlInterceptions.DistinctStarKeys),
        new SparqlInterceptionEntry(SparqlInterceptions.LimitLeafCapName, SparqlInterceptions.LimitLeafCap),
        new SparqlInterceptionEntry(SparqlInterceptions.SliceWindowDrainName, SparqlInterceptions.SliceWindowDrain),
        new SparqlInterceptionEntry(SparqlInterceptions.ValueIndexProbeName, SparqlInterceptions.ValueIndexProbe),
    ]);

    /// <summary>The ordered entries the driver consults.</summary>
    public IReadOnlyList<SparqlInterceptionEntry> Entries { get; }
}
