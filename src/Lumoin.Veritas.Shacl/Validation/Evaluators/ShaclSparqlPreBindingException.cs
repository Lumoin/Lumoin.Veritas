using System;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Thrown when a SHACL-SPARQL constraint query uses a construct that cannot be combined with pre-binding
/// (SHACL §5.2.1) — a <c>MINUS</c>, <c>VALUES</c>, <c>SERVICE</c>, a sub-<c>SELECT</c> that does not project
/// the pre-bound focus variable, or an assignment (<c>BIND … AS</c> / <c>SELECT (… AS ?v)</c>) to a pre-bound
/// variable. The shapes graph is ill-formed for validation; the SHACL processing fails (the conformance
/// suite marks such tests <c>sht:Failure</c>) rather than producing a validation report.
/// </summary>
public sealed class ShaclSparqlPreBindingException: Exception
{
    /// <summary>Initialises a new instance of the <see cref="ShaclSparqlPreBindingException"/> class.</summary>
    public ShaclSparqlPreBindingException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="ShaclSparqlPreBindingException"/> class with a message.</summary>
    /// <param name="message">The message describing the unsupported construct.</param>
    public ShaclSparqlPreBindingException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="ShaclSparqlPreBindingException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message describing the unsupported construct.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ShaclSparqlPreBindingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
