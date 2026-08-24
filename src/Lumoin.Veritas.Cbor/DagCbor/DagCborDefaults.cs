using Lumoin.Veritas.Cbor.Drisl;

namespace Lumoin.Veritas.Cbor.DagCbor;

/// <summary>
/// Factory for <see cref="CborSerializerOptions"/> pre-configured for the
/// DAG-CBOR profile per the IPLD specification. The options align the
/// underlying <see cref="CborConformanceMode"/> with DAG-CBOR's strictness
/// rules: <see cref="CborConformanceMode.RfcCanonical"/> provides RFC 7049
/// §3.9 length-first lexical map-key ordering, no indefinite-length items,
/// 64-bit-only floats, and length-minimised integer encodings. The caps
/// are tighter than the global defaults because DAG-CBOR is used for
/// content-addressed blocks whose individual size is bounded by the
/// surrounding system's block-size convention.
/// </summary>
/// <seealso href="https://ipld.io/specs/codecs/dag-cbor/spec/"/>
public static class DagCborDefaults
{
    /// <summary>
    /// Gets the shared, pre-configured DAG-CBOR options instance. Used
    /// by every <c>DagCborReader</c> / <c>DagCborWriter</c> constructed
    /// with default options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a single shared instance to avoid per-construction
    /// allocation in hot loops (e.g. CAR-file parsing creates one
    /// reader per block; pre-this-change, each construction allocated
    /// a fresh options object plus a fresh <see cref="CidCborConverter"/>
    /// — measurable overhead at firehose-rate parsing).
    /// </para>
    /// <para>
    /// <b>Treat the returned instance as read-only.</b> Callers that
    /// need custom options (additional converters, different size
    /// caps, etc.) must construct their own <see cref="CborSerializerOptions"/>
    /// rather than mutating this one. Mutations would be observed by
    /// every other consumer in the process.
    /// </para>
    /// </remarks>
    public static CborSerializerOptions Options { get; } = Build();

    /// <summary>
    /// Returns the shared DAG-CBOR options instance. Equivalent to the
    /// <see cref="Options"/> property; kept for callers that prefer
    /// the method-call form.
    /// </summary>
    public static CborSerializerOptions CreateOptions() => Options;

    private static CborSerializerOptions Build()
    {
        CborSerializerOptions options = new()
        {
            ConformanceMode = CborConformanceMode.RfcCanonical,
            //Indefinite-length items are forbidden by DAG-CBOR rule 2, but
            //we allow them through to the inner reader so the DAG-CBOR
            //layer can reject them with a DagCborConformanceException
            //naming the §Strictness rule rather than a generic
            //InvalidOperationException from the conformance-mode check.
            AllowIndefiniteLength = true,
            ValidateUtf8 = true,
            //DAG-CBOR rule 4: floats are always 64-bit. Suppress the
            //shortest-form reduction the canonical mode would otherwise apply.
            SuppressFloatReduction = true,
            MaxByteStringLength = 64 * 1024 * 1024,
            MaxTextStringLength = 64 * 1024 * 1024,
            MaxArrayLength = 100_000,
            MaxMapEntryCount = 100_000,
            MaxDepth = 32,
            MaxTagDepth = 4,
            MaxIndefiniteStringChunks = 0
        };
        options.Converters.Add(new CidCborConverter());

        return options;
    }
}
