using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// One result of a basic graph pattern query: an immutable
/// mapping from each query variable to its bound encoded value,
/// optionally accompanied by the witness quads that produced
/// the binding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot semantics.</b> A solution is constructed when the
/// driver yields it and never mutated afterwards. The
/// <see cref="Bindings"/> list is a snapshot of the driver's
/// internal binding state at yield time; subsequent driver
/// activity does not affect this solution.
/// </para>
/// <para>
/// <b>Lookup.</b> The number of variables in a query is small
/// (typically ≤ 10), so linear search through
/// <see cref="Bindings"/> in <see cref="TryGetValue"/> is the
/// right shape — it allocates nothing and is faster than
/// dictionary construction at these sizes.
/// </para>
/// <para>
/// <b>Witnesses.</b> <see cref="Witnesses"/> is the list of
/// <see cref="EmittedQuad"/>s that contributed to this solution
/// — the triples that satisfied the query patterns, in the
/// order they were bound. The field is null when the evaluator
/// is run against a quad source that does not propagate
/// provenance, or by an evaluator that does not yet implement
/// witness tracking. Consumers that need provenance test for
/// null and degrade gracefully when it is unavailable.
/// </para>
/// </remarks>
[DebuggerDisplay("Solution Variables={Bindings.Count}")]
public sealed class Solution
{
    /// <summary>
    /// The bindings produced by this solution, in the order the
    /// driver bound them.
    /// </summary>
    public IReadOnlyList<VariableBinding> Bindings { get; }

    /// <summary>
    /// The witness quads that contributed to this solution, when the
    /// evaluator tracks provenance; otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When non-null, the list contains one <see cref="EmittedQuad"/>
    /// per pattern in the query, in the order the driver matched them.
    /// Each <see cref="EmittedQuad"/> carries the matched triple and,
    /// when its source is known, a <see cref="DocumentNodeRef"/> back to
    /// the source document. Consumers compose these chains for
    /// "highlight all triples that contributed to this answer" and
    /// for proof-system witness assembly.
    /// </remarks>
    public IReadOnlyList<EmittedQuad>? Witnesses { get; }

    /// <summary>
    /// Constructs a new solution wrapping
    /// <paramref name="bindings"/> with optional
    /// <paramref name="witnesses"/>. Both lists are held by
    /// reference; callers passing mutable lists are responsible
    /// for not mutating them after.
    /// </summary>
    /// <param name="bindings">The variable-to-value bindings produced by the driver.</param>
    /// <param name="witnesses">
    /// The witness quads that contributed to the bindings, when known;
    /// <see langword="null"/> when the evaluator does not propagate
    /// provenance.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is <c>null</c>.</exception>
    public Solution(
        IReadOnlyList<VariableBinding> bindings,
        IReadOnlyList<EmittedQuad>? witnesses = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        Bindings = bindings;
        Witnesses = witnesses;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="variable"/> is
    /// bound in this solution; <paramref name="value"/> receives
    /// the bound <see cref="TermId"/> on success and
    /// <see cref="TermId.None"/> on failure.
    /// </summary>
    public bool TryGetValue(Variable variable, out TermId value)
    {
        for(int i = 0; i < Bindings.Count; i++)
        {
            VariableBinding binding = Bindings[i];

            if(binding.Variable == variable)
            {
                value = binding.Value;

                return true;
            }
        }

        value = TermId.None;

        return false;
    }

    /// <summary>
    /// Returns the <see cref="TermId"/> bound to <paramref name="variable"/>,
    /// or throws when the variable is not bound in this
    /// solution.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="variable"/> is not bound in this solution.</exception>
    public TermId Get(Variable variable)
    {
        if(TryGetValue(variable, out TermId value))
        {
            return value;
        }

        throw new ArgumentException($"Variable id {variable.Id} is not bound in this solution.", nameof(variable));
    }
}
