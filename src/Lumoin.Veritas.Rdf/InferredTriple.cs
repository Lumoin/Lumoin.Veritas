using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// A triple derived by RDFS inference paired with its provenance: the
/// rule that fired and the two W3C-schema premises the rule matched
/// against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance DAG closure.</b> Each <see cref="Antecedents"/> entry
/// is an <see cref="EncodedTriple"/> the consumer can find either in
/// the source graph (asserted) or earlier in the same
/// <see cref="RdfsInference.InferWithProvenanceAsync"/> output stream
/// (derived) by triple equality. Walking <see cref="Antecedents"/>
/// transitively reaches asserted triples in bounded steps; the
/// substrate's provenance DAG closes through this type.
/// </para>
/// <para>
/// <b>Fixed antecedent cardinality.</b> Every RDFS rule
/// <see cref="RdfsInference"/> implements is two-premise.
/// <see cref="Antecedents"/> therefore always carries exactly two
/// elements. Consumers may rely on this and pattern-match on
/// <see cref="Rule"/> to know the premise ordering without runtime
/// inspection.
/// </para>
/// <para>
/// <b>Composition with proof systems.</b> The shape mirrors
/// <see cref="Lumoin.Veritas.Core.Sourcing.EmittedQuad"/>: a derived
/// artefact paired with the chain that produced it. Sister libraries
/// that construct zero-knowledge or folding-scheme proofs over
/// inference consequents consume <see cref="Antecedents"/> as the
/// witness set without further adaptation.
/// </para>
/// </remarks>
/// <param name="Triple">The derived triple.</param>
/// <param name="Antecedents">
/// The two premises of <paramref name="Rule"/> as they matched at the
/// derivation step. Either premise may itself be a derived triple
/// emitted elsewhere in the same stream.
/// </param>
/// <param name="Rule">
/// The RDFS rule that produced <paramref name="Triple"/>.
/// </param>
[DebuggerDisplay("InferredTriple {Triple,nq} via {Rule,nq} from {Antecedents.Length} premise(s)")]
public readonly record struct InferredTriple(
    EncodedTriple Triple,
    ImmutableArray<EncodedTriple> Antecedents,
    InferenceRule Rule);
