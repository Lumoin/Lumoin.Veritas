namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// The Sourcing namespace defines the substrate types that bind parsed
/// content to its origin and that admit the cross-cutting capabilities the
/// Veritas family of libraries layer on top: distributed coordination,
/// access control, selective disclosure, zero-knowledge proofs, and
/// collaborative editing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The five types and how they compose.</b>
/// </para>
/// <para>
/// <see cref="DocumentId"/> is a 64-bit identifier for a parsed document.
/// Conventionally content-addressed: the value is the application's chosen
/// <c>VeritasHash</c> applied to the document's canonical bytes. Two
/// parties that apply the same <c>VeritasHash</c> to the same bytes
/// produce the same identifier; the identifier serves as a commitment
/// input for cryptographic protocols that need to refer to a document
/// without inspecting it. The hash algorithm is the application's choice
/// at the composition root, not pinned by this type.
/// </para>
/// <para>
/// <see cref="DocumentIdentity"/> wraps a <see cref="DocumentId"/> with
/// origin metadata (<c>OriginUri</c>, <c>MediaType</c>) that records how
/// the bytes were located and how they were interpreted. Identity and
/// metadata are kept distinct: identity participates in protocols,
/// metadata is local context.
/// </para>
/// <para>
/// <see cref="SourceSpan"/> describes a half-open byte and line/column
/// range within a document. Byte offsets are for storage and protocols;
/// line and column are for editor and human consumption. The pipeline is
/// UTF-8 throughout, so byte offsets compose without conversion.
/// </para>
/// <para>
/// <see cref="DocumentNodeRef"/> is the opaque, value-equatable handle by
/// which consumers refer to nodes within a document, where "node" is
/// whatever the document origin chooses — an AST node for a text parser,
/// a row for columnar storage, an ordinal for an N-Quads dump. Its
/// internal structure pairs a <see cref="DocumentId"/> with a
/// producer-assigned integer; consumers do not depend on this layout,
/// allowing the producer to evolve toward content-addressed node
/// identity — useful for collaborative editing and for any other workload
/// that benefits from stable cross-replica references — without breaking
/// consumer code.
/// </para>
/// <para>
/// <see cref="EmittedQuad"/> pairs a <see cref="Quad"/> with an optional
/// <see cref="DocumentNodeRef"/> identifying the document node that produced it.
/// This is the structural unit of provenance throughout the pipeline,
/// the witness shape that proof systems consume, and the input shape
/// that paramorphic graph operations preserve.
/// </para>
/// <para>
/// <b>Composition with the existing Veritas substrate.</b>
/// </para>
/// <para>
/// The <c>Quad.Graph</c> field expresses the unit of ownership and scope:
/// a named graph identifies which party authored a set of triples, and
/// access-control policies operate at the named-graph granularity. The
/// access-control delegate in <c>Lumoin.Veritas.Core.Hypertrie.AccessControl</c>
/// receives a candidate triple and consults a policy that decides
/// visibility — Allow, Deny, or NotFound — based on caller identity,
/// purpose, and capability tokens. The <c>NotFound</c> case prevents
/// audit-channel inference of denied triples, the privacy guarantee on
/// which selective disclosure depends.
/// </para>
/// <para>
/// The canonicalisation pipeline (RDFC-1.0, in
/// <c>Lumoin.Veritas.Canonicalization</c>) produces a deterministic
/// byte-level encoding of an RDF graph. Applying the application's
/// <c>VeritasHash</c> to that encoding yields a <see cref="DocumentId"/>
/// that any party can recompute and verify. Canonicalisation, hashing,
/// and access control together form the substrate for selective-disclosure
/// credentials and zero-knowledge proofs.
/// </para>
/// <para>
/// <b>Composition with sister libraries.</b>
/// </para>
/// <para>
/// Distributed-coordination libraries use <see cref="DocumentId"/> as
/// the sync identity for replicated documents and consume
/// <see cref="EmittedQuad"/> chains as deltas to merge.
/// </para>
/// <para>
/// Authorization libraries plug into the access-control delegate's
/// extensible context, carrying decentralised identifiers, verifiable
/// credentials, capability tokens, and agentic-protocol state into
/// per-triple decisions.
/// </para>
/// <para>
/// Proof-system libraries consume <see cref="DocumentId"/> as a
/// commitment input, RDFC-1.0-canonical bytes as commitment content,
/// and <see cref="EmittedQuad"/> witness chains as the input set for
/// folding schemes and selective-disclosure proofs over RDF operations.
/// </para>
/// <para>
/// Each sister library participates through delegate seams the substrate
/// already exposes; Veritas itself does not implement distribution,
/// authorization, or proof construction.
/// </para>
/// </remarks>
internal static class NamespaceDocument
{
}