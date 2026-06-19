using System;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A derived artifact a repair pass regenerated from the verified system-of-record: its manifest role and the
/// freshly-serialized, self-describing, block-checksummed image bytes. The repair pass produces these but does
/// NOT commit them — it is a generation-agnostic producer. The caller stages each image under a new name and
/// lists it in a new manifest generation alongside the unchanged entries, so the single atomic CURRENT publish
/// stays the caller's (a host or test today, the generation-commit coordinator later).
/// </summary>
/// <param name="Role">The manifest role of the regenerated artifact — the re-derivable sidecar or sketch.</param>
/// <param name="Image">The freshly-serialized, block-checksummed artifact image for the caller to stage.</param>
public readonly record struct RederivedArtifact(ManifestFileRole Role, ReadOnlyMemory<byte> Image);
