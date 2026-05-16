using System;
using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// The mutable bookkeeping state shared across iterations of the CBOR-LD
/// compression and decompression algorithms (W3C CBOR-LD 1.0 §5.2). This
/// type is internal because callers should not construct or populate it
/// directly; the encoder and decoder build it from a registry entry plus
/// the input.
/// </summary>
internal sealed class CborLdConversionState
{
    /// <summary>Gets or sets the algorithm direction.</summary>
    public CborLdStrategy Strategy { get; set; }

    /// <summary>Gets or sets the active context at the current walk point.</summary>
    public LinkedDataContext ActiveContext { get; set; } = LinkedDataContext.Empty;

    /// <summary>Gets or sets the initial active context for the document.</summary>
    public LinkedDataContext InitialActiveContext { get; set; } = LinkedDataContext.Empty;

    /// <summary>Gets the mapping from compact term to integer id.</summary>
    public Dictionary<string, int> TermToId { get; init; } = [];

    /// <summary>Gets the mapping from integer id back to compact term.</summary>
    public Dictionary<int, string> IdToTerm { get; init; } = [];

    /// <summary>Gets the mapping from JSON-LD keyword to integer id.</summary>
    public IReadOnlyDictionary<string, int> KeywordsToIds { get; init; } = CborLdKeywords.KeywordsToIds;

    /// <summary>Gets the mapping from integer id back to JSON-LD keyword.</summary>
    public IReadOnlyDictionary<int, string> IdsToKeywords { get; init; } = CborLdKeywords.IdsToKeywords;

    /// <summary>Gets or sets the next available term id (even, starting at 100).</summary>
    public int NextTermId { get; set; } = 100;

    /// <summary>Gets the set of typed-value type names that compress to byte strings (W3C CBOR-LD 1.0 §5.2.1 step 4).</summary>
    public HashSet<string> TypesEncodedAsBytes { get; init; } = [];

    /// <summary>Gets the registry's type tables (name -> table). Used by typed-value codecs.</summary>
    public Dictionary<string, IReadOnlyDictionary<string, int>> TypeTable { get; init; } = [];

    /// <summary>Gets the reverse type tables (name -> reverse table).</summary>
    public Dictionary<string, IReadOnlyDictionary<int, string>> ReverseTypeTable { get; init; } = [];

    /// <summary>Gets the context-map used by the algorithm to remember processed contexts.</summary>
    public Dictionary<string, CborLdRegistryContextEntry> ContextMap { get; init; } = [];

    /// <summary>
    /// Returns the integer id assigned to <paramref name="term"/>,
    /// allocating a fresh id from <see cref="NextTermId"/> on first call
    /// for that term. The id is recorded in both
    /// <see cref="TermToId"/> and <see cref="IdToTerm"/> so encoder and
    /// decoder can resolve in either direction. Idempotent: subsequent
    /// calls for the same term return the existing id.
    /// </summary>
    /// <param name="term">The compact term name to assign.</param>
    /// <returns>The integer id (always even, per W3C CBOR-LD 1.0 §5.2.5.3).</returns>
    /// <remarks>
    /// Called eagerly by <c>CborLdActiveContextScope</c> when an active
    /// context is applied: the scope helper walks the resulting
    /// <see cref="LinkedDataContext"/>'s newly-added term names and calls this
    /// method for each. Eager assignment is what keeps encoder and decoder
    /// id tables aligned — both apply contexts at the same walk points,
    /// so both allocate ids in the same order.
    /// </remarks>
    public int AssignTermId(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        if(TermToId.TryGetValue(term, out int existingId))
        {
            return existingId;
        }

        int assigned = NextTermId;
        NextTermId += 2;
        TermToId[term] = assigned;
        IdToTerm[assigned] = term;
        return assigned;
    }
}
