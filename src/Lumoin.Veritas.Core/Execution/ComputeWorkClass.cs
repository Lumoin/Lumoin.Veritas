using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The priority class of a unit of work admitted to the compute lane.
/// An extensible "dynamic enum": a closed C# <see langword="enum"/> would
/// let neither the library nor an embedding application add their own
/// background classes, so this follows the project family's
/// <c>Purpose</c>/<c>BufferKind</c> pattern — a <see langword="readonly"/>
/// <see langword="struct"/> over an <see cref="int"/> with named built-in
/// instances and a <see cref="Create"/> extension point.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Priority"/> is both the identity and the service order:
/// the lane serves a lower priority before a higher one, FIFO within a
/// class, and the value is unique (a collision throws in
/// <see cref="Create"/>), so the order is total and there are no ties.
/// The two ends are load-bearing — <see cref="ControlPlaneTick"/> is the
/// one reserved class (admitted even when the shed-able classes are at
/// capacity, and served first), and <see cref="Scrub"/> is lowest so
/// background integrity work yields to real work under load.
/// </para>
/// <para>
/// An embedder adds a class with <see cref="Create"/> — e.g.
/// <c>ComputeWorkClass.Create(250)</c> to slot between
/// <see cref="Reasoning"/> and <see cref="BulkSort"/>. <see cref="Create"/>
/// mutates shared static state and is not thread-safe; call it at startup
/// before the lane runs, and use priorities at or above 1000 to stay clear
/// of future built-ins. Names for custom classes fall back to a generated
/// tag (<see cref="ComputeWorkClassNames"/>).
/// </para>
/// </remarks>
[DebuggerDisplay("{ComputeWorkClassNames.GetName(this),nq}")]
public readonly struct ComputeWorkClass: IEquatable<ComputeWorkClass>, IComparable<ComputeWorkClass>
{
    /// <summary>The class's priority — lower is served first — which is also its unique identity.</summary>
    public int Priority { get; }

    /// <summary>Constructs a class with the given priority.</summary>
    /// <param name="priority">The priority and identity.</param>
    private ComputeWorkClass(int priority)
    {
        Priority = priority;
    }

    /// <summary>The control-plane tick — the periodic CPU-quota re-read and lane resize. Reserved: admitted even when the shed-able classes are at capacity, and served first.</summary>
    public static ComputeWorkClass ControlPlaneTick { get; } = new(0);

    /// <summary>On-demand columnar view materialisation moved off the serve pool.</summary>
    public static ComputeWorkClass ViewBuild { get; } = new(100);

    /// <summary>Bulk sorting work — the radix-sorted bulk-load path and kindred whole-dataset passes.</summary>
    public static ComputeWorkClass BulkSort { get; } = new(200);

    /// <summary>A reasoning decision — one locality module per turn.</summary>
    public static ComputeWorkClass Reasoning { get; } = new(300);

    /// <summary>Integrity sketch maintenance — background, above only the scrub walk.</summary>
    public static ComputeWorkClass SketchUpdate { get; } = new(400);

    /// <summary>The background integrity walk. Lowest priority by design: it yields to all real work, and its starvation under load is a visible metric.</summary>
    public static ComputeWorkClass Scrub { get; } = new(500);

    /// <summary>The registry of all defined classes, built-in and consumer-created; guarded by <see cref="registryGate"/>.</summary>
    private static readonly List<ComputeWorkClass> registry =
    [
        ControlPlaneTick, ViewBuild, BulkSort, Reasoning, SketchUpdate, Scrub,
    ];

    /// <summary>Guards the registry so registration and enumeration are safe even though registration is normally a startup-time act.</summary>
    private static readonly Lock registryGate = new();

    /// <summary>A snapshot of all defined classes, built-in and consumer-created, in registration order.</summary>
    public static IReadOnlyList<ComputeWorkClass> All
    {
        get
        {
            lock(registryGate)
            {
                return [.. registry];
            }
        }
    }

    /// <summary>
    /// Defines a new priority class for application-specific background
    /// work. Thread-safe, though it is normally a startup-time act so every
    /// consumer observes a stable class set. Use a priority at or above
    /// 1000 to avoid colliding with future built-ins.
    /// </summary>
    /// <param name="priority">The class's priority and identity; lower is served first. Must not already exist.</param>
    /// <returns>The new class.</returns>
    /// <exception cref="ArgumentException">A class with <paramref name="priority"/> already exists.</exception>
    public static ComputeWorkClass Create(int priority)
    {
        lock(registryGate)
        {
            for(int i = 0; i < registry.Count; i++)
            {
                if(registry[i].Priority == priority)
                {
                    throw new ArgumentException($"A compute work class with priority {priority} already exists.", nameof(priority));
                }
            }

            ComputeWorkClass created = new(priority);
            registry.Add(created);

            return created;
        }
    }

    /// <inheritdoc/>
    public bool Equals(ComputeWorkClass other) => Priority == other.Priority;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ComputeWorkClass other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Priority;

    /// <inheritdoc/>
    public int CompareTo(ComputeWorkClass other) => Priority.CompareTo(other.Priority);

    /// <inheritdoc/>
    public override string ToString() => ComputeWorkClassNames.GetName(this);

    /// <summary>Tests two classes for equality by priority.</summary>
    public static bool operator ==(ComputeWorkClass left, ComputeWorkClass right) => left.Equals(right);

    /// <summary>Tests two classes for inequality by priority.</summary>
    public static bool operator !=(ComputeWorkClass left, ComputeWorkClass right) => !left.Equals(right);

    /// <summary>Returns <c>true</c> when <paramref name="left"/> has a higher service priority (lower value) than <paramref name="right"/>.</summary>
    public static bool operator <(ComputeWorkClass left, ComputeWorkClass right) => left.Priority < right.Priority;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> has a higher-or-equal service priority than <paramref name="right"/>.</summary>
    public static bool operator <=(ComputeWorkClass left, ComputeWorkClass right) => left.Priority <= right.Priority;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> has a lower service priority (higher value) than <paramref name="right"/>.</summary>
    public static bool operator >(ComputeWorkClass left, ComputeWorkClass right) => left.Priority > right.Priority;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> has a lower-or-equal service priority than <paramref name="right"/>.</summary>
    public static bool operator >=(ComputeWorkClass left, ComputeWorkClass right) => left.Priority >= right.Priority;
}
