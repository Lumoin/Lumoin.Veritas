namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// One named rule in a rewrite pipeline's ordered list: the rule delegate, the unique name its trace
/// provenance carries, and whether the rule participates in fixpoint passes beyond the first.
/// </summary>
/// <param name="Name">The rule's unique name — the label every <c>RewriteApplied</c> trace event carries.</param>
/// <param name="Rule">The rule delegate, bound as a method group.</param>
/// <param name="Fixpoint">Whether the rule runs in passes beyond the first; a rule whose output can enable other rules (or itself over nested redexes) declares <see langword="true"/>, a one-shot rule runs in pass zero only.</param>
public readonly record struct AlgebraRewriteEntry(string Name, AlgebraRewriteDelegate Rule, bool Fixpoint);
