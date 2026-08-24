using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The engine's hook for materialising entailments into its store at build
/// time: given the freshly built store and the dictionary that encoded it, it
/// returns the store the engine serves — the same store when nothing is
/// derived, or a post-materialisation store carrying the entailed triples. It
/// is a first-class engine-construction seam, peer to the access-control,
/// type-expansion, and execution-trace seams: the query engine defines the
/// hook and never depends on a reasoner, and a composition root supplies the
/// reasoner-backed implementation. Leaving it unwired is the lean-deployment
/// optimisation — the engine then serves simple-entailment results with no
/// reasoning machinery linked.
/// </summary>
/// <param name="store">The freshly built store over the source triples.</param>
/// <param name="dictionary">The dictionary that encoded <paramref name="store"/>; the materialisation may intern further vocabulary terms into it.</param>
/// <param name="cancellationToken">A token that aborts materialisation.</param>
/// <returns>The store the engine serves: unchanged, or carrying the materialised entailments.</returns>
public delegate ValueTask<HypertrieGraphStore> ReasoningMaterializationDelegate(
    HypertrieGraphStore store,
    TermDictionary dictionary,
    CancellationToken cancellationToken);
