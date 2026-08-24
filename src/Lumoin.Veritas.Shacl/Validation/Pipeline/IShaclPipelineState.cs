using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Shacl.Validation.Pipeline;

/// <summary>
/// Marker interface for <see cref="ShaclPipeline"/> state.
/// </summary>
/// <remarks>
/// <para>
/// Tags <see cref="ShaclPipelineDataState"/> so extension methods and
/// any future generic discovery can target it uniformly. Mirrors the
/// builder-state pattern used elsewhere in the codebase, where a
/// marker interface lets unrelated state shapes share an identity
/// for purposes of fluent composition.
/// </para>
/// <para>
/// Only one state type implements this today, but extensions in
/// other assemblies (test fixtures, custom shape-graph builders) can
/// implement their own phase-specific states that participate in the
/// same fluent surface.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface used to tag pipeline-state types for extension-method dispatch.")]
public interface IShaclPipelineState { }
