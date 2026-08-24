namespace Lumoin.Veritas.Core.Persistence;

/// <summary>The outcome of a <see cref="DurableSystemOfRecordStore.TryLoad"/>.</summary>
public enum DurableSystemOfRecordLoadOutcome
{
    /// <summary>No committed generation exists in the store.</summary>
    NotFound,

    /// <summary>The recovered manifest names no dictionary artifact.</summary>
    NoDictionaryEntry,

    /// <summary>The recovered manifest names no system-of-record data segment.</summary>
    NoDataSegmentEntry,

    /// <summary>An artifact is missing or failed its at-rest verification.</summary>
    Rejected,

    /// <summary>The dictionary and the system-of-record triples were recovered and verified.</summary>
    Loaded,
}
