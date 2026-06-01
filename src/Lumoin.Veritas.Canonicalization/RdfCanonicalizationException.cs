using System;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Thrown when RDFC-1.0 canonicalization is abandoned because the dataset's blank-node structure exceeds the
/// configured work budget — the "poison" case (RDFC-1.0 §security): a highly-symmetric graph (for example a
/// complete clique of mutually-related blank nodes) whose hash-n-degree resolution explores a factorial number
/// of permutations. Rejecting bounds the work an adversarial input can demand.
/// </summary>
public sealed class RdfCanonicalizationException: Exception
{
    /// <summary>Initialises a new instance of the <see cref="RdfCanonicalizationException"/> class.</summary>
    public RdfCanonicalizationException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="RdfCanonicalizationException"/> class with a message.</summary>
    /// <param name="message">The message describing why canonicalization was abandoned.</param>
    public RdfCanonicalizationException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="RdfCanonicalizationException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public RdfCanonicalizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
