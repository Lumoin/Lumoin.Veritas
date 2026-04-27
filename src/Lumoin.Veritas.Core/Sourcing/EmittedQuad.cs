using System.Diagnostics;

namespace Lumoin.Veritas.Core.Sourcing;

/// <summary>
/// A <see cref="Quad"/> paired with an optional reference to the AST node
/// that produced it. The structural unit of provenance throughout the
/// Veritas pipeline — parsing, inference, validation, query — and the
/// witness shape that proof systems consume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance role.</b> When a parser, expander, inference engine, or
/// query evaluator emits a quad it knows the source — the parsed token,
/// the rule that fired, the input pattern that matched. Carrying that
/// provenance alongside the quad lets downstream consumers map results
/// back to their causes: editor diagnostics highlight the offending
/// source bytes, inference reports cite the input triples a derived
/// triple came from, validation results identify the data that triggered
/// a constraint failure.
/// </para>
/// <para>
/// <b>Witness role.</b> The same shape composes with proof systems that
/// operate over RDF data. A SNARK or folding-scheme proof of a property
/// over a derived result needs the input quads that contributed to it;
/// that input set is structurally the list of <see cref="EmittedQuad"/>s
/// the operation consumed. The paramorphic recursion-scheme primitives
/// in the library are shaped for exactly this: a fold whose algebra
/// produces a result alongside the original triples it derived from.
/// Sister libraries that implement zero-knowledge proofs consume the
/// witness chain without further adaptation.
/// </para>
/// <para>
/// <b>Null source is legitimate.</b> Some operations produce quads with
/// no AST origin: in-memory API insertions, programmatic graph builders,
/// formats that do not yet carry source positions. The
/// <see cref="Source"/> field is nullable to admit these cases honestly
/// rather than fabricating empty references. Consumers that require
/// source provenance test for null and degrade gracefully when it is
/// unavailable.
/// </para>
/// <para>
/// <b>Composition with named graphs.</b> The <see cref="Quad"/>'s
/// <c>Graph</c> field identifies the named graph the triple belongs to
/// — the unit of ownership and access control. <see cref="Source"/>
/// identifies the textual origin within whatever document was parsed.
/// The two are orthogonal: a single source document may emit triples
/// into multiple named graphs, and a single named graph may receive
/// triples from multiple source documents. Both fields together describe
/// "where this triple came from" at the two relevant levels.
/// </para>
/// </remarks>
/// <param name="Quad">The emitted quad.</param>
/// <param name="Source">
/// The document node that produced the quad, when known; <c>null</c> for
/// quads with no document origin.
/// </param>
[DebuggerDisplay("EmittedQuad {Quad,nq} from {Source}")]
public readonly record struct EmittedQuad(Quad Quad, DocumentNodeRef? Source);
