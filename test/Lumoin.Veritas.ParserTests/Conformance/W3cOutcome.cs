using System.Diagnostics;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// The result the runner reports for one W3C test case.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is intended for display in the test
/// runner's failure output and in the conformance status
/// report; it should name what was expected and what was seen
/// in one short sentence.
/// </remarks>
[DebuggerDisplay("{Status} {Message,nq}")]
internal sealed record W3cOutcome(W3cOutcomeStatus Status, string Message);
