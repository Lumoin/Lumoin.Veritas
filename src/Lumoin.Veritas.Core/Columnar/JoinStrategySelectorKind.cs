using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Which selector took a join-route decision — the decision's telemetry identity. An extensible
/// "dynamic enum": a closed C# <see langword="enum"/> would let neither a later library rule nor a
/// deployment-supplied selector name itself distinguishably on the trace bus, so this follows the
/// project's <see cref="Lumoin.Veritas.Core.Execution.ComputeWorkClass"/> pattern — a
/// <see langword="readonly"/> <see langword="struct"/> over an <see cref="int"/> with named built-in
/// instances and a <see cref="Create"/> extension point.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Code"/> is the identity and nothing else — unlike
/// <see cref="Lumoin.Veritas.Core.Execution.ComputeWorkClass"/>, whose code doubles as a service order,
/// these values carry no order and the type is not comparable. <see cref="None"/> is code zero so the
/// default value means "no selector was consulted", which is what every trace event that did not pass
/// through the seam carries.
/// </para>
/// <para>
/// A deployment names its own selector with <see cref="Create"/>. It mutates shared static state; call it
/// at startup before queries run, and use codes at or above 1000 to stay clear of future built-ins. Names
/// for created kinds fall back to a generated tag (<see cref="JoinStrategySelectorKindNames"/>).
/// </para>
/// </remarks>
[DebuggerDisplay("{JoinStrategySelectorKindNames.GetName(this),nq}")]
public readonly struct JoinStrategySelectorKind: IEquatable<JoinStrategySelectorKind>
{
    /// <summary>The kind's identity.</summary>
    public int Code { get; }

    /// <summary>Constructs a kind with the given identity.</summary>
    /// <param name="code">The identity.</param>
    private JoinStrategySelectorKind(int code)
    {
        Code = code;
    }

    /// <summary>No selector was consulted: the route was not a selector decision. The default value.</summary>
    public static JoinStrategySelectorKind None { get; } = new(0);

    /// <summary>An explicit policy force decided the route; no selector was consulted.</summary>
    public static JoinStrategySelectorKind Forced { get; } = new(100);

    /// <summary>The library's structural rule: the route follows the shape's cyclicity and connectivity.</summary>
    public static JoinStrategySelectorKind Structural { get; } = new(200);

    /// <summary>The library's flags-verbatim rule: the route follows the policy flags and nothing else.</summary>
    public static JoinStrategySelectorKind Manual { get; } = new(300);

    /// <summary>The library's calibrated rule: the route follows the shape, and the remaining axes follow the measured statistics of the view the query would run on.</summary>
    public static JoinStrategySelectorKind Calibrated { get; } = new(400);

    /// <summary>A per-query hint decided the route; no selector was consulted for it.</summary>
    public static JoinStrategySelectorKind Hinted { get; } = new(500);

    /// <summary>The registry of all defined kinds, built-in and consumer-created; guarded by <see cref="registryGate"/>.</summary>
    private static readonly List<JoinStrategySelectorKind> registry =
    [
        None, Forced, Structural, Manual, Calibrated, Hinted
    ];

    /// <summary>Guards the registry so registration and enumeration are safe even though registration is normally a startup-time act.</summary>
    private static readonly Lock registryGate = new();

    /// <summary>A snapshot of all defined kinds, built-in and consumer-created, in registration order.</summary>
    public static IReadOnlyList<JoinStrategySelectorKind> All
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
    /// Defines a new selector identity so a deployment-supplied selector names itself distinguishably on
    /// the trace bus. Thread-safe, though it is normally a startup-time act so every consumer observes a
    /// stable kind set. Use a code at or above 1000 to avoid colliding with future built-ins.
    /// </summary>
    /// <param name="code">The kind's identity. Must not already exist.</param>
    /// <returns>The new kind.</returns>
    /// <exception cref="ArgumentException">A kind with <paramref name="code"/> already exists.</exception>
    public static JoinStrategySelectorKind Create(int code)
    {
        lock(registryGate)
        {
            for(int i = 0; i < registry.Count; i++)
            {
                if(registry[i].Code == code)
                {
                    throw new ArgumentException($"A join strategy selector kind with code {code} already exists.", nameof(code));
                }
            }

            JoinStrategySelectorKind created = new(code);
            registry.Add(created);

            return created;
        }
    }

    /// <inheritdoc/>
    public bool Equals(JoinStrategySelectorKind other) => Code == other.Code;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JoinStrategySelectorKind other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Code;

    /// <inheritdoc/>
    public override string ToString() => JoinStrategySelectorKindNames.GetName(this);

    /// <summary>Tests two kinds for equality by code.</summary>
    public static bool operator ==(JoinStrategySelectorKind left, JoinStrategySelectorKind right) => left.Equals(right);

    /// <summary>Tests two kinds for inequality by code.</summary>
    public static bool operator !=(JoinStrategySelectorKind left, JoinStrategySelectorKind right) => !left.Equals(right);
}
