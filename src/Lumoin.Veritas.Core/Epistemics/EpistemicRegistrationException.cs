using System;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// The error the epistemic-reason acceptance ladder raises when a registration is rejected — a
/// composition-time configuration invariant, never a query-time condition. Its message names the
/// offending registration (its canonical name or code) and the ladder rung that failed.
/// </summary>
public sealed class EpistemicRegistrationException: Exception
{
    /// <summary>Constructs the error with the rejection reason.</summary>
    /// <param name="message">The rejection reason, naming the registration and the failing rung.</param>
    public EpistemicRegistrationException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs the error with no detail.</summary>
    public EpistemicRegistrationException()
    {
    }

    /// <summary>Constructs the error with the rejection reason and a cause.</summary>
    /// <param name="message">The rejection reason, naming the registration and the failing rung.</param>
    /// <param name="innerException">The causing error.</param>
    public EpistemicRegistrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
