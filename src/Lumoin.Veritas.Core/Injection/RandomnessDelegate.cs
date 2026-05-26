namespace Lumoin.Veritas.Core;

/// <summary>
/// Supplies randomness to engine call sites (the context-dependent built-ins such
/// as SPARQL <c>RAND</c>, <c>UUID</c>, <c>STRUUID</c>). The delegate decides
/// whether to produce real entropy, a deterministic value derived from the
/// request, or a constant — entirely under the caller's control. Production
/// defaults live in <see cref="VeritasRandomness"/>.
/// </summary>
/// <param name="request">What the caller is asking for.</param>
/// <returns>The randomness value; its populated field matches <see cref="RandomnessRequest.Kind"/>.</returns>
public delegate RandomnessValue RandomnessDelegate(in RandomnessRequest request);
