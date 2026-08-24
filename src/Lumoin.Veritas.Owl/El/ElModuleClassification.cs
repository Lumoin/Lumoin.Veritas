namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// The result of deciding a module by EL saturation: the consistency verdict,
/// the named-class classification, and the saturation telemetry. Produced by
/// <see cref="ElClassifier.ClassifyModule"/> on the pay-as-you-go fast-path and
/// sound only for modules within the EL⊥ fragment the caller gates on.
/// </summary>
/// <param name="IsConsistent">Whether the module has a model: <see langword="false"/> when owl:Thing is unsatisfiable (no non-empty model) or some named individual is forced empty.</param>
/// <param name="Classification">The named-class subsumption closure — the same projection the TBox classifier produces, the ABox not contaminating it.</param>
/// <param name="CompletionRuleApplications">The completion-rule applications the saturation ran, as decision telemetry.</param>
/// <param name="CompletionEdges">The role edges the saturation derived, as decision telemetry.</param>
internal sealed record ElModuleClassification(
    bool IsConsistent,
    ElClassification Classification,
    long CompletionRuleApplications,
    int CompletionEdges);
