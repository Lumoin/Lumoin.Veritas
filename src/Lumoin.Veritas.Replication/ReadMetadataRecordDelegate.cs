using System.Text.Json;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Reads ONE coordinated metadata record back from the application value of a consensus payload — the
/// counterpart of <see cref="WriteMetadataRecordDelegate"/>.
/// </summary>
/// <param name="element">The element the record's object was written into.</param>
/// <returns>The record the payload carries.</returns>
/// <remarks>
/// A payload this seam cannot read is refused by raising, and the codec that drives it turns that into the one
/// deserialization failure a channel consumer catches, so a malformed record is never read as a partial one.
/// <see cref="VeritasMetadataWireCodec.ReadRecord"/> is the deployment's binding of it.
/// </remarks>
public delegate VeritasMetadataRecord ReadMetadataRecordDelegate(JsonElement element);
