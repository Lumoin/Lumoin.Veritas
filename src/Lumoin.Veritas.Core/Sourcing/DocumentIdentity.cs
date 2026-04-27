using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// An envelope around a <see cref="DocumentId"/> that carries origin and
/// media-type metadata about a parsed document. The identity within
/// participates in cross-machine and cryptographic protocols; the metadata
/// is local context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity vs metadata.</b> Two parsers running over the same canonical
/// bytes produce the same <see cref="Id"/>, even when the bytes are loaded
/// from different paths or URLs. <see cref="OriginUri"/> and
/// <see cref="MediaType"/> are not part of identity; they record where this
/// particular instance came from and how it was interpreted. Equality
/// comparisons over <see cref="DocumentIdentity"/> include all three fields,
/// so two instances that disagree on origin or media type are not equal even
/// though they refer to the same logical document. Consumers that want
/// content-only identity compare <see cref="Id"/> directly.
/// </para>
/// <para>
/// <b>Ownership and scope.</b> The unit of ownership in RDF is the named
/// graph (the fourth field of a quad). A <see cref="DocumentIdentity"/>
/// describes the textual document the parser consumed; its triples may
/// belong to one or more named graphs depending on the format. For Turtle
/// the document is a single graph; for TriG, JSON-LD, and N-Quads it can
/// span multiple. Higher-level layers map between the document identity
/// and the named-graph identities its triples populate.
/// </para>
/// <para>
/// <b>Selective disclosure.</b> When a sister library implements
/// selective-disclosure credentials, the <see cref="DocumentId"/> is the
/// commitment input; <see cref="OriginUri"/> may participate in the
/// credential's metadata; <see cref="MediaType"/> records the canonical
/// form the commitment was computed over.
/// </para>
/// </remarks>
/// <param name="Id">The content-addressed identifier of the document.</param>
/// <param name="OriginUri">
/// The location the document was loaded from, when known. Carried as
/// <see cref="Uri"/> so that origin labels are validated at construction
/// rather than re-parsed at every consumer. Local file paths are wrapped
/// with the <c>file:</c> scheme; HTTP and HTTPS URLs flow through unchanged.
/// Not part of identity.
/// </param>
/// <param name="MediaType">
/// The media type that determined how the bytes were parsed. Examples:
/// <c>text/turtle</c>, <c>application/n-quads</c>, <c>application/ld+json</c>.
/// Not part of identity.
/// </param>
[DebuggerDisplay("DocumentIdentity {Id,nq} ({MediaType,nq})")]
public readonly record struct DocumentIdentity(
    DocumentId Id,
    Uri? OriginUri,
    string MediaType);
