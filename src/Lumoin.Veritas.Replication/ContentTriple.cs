using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// An RDF triple as terms — the dictionary-independent form the content-hash reconcile transfers a peer-only triple
/// in. A content-hash item is not invertible to a triple, and the peer holds the triple under its own dictionary's
/// identifiers, so a recovered peer-only item is fetched as its terms and re-encoded into the local dictionary;
/// this is that transfer unit. It is Replication-local — the Core layer stays encoded-identifier-centric — and is
/// the content-hash counterpart of the structural domain's invertible packed key.
/// </summary>
/// <param name="Subject">The subject term.</param>
/// <param name="Predicate">The predicate term.</param>
/// <param name="Object">The object term.</param>
public readonly record struct ContentTriple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);
