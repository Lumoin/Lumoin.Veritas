using Lumoin.Veritas.Core.Hypertrie.Editing;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// One decoded durable dataset journal record: the entry plus the term section
/// <see cref="DatasetJournalRecordFormat"/> read back alongside it. The term section is not part of the
/// entry — it is the durability half that lets the log resolve the entry's term identifiers — so replay
/// carries it separately to verify and restore the dictionary before it hands the entry to the read mirror.
/// </summary>
/// <param name="Entry">The decoded dataset journal entry.</param>
/// <param name="TermWatermark">The exclusive lower bound of the record's term identifier range; the count captured by the previous durable record.</param>
/// <param name="NewTerms">The terms the record carried, in identifier order, where <c>NewTerms[i]</c> has identifier <c>TermWatermark + 1 + i</c>.</param>
internal readonly record struct DatasetJournalRecord(DatasetJournalEntry Entry, int TermWatermark, RdfTerm[] NewTerms);
