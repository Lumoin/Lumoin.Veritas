using System;
using System.Threading;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// A held entry of a remove-aware dataset's causality commit gate
/// (<see cref="MutableSparqlDataset.EnterCausalityCommitScopeAsync"/>); disposing releases the gate. The adopt
/// write-back holds one scope across its whole plan-apply-commit attempt, so the plan it builds against the
/// live ledger stays the ledger's state through the commit's publish — the freshness fence the journal head
/// compare-and-swap cannot provide for causality-only commits, whose child state equals their parent and
/// leaves the head value unchanged.
/// </summary>
public sealed class CausalityCommitScope : IDisposable
{
    /// <summary>The gate this scope holds.</summary>
    private readonly SemaphoreSlim gate;

    /// <summary>Whether the gate has already been released; a second dispose is a no-op.</summary>
    private bool released;

    /// <summary>Wraps a held gate.</summary>
    /// <param name="gate">The gate, already entered by the creator.</param>
    internal CausalityCommitScope(SemaphoreSlim gate)
    {
        this.gate = gate;
    }

    /// <summary>Releases the gate; idempotent.</summary>
    public void Dispose()
    {
        if(!released)
        {
            released = true;
            gate.Release();
        }
    }
}
