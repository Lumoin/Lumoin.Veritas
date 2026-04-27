using System;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Thrown by an
/// <see cref="JournalDelegates.AppendJournalEntryAsync"/>
/// implementation when the entry's parent identifier does not
/// match the journal's current head, indicating that another
/// session committed between this session opening and attempting
/// to commit. Callers handle the exception by rebasing against
/// the new head and re-applying their edits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Serialisation.</b> This exception type is not marked
/// <c>[Serializable]</c>. Modern .NET no longer uses
/// <c>BinaryFormatter</c> for exception marshalling, and the
/// <c>NodeIdentifier</c> payload is a value type that has no
/// portable serialised form across runtimes. Cross-process
/// transport of concurrency conflicts is the journal
/// implementation's responsibility — the implementation observes
/// the conflict at its boundary and reconstructs an exception
/// locally before throwing.
/// </para>
/// </remarks>
public sealed class EditSessionConcurrencyException: Exception
{
    /// <summary>The parent identifier the caller's append expected to find as the current head.</summary>
    public NodeIdentifier ExpectedHead { get; }

    /// <summary>The actual current head of the journal at the time of the append.</summary>
    public NodeIdentifier ActualHead { get; }

    /// <summary>Constructs a new exception with default empty heads and a default message; provided for the standard exception-constructor set.</summary>
    public EditSessionConcurrencyException()
        : base("Edit session concurrency conflict.")
    {
        ExpectedHead = NodeIdentifier.Empty;
        ActualHead = NodeIdentifier.Empty;
    }

    /// <summary>Constructs a new exception with default empty heads and a caller-supplied message; provided for the standard exception-constructor set.</summary>
    public EditSessionConcurrencyException(string message)
        : base(message)
    {
        ExpectedHead = NodeIdentifier.Empty;
        ActualHead = NodeIdentifier.Empty;
    }

    /// <summary>Constructs a new exception with default empty heads, a caller-supplied message, and an inner exception; provided for the standard exception-constructor set.</summary>
    public EditSessionConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        ExpectedHead = NodeIdentifier.Empty;
        ActualHead = NodeIdentifier.Empty;
    }

    /// <summary>Constructs a new exception with the given expected and actual heads and a default message.</summary>
    public EditSessionConcurrencyException(NodeIdentifier expectedHead, NodeIdentifier actualHead)
        : base($"Edit session concurrency conflict: expected journal head {expectedHead.Value:X16}, found {actualHead.Value:X16}. Another session committed first; rebase and retry.")
    {
        ExpectedHead = expectedHead;
        ActualHead = actualHead;
    }

    /// <summary>Constructs a new exception with the given expected and actual heads and a caller-supplied message.</summary>
    public EditSessionConcurrencyException(NodeIdentifier expectedHead, NodeIdentifier actualHead, string message)
        : base(message)
    {
        ExpectedHead = expectedHead;
        ActualHead = actualHead;
    }

    /// <summary>Constructs a new exception with the given expected and actual heads, a caller-supplied message, and an inner exception.</summary>
    public EditSessionConcurrencyException(NodeIdentifier expectedHead, NodeIdentifier actualHead, string message, Exception innerException)
        : base(message, innerException)
    {
        ExpectedHead = expectedHead;
        ActualHead = actualHead;
    }
}
