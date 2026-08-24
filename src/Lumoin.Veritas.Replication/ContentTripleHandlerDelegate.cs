namespace Lumoin.Veritas.Replication;

/// <summary>
/// Handles one triple a peer returns for a content-hash fetch. The triple is BORROWED for the duration of the
/// call: its terms' <see cref="Utf8String"/> memory views a pooled per-item buffer the reader releases the moment
/// the handler returns, so a handler that must retain a term copies it first. The handler runs synchronously on
/// the reader's loop, one call per triple, so the lifetime of each triple's memory is bounded by exactly one call.
/// </summary>
/// <param name="triple">The decoded triple, passed by read-only reference; valid only for the duration of this call.</param>
public delegate void ContentTripleHandlerDelegate(in ContentTriple triple);
