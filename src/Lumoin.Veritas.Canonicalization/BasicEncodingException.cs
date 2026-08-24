using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Thrown when a dataset cannot be basic-decoded because its
/// <c>rdf:PropositionForm</c> structure is malformed — a marker blank node is missing or has more
/// than one of its <c>rdf:propositionFormSubject</c> / <c>rdf:propositionFormPredicate</c> /
/// <c>rdf:propositionFormObject</c> assertions, a marker's predicate position is not an IRI, the
/// marker references form a cycle, or the input mixes a triple term with a marker assertion (forbidden
/// by RDF 1.2 Interoperability §3).
/// </summary>
[DebuggerDisplay("BasicEncodingException {Message,nq}")]
public sealed class BasicEncodingException: Exception
{
    /// <summary>
    /// Initialises a new <see cref="BasicEncodingException"/> with a default message.
    /// </summary>
    public BasicEncodingException()
        : base("The basic-encoded input is malformed.")
    {
    }

    /// <summary>
    /// Initialises a new <see cref="BasicEncodingException"/> with the given message.
    /// </summary>
    /// <param name="message">A description of the malformed basic-encoded input.</param>
    public BasicEncodingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="BasicEncodingException"/> with the given message and inner exception.
    /// </summary>
    /// <param name="message">A description of the malformed basic-encoded input.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public BasicEncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
