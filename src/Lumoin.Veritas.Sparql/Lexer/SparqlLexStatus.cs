namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// The outcome of one resumable lexing step.
/// </summary>
/// <remarks>
/// The status is the value-based control signal the lexer core returns instead of
/// throwing or blocking: <see cref="Complete"/> yields a token, <see cref="NeedMore"/>
/// asks the driver for more bytes and a re-lex from the token boundary, and
/// <see cref="Error"/> reports a recorded <see cref="SparqlLexDiagnostic"/> without
/// unwinding the stack — so a driver can throw, or recover and continue, as it chooses.
/// </remarks>
internal enum SparqlLexStatus
{
    /// <summary>A token was fully lexed; the reader sits just past it.</summary>
    Complete,

    /// <summary>The buffer ended mid-token; the driver must supply more bytes and re-lex.</summary>
    NeedMore,

    /// <summary>A lexical error was detected; the diagnostic is available from the lexer.</summary>
    Error
}
