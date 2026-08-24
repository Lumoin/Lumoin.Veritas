namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Phases of the three-phase shape load. Tracked on each
/// <see cref="ShapeBuilder"/> so the loader can detect whether a
/// referenced shape's dependencies are populated to the degree that the
/// current phase requires.
/// </summary>
/// <remarks>
/// Phase 1 (discovery) allocates one builder per shape; its state is
/// <see cref="Discovered"/>. Phase 2 walks every shape's outgoing
/// triples to populate targets, metadata, and the leaf (non-shape-
/// referencing) constraints, advancing the state to
/// <see cref="PartiallyPopulated"/>. Phase 3 walks the shapes in
/// dependency order, invoking shape-referencing constraint factories
/// whose references resolve through the dictionary populated in phase 2;
/// the state advances to <see cref="FullyPopulated"/>. A builder that
/// has reached <see cref="FullyPopulated"/> is ready for
/// <see cref="ShapeBuilder.Build"/>.
/// </remarks>
internal enum LoadPhase
{
    /// <summary>Phase 1 — builder allocated, nothing else populated.</summary>
    Discovered,

    /// <summary>Phase 2 complete — targets, metadata, leaf constraints populated.</summary>
    PartiallyPopulated,

    /// <summary>Phase 3 complete — shape-referencing constraints populated, builder ready to <see cref="ShapeBuilder.Build"/>.</summary>
    FullyPopulated,
}
