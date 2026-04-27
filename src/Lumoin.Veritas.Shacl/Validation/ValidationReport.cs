using Lumoin.Veritas.Shacl.Validation.Evaluators;
using System.Collections.Immutable;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// The aggregate output of a SHACL validation run — a bag of
/// <see cref="ValidationResult"/> entries plus a conformance flag.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.8, the report corresponds to an RDF description
/// with type <c>sh:ValidationReport</c>. The <see cref="Conforms"/>
/// property corresponds to <c>sh:conforms</c>; <see cref="Results"/>
/// corresponds to the set of <c>sh:result</c> links.
/// </para>
/// <para>
/// Conformance is defined as the absence of any
/// <see cref="Shacl.Severity.Violation"/> result. Warnings and
/// informational results — including the output of
/// <see cref="NotImplementedEvaluator"/> — leave conformance intact.
/// </para>
/// <para>
/// The report does <em>not</em> carry a total count of shapes evaluated
/// or timing information; those are orthogonal diagnostic concerns and
/// can be layered on via a wrapping type if needed.
/// </para>
/// </remarks>
public sealed record ValidationReport
{
    /// <summary>
    /// <c>true</c> when no validation result in <see cref="Results"/>
    /// has <see cref="Severity.Violation"/> severity.
    /// </summary>
    public required bool Conforms { get; init; }

    /// <summary>
    /// All results produced during the validation run, in the order
    /// they were emitted by the orchestrator.
    /// </summary>
    public required ImmutableArray<ValidationResult> Results { get; init; }

    /// <summary>
    /// An empty, conforming report. Useful as a baseline in tests and
    /// as a default for no-op validation flows.
    /// </summary>
    public static ValidationReport Empty { get; } = new()
    {
        Conforms = true,
        Results = [],
    };
}
