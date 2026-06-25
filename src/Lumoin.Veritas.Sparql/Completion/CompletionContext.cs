using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.Sparql.Completion;

/// <summary>How a variable's RDF datatype was resolved, weakest-to-strongest provenance.</summary>
public enum DatatypeSource
{
    /// <summary>No datatype could be resolved.</summary>
    Unknown,

    /// <summary>Observed by sampling the data with <c>DATATYPE()</c> over a binding of the predicate.</summary>
    DataSample,

    /// <summary>Declared by an <c>rdfs:range</c> (or a materialized OWL data-property range).</summary>
    RdfsRange,

    /// <summary>Declared by a SHACL property shape's <c>sh:datatype</c>.</summary>
    ShaclShape
}

/// <summary>Which triple position a variable occupies for a paired predicate.</summary>
public enum TermPosition
{
    /// <summary>The subject position.</summary>
    Subject,

    /// <summary>The predicate position.</summary>
    Predicate,

    /// <summary>The object position.</summary>
    Object
}

/// <summary>A variable visible at the caret, with its best-available resolved RDF datatype IRI.</summary>
/// <param name="Variable">The in-scope variable.</param>
/// <param name="Datatype">The resolved datatype IRI, or <see langword="null"/> when unknown.</param>
/// <param name="DatatypeSource">How the datatype was resolved (the strongest source that produced it).</param>
public readonly record struct ScopeVariable(SparqlVariable Variable, Utf8String? Datatype, DatatypeSource DatatypeSource);

/// <summary>One variable→predicate binding observed in a triple at or enclosing the caret.</summary>
/// <param name="Variable">The variable.</param>
/// <param name="Predicate">The predicate IRI the variable is bound by (absent for path or variable predicates).</param>
/// <param name="Position">The triple position the variable occupies for that predicate.</param>
public readonly record struct VariablePredicate(SparqlVariable Variable, Utf8String Predicate, TermPosition Position);

/// <summary>
/// The grammatical and binding context at a caret offset over a SPARQL query buffer: what may be typed
/// next, which variables are in scope (with best-available datatype), the enclosing production chain, and
/// the variable→predicate links that make typed completion and drill-in possible. Produced store-free by
/// the parser + scope analysis; the datatypes are <see cref="DatatypeSource.Unknown"/> until a store-backed
/// resolver fills them.
/// </summary>
/// <param name="CaretByteOffset">The caret position, as a byte offset into the source.</param>
/// <param name="InScopeVariables">The variables in scope at the caret, in source order.</param>
/// <param name="ExpectedTokens">The token kinds the grammar admits next at the caret.</param>
/// <param name="EnclosingProductions">The open productions, from outermost to innermost, enclosing the caret.</param>
/// <param name="VariablePredicates">The variable→predicate bindings observed in the enclosing group graph pattern.</param>
public sealed record CompletionContext(
    int CaretByteOffset,
    IReadOnlyList<ScopeVariable> InScopeVariables,
    IReadOnlyList<SparqlTokenKind> ExpectedTokens,
    IReadOnlyList<ParseFrameKind> EnclosingProductions,
    IReadOnlyList<VariablePredicate> VariablePredicates);
