using System;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// What one metadata-plane obligation established: the value its outcome ladder answered with, and the record and
/// version the obligation's own write decided.
/// </summary>
/// <typeparam name="TOutcome">The obligation's outcome ladder.</typeparam>
/// <param name="Outcome">The value-based outcome, which is the whole answer; no obligation raises for an expected condition.</param>
/// <param name="Record">The decided record when this replica's own write committed, and <see langword="null"/> otherwise.</param>
/// <param name="Version">The version the write was decided at, and <see cref="RegisterVersion.Unwritten"/> when this replica decided nothing.</param>
/// <remarks>
/// The record is carried only for a committed write. A superseded or undecided attempt answers
/// <c>Undecided</c> on its ladder, and pairing that with a record would hand back a value the obligation did not
/// establish; a caller that wants the record anyway reads it with
/// <see cref="VeritasMetadataPlane.ReadRecordAsync"/>, which is a catch-up rather than a claim of currency.
/// </remarks>
public readonly record struct MetadataPlaneResult<TOutcome>(TOutcome Outcome, VeritasMetadataRecord? Record, RegisterVersion Version)
    where TOutcome: struct, Enum;
