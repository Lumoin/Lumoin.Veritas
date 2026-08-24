using System;

namespace Lumoin.Veritas.Database;

/// <summary>
/// Thrown by an immutable open whose wired reasoning derives an inconsistency when
/// <see cref="ReasoningConfiguration.RefuseInconsistent"/> is set: instead of serving the partial closure and
/// surfacing the outcome on <see cref="VeritasEngine.ReasoningProvenance"/>, the open fails loudly.
/// <see cref="Provenance"/> carries the same reasoning outcome the served database would otherwise have
/// exposed, so a caller that refuses still learns what reasoning decided and which rule, when one fired.
/// </summary>
public sealed class ReasoningInconsistencyException: Exception
{
    /// <summary>Initialises a new instance with a default message; provided for the standard exception-constructor set.</summary>
    public ReasoningInconsistencyException()
        : base("Reasoning derived an inconsistency.")
    {
    }

    /// <summary>Initialises a new instance with a caller-supplied message; provided for the standard exception-constructor set.</summary>
    /// <param name="message">A description of the inconsistency.</param>
    public ReasoningInconsistencyException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with a caller-supplied message and inner exception; provided for the standard exception-constructor set.</summary>
    /// <param name="message">A description of the inconsistency.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ReasoningInconsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance for a refused open, naming the inconsistency rule when one fired and carrying the reasoning outcome.</summary>
    /// <param name="provenance">The reasoning provenance of the refused open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> is <see langword="null"/>.</exception>
    public ReasoningInconsistencyException(ReasoningProvenance provenance)
        : base(DescribeRefusal(provenance))
    {
        Provenance = provenance;
    }

    /// <summary>The reasoning outcome of the refused open — non-<see langword="null"/> whenever the engine raised this, <see langword="null"/> only on the framework-standard constructors.</summary>
    public ReasoningProvenance? Provenance { get; }

    /// <summary>Builds the refusal message, naming the fired rule when the inconsistency was a rule falsity rather than a delegated condemnation.</summary>
    /// <param name="provenance">The refused open's provenance.</param>
    /// <returns>The exception message.</returns>
    private static string DescribeRefusal(ReasoningProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        return provenance.InconsistencyRule is { } rule
            ? $"Reasoning derived an inconsistency (rule {rule}); the open is refused because RefuseInconsistent is set."
            : "Reasoning derived an inconsistency; the open is refused because RefuseInconsistent is set.";
    }
}
