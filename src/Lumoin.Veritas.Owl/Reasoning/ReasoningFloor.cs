using System.Diagnostics;
using Lumoin.Veritas.Owl.Profiles;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The expressiveness floor of one store generation — everything strategy
/// selection reads, held without the decoded ontology document so the
/// cache costs verdicts, not the mapped axiom set.
/// </summary>
/// <remarks>
/// The floor is a property of the data, not of the policy: whether the
/// rendezvous prefers the RDFS pass when it suffices is a per-request
/// policy decision over <see cref="IsRdfsShaped"/>, never folded into the
/// cached value. An assertion-only commit cannot move the floor — plain
/// individual assertions are within every profile grammar and the RDFS
/// shape — which is what lets <see cref="ReasoningRendezvous.Advance"/>
/// carry the floor across such commits instead of re-detecting.
/// </remarks>
/// <param name="Memberships">The profile memberships the grammar check detected.</param>
/// <param name="IsRdfsShaped">Whether every axiom stays within the RDFS streaming pass's vocabulary.</param>
/// <param name="IsWithinRl">Whether the content is inside the OWL 2 RL grammar — the in-engine materialization ceiling.</param>
/// <param name="Module">The beyond-ceiling module, or <c>null</c> when <paramref name="IsWithinRl"/> holds.</param>
[DebuggerDisplay("ReasoningFloor Memberships={Memberships} RdfsShaped={IsRdfsShaped} WithinRl={IsWithinRl}")]
public sealed record ReasoningFloor(
    OwlProfiles Memberships,
    bool IsRdfsShaped,
    bool IsWithinRl,
    ReasoningModule? Module);
