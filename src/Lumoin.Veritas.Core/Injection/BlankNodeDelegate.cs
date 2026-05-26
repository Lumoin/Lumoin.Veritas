namespace Lumoin.Veritas.Core;

/// <summary>
/// Produces a blank-node label (interned into the request's pool) for a syntactic
/// or solution-time occurrence. Centralising allocation behind a delegate lets the
/// caller choose fresh-per-call labels, call-site-deterministic labels, or
/// per-solution-correlated labels. Defaults live in <see cref="VeritasBlankNodes"/>.
/// </summary>
/// <param name="request">The occurrence the label is needed for.</param>
/// <returns>The blank-node label, interned into <see cref="BlankNodeRequest.Pool"/>.</returns>
public delegate Utf8String BlankNodeDelegate(in BlankNodeRequest request);
