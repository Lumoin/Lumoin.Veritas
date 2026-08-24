using System.Text.Json;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Writes ONE coordinated metadata record as the application value inside a consensus payload — the body seam
/// every metadata envelope this deployment sends is composed over.
/// </summary>
/// <param name="writer">The writer the record's object is written into; the seam writes one complete JSON value and nothing around it.</param>
/// <param name="record">The record to write.</param>
/// <remarks>
/// The envelope around the value belongs to the consensus library, so this seam names the application half
/// alone. <see cref="VeritasMetadataWireCodec.WriteRecord"/> is the deployment's binding of it, and the
/// counterpart that reads the body back is <see cref="ReadMetadataRecordDelegate"/>.
/// </remarks>
public delegate void WriteMetadataRecordDelegate(Utf8JsonWriter writer, VeritasMetadataRecord record);
