using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One triple of the data graph a <see cref="SparqlQueryEngine"/> is queried against, over unencoded
/// <see cref="RdfTerm"/>s. The engine encodes these into the hypertrie backend's term dictionary when it builds.
/// </summary>
/// <param name="Subject">The triple's subject.</param>
/// <param name="Predicate">The triple's predicate.</param>
/// <param name="Object">The triple's object.</param>
/// <remarks>
/// A provisional convenience surface for loading a graph from in-memory terms; the durable data-source binding
/// (a built <see cref="Core.Hypertrie.HypertrieGraphStore"/> shared across queries) is the production path.
/// </remarks>
[DebuggerDisplay("{Subject} {Predicate} {Object}")]
public readonly record struct DataTriple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);
