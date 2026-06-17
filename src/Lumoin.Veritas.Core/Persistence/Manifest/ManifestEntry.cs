using System;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// One file a manifest generation makes live: its role, its name within the store, the byte range it
/// occupies (the whole file when the range starts at 0 and spans its length), and the file's checksum
/// under the manifest's algorithm so the generation binds the integrity of every file it names.
/// </summary>
/// <param name="Role">The role the file plays in the generation.</param>
/// <param name="FileName">The file's name within the <see cref="Lumoin.Veritas.Core.Persistence.PersistenceStore"/>.</param>
/// <param name="ByteOffset">The byte offset the file's content begins at (0 for a whole-file artifact).</param>
/// <param name="ByteLength">The byte length of the file's content.</param>
/// <param name="Checksum">The file's checksum under the manifest's algorithm; empty when the manifest carries no checksums.</param>
public readonly record struct ManifestEntry(ManifestFileRole Role, string FileName, long ByteOffset, long ByteLength, ReadOnlyMemory<byte> Checksum);
