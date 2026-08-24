namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// One entry of the house-authored GeoSPARQL 1.1 requirement census
/// (<c>Material/Geo/manifests/geosparql-11-requirement-census.ttl</c>): a normative requirement id with
/// its conformance class, census bucket, and the arm's disposition. Values are carried verbatim as the
/// manifest states them; the coverage ledger in <see cref="GeoConformanceCoverageTests"/> enforces the
/// closed vocabularies and the per-entry coherence, so a manifest defect fails a named row rather than
/// silently shifting the roster.
/// </summary>
/// <param name="RequirementId">The requirement id relative to the specification root (for example <c>/req/core/feature-class</c>).</param>
/// <param name="ConformanceClass">The owning conformance class segment (for example <c>geometry-extension</c>).</param>
/// <param name="Bucket">The census bucket (<c>vocabulary</c>, <c>datatype</c>, <c>serialization</c>, <c>function</c>, <c>entailment</c>, <c>query-rewrite</c>, or <c>other</c>).</param>
/// <param name="Disposition">The arm's disposition (<c>decided</c>, <c>pinned-backlog</c>, or <c>silenced-with-reason</c>).</param>
/// <param name="Reason">What a non-decided disposition awaits; empty for a decided entry.</param>
/// <param name="Evidence">The rows deciding a decided entry; empty otherwise.</param>
internal sealed record GeoRequirementCase(
    string RequirementId,
    string ConformanceClass,
    string Bucket,
    string Disposition,
    string Reason,
    string Evidence)
{
    /// <summary>The <c>decided</c> disposition: live conformance rows exercise the requirement.</summary>
    public const string Decided = "decided";

    /// <summary>The <c>pinned-backlog</c> disposition: not yet decided, with the reason naming what the decision awaits.</summary>
    public const string PinnedBacklog = "pinned-backlog";

    /// <summary>The <c>silenced-with-reason</c> disposition: deliberately outside the claimed scope.</summary>
    public const string SilencedWithReason = "silenced-with-reason";

    /// <inheritdoc/>
    public override string ToString()
    {
        return RequirementId;
    }
}
