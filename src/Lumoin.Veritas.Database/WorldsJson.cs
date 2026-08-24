using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// Projects the worlds facade's values into the compact JSON documents the worlds wire carries: the
/// worlds listing, the fork and drop outcome documents, the update acknowledgement, and the bounded
/// diff document. This is the editor's wire shape, not a canonical format — every host that answers
/// the worlds face (the CLI server's <c>/worlds</c> routes, the in-browser engine's interop exports)
/// answers these documents, so the transport seam reads one shape whichever tier produced it.
/// </summary>
/// <remarks>
/// The diff document is BOUNDED: it always carries the exact per-graph and whole-document totals, and
/// lists at most a capped number of triples across the document, marking itself truncated when any
/// triple was omitted — truncated-at-N-of-M truth rather than an unbounded dump. State identifiers
/// are 64-bit values and cross as text (sixteen lowercase hex digits), and terms cross as their
/// lexical forms (<c>&lt;iri&gt;</c>, <c>_:label</c>, <c>"value"^^&lt;datatype&gt;</c>), decoded
/// engine-side so no consumer handles encoded triples.
/// </remarks>
public static class WorldsJson
{
    /// <summary>The default bound on the number of triples a diff document lists across all its transitions; totals stay exact beyond it.</summary>
    public const int DefaultDiffTripleCap = 1000;

    /// <summary>The acknowledgement document a successful world-scoped update answers.</summary>
    public const string UpdatedDocument = "{\"outcome\":\"updated\"}";

    /// <summary>
    /// Writes the worlds listing document: one entry per world carrying its name, its
    /// content-addressed state identifier, and its fork parent's name (<see langword="null"/> for the
    /// primary world), in the order the descriptors arrive (<see cref="VeritasEngine.DescribeWorlds"/>
    /// answers the primary world first).
    /// </summary>
    /// <param name="worlds">The world descriptors to list.</param>
    /// <returns>The worlds listing JSON.</returns>
    public static string WriteWorlds(ImmutableArray<WorldDescriptor> worlds)
    {
        StringBuilder json = new();
        json.Append("{\"worlds\":[");

        bool first = true;
        foreach(WorldDescriptor world in worlds)
        {
            AppendSeparator(json, ref first);
            json.Append("{\"name\":").Append(JsonString(world.Name))
                .Append(",\"stateId\":").Append(JsonString(world.StateId.Value.ToString("x16", CultureInfo.InvariantCulture)))
                .Append(",\"parent\":").Append(world.Parent is { } parent ? JsonString(parent) : "null")
                .Append('}');
        }

        json.Append("]}");

        return json.ToString();
    }

    /// <summary>Writes a fork outcome document, the outcome as its wire token.</summary>
    /// <param name="outcome">The fork outcome.</param>
    /// <returns>The outcome JSON.</returns>
    public static string WriteForkOutcome(WorldForkOutcome outcome)
    {
        return outcome switch
        {
            WorldForkOutcome.Forked => "{\"outcome\":\"forked\"}",
            WorldForkOutcome.UnknownSource => "{\"outcome\":\"unknownSource\"}",
            WorldForkOutcome.DuplicateName => "{\"outcome\":\"duplicateName\"}",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown fork outcome.")
        };
    }

    /// <summary>Writes a drop outcome document, the outcome as its wire token.</summary>
    /// <param name="outcome">The drop outcome.</param>
    /// <returns>The outcome JSON.</returns>
    public static string WriteDropOutcome(WorldDropOutcome outcome)
    {
        return outcome switch
        {
            WorldDropOutcome.Dropped => "{\"outcome\":\"dropped\"}",
            WorldDropOutcome.UnknownWorld => "{\"outcome\":\"unknownWorld\"}",
            WorldDropOutcome.PrimaryWorld => "{\"outcome\":\"primaryWorld\"}",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown drop outcome.")
        };
    }

    /// <summary>
    /// Writes a diff document. An unknown world answers the bare outcome document. A computed diff
    /// answers the cap it was written under, the exact transition and triple totals, whether any
    /// triple was omitted, and the per-graph transitions — each with its exact addition and removal
    /// totals beside triple arrays capped by the document's remaining triple budget, so every graph's
    /// magnitude is truthful even when its listing is cut.
    /// </summary>
    /// <param name="diff">The diff to write.</param>
    /// <param name="tripleCap">The bound on the number of triples listed across the whole document; positive.</param>
    /// <returns>The diff JSON.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tripleCap"/> is not positive.</exception>
    public static string WriteDiff(in WorldDiff diff, int tripleCap = DefaultDiffTripleCap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tripleCap);

        if(diff.Outcome != WorldDiffOutcome.Diffed)
        {
            return "{\"outcome\":\"unknownWorld\"}";
        }

        int totalTriples = 0;
        foreach(WorldGraphTransition transition in diff.Transitions)
        {
            totalTriples += transition.Additions.Count + transition.Removals.Count;
        }

        StringBuilder json = new();
        json.Append("{\"outcome\":\"diffed\",\"cap\":").Append(tripleCap)
            .Append(",\"totalTransitions\":").Append(diff.Transitions.Length)
            .Append(",\"totalTriples\":").Append(totalTriples)
            .Append(",\"truncated\":").Append(totalTriples > tripleCap ? "true" : "false")
            .Append(",\"transitions\":[");

        int remainingBudget = tripleCap;
        bool first = true;
        foreach(WorldGraphTransition transition in diff.Transitions)
        {
            AppendSeparator(json, ref first);
            json.Append("{\"graph\":").Append(transition.Graph is { } graph ? JsonString(graph.ToString() ?? string.Empty) : "null")
                .Append(",\"totalAdditions\":").Append(transition.Additions.Count)
                .Append(",\"totalRemovals\":").Append(transition.Removals.Count)
                .Append(",\"additions\":");
            AppendTriples(json, transition.Additions, ref remainingBudget);
            json.Append(",\"removals\":");
            AppendTriples(json, transition.Removals, ref remainingBudget);
            json.Append('}');
        }

        json.Append("]}");

        return json.ToString();
    }

    /// <summary>Appends a triple array, listing at most the remaining budget's worth of triples and spending the budget by what it listed.</summary>
    /// <param name="json">The buffer being built.</param>
    /// <param name="triples">The decoded triples.</param>
    /// <param name="remainingBudget">The document's remaining triple budget; reduced by the number listed.</param>
    private static void AppendTriples(StringBuilder json, IReadOnlyList<DataTriple> triples, ref int remainingBudget)
    {
        json.Append('[');

        int listed = Math.Min(triples.Count, remainingBudget);
        for(int i = 0; i < listed; i++)
        {
            if(i > 0)
            {
                json.Append(',');
            }

            DataTriple triple = triples[i];
            json.Append("{\"s\":").Append(JsonString(triple.Subject.ToString() ?? string.Empty))
                .Append(",\"p\":").Append(JsonString(triple.Predicate.ToString() ?? string.Empty))
                .Append(",\"o\":").Append(JsonString(triple.Object.ToString() ?? string.Empty))
                .Append('}');
        }

        remainingBudget -= listed;
        json.Append(']');
    }

    /// <summary>Appends a comma before every element after the first, then clears the first-element flag.</summary>
    /// <param name="json">The buffer being built.</param>
    /// <param name="first">Whether the next element is the first in its array.</param>
    private static void AppendSeparator(StringBuilder json, ref bool first)
    {
        if(!first)
        {
            json.Append(',');
        }

        first = false;
    }

    /// <summary>A JSON string literal: the value escaped per RFC 8259 and double-quoted.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The quoted, escaped JSON string.</returns>
    private static string JsonString(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach(char character in value)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when character < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }

        builder.Append('"');

        return builder.ToString();
    }
}
