using System.Collections.Immutable;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// One triple paired explicitly with the causal dots a commit assigns to it — the dots minted or adopted for an
/// addition, or the dots dropped for a removal. The pairing is the carrier itself: dots never correlate to
/// triples by position in a parallel structure.
/// </summary>
/// <param name="Triple">The encoded triple the dots belong to.</param>
/// <param name="Dots">The dots assigned; at least one. A locally-minted addition carries exactly one fresh dot; an adopted addition carries every surviving peer dot; a drop names every dot the removal cancels.</param>
[DebuggerDisplay("DottedTripleAssignment S={Triple.Subject.Encoded} P={Triple.Predicate.Encoded} O={Triple.Object.Encoded} Dots={Dots.Length}")]
public readonly record struct DottedTripleAssignment(EncodedTriple Triple, ImmutableArray<CausalDot> Dots);
