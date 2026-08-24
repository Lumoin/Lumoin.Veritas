namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// The three outcomes the test runner reports per W3C test
/// case.
/// </summary>
/// <remarks>
/// <see cref="Skipped"/> is reserved for structural skips
/// — unrecognised test type IRIs, manifests pointing at formats
/// the project does not yet parse. It is never used to bury a
/// parser/emitter defect that the harness has actually
/// uncovered; those are <see cref="Failed"/>.
/// </remarks>
internal enum W3cOutcomeStatus
{
    /// <summary>The test ran and met its assertion.</summary>
    Passed,

    /// <summary>The test ran and did not meet its assertion.</summary>
    Failed,

    /// <summary>The test could not run for a structural reason and is treated as inconclusive.</summary>
    Skipped
}
