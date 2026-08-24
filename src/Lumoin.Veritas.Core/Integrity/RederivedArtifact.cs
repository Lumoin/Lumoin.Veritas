using System;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// An artifact a repair pass regenerated or restored: a re-derivable view rebuilt from the verified
/// system-of-record, or a system-of-record image whose one lost block was restored from local parity — its
/// manifest role and the self-describing, block-checksummed image bytes. The repair pass produces these but
/// does NOT commit them — it is a generation-agnostic producer. The caller stages each image under a new name
/// and lists it in a new manifest generation alongside the unchanged entries, so the single atomic CURRENT
/// publish stays the caller's — a host, a test, or the generation-commit coordinator.
/// </summary>
/// <param name="Role">The manifest role of the regenerated artifact — the re-derivable sidecar or sketch, or a system-of-record restored from local parity.</param>
/// <param name="Image">The self-describing, block-checksummed artifact image for the caller to stage.</param>
public readonly record struct RederivedArtifact(ManifestFileRole Role, ReadOnlyMemory<byte> Image);
