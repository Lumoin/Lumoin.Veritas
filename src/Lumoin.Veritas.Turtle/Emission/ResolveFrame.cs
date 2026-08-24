using System.Diagnostics;
using Lumoin.Veritas.Turtle.Ast;

namespace Lumoin.Veritas.Turtle.Emission;

/// <summary>
/// One frame on the quad emitter's term-resolution stack. The emitter
/// resolves a term tree to RDF terms bottom-up without recursion: each
/// compound term is visited twice — once to schedule its children, and
/// once (with <see cref="Expanded"/> set) to combine the
/// already-resolved children into the term's value.
/// </summary>
/// <param name="Term">The AST term this frame resolves.</param>
/// <param name="Expanded">
/// <c>false</c> on first visit (schedule children); <c>true</c> on the
/// second visit (children resolved, ready to combine).
/// </param>
[DebuggerDisplay("ResolveFrame {Term.GetType().Name,nq} expanded={Expanded}")]
internal readonly record struct ResolveFrame(Term Term, bool Expanded);
