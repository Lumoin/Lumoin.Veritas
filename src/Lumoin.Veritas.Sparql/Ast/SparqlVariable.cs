using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A SPARQL query variable, identified by its name (the text after the leading
/// <c>?</c> or <c>$</c> marker, which the two forms share).
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="Core.Hypertrie.Query.Variable"/>, which is an integer
/// id requiring a registry to resolve its name: the SPARQL AST and algebra are
/// self-contained and carry the name directly, so a tree can be interpreted or
/// serialised without a side table. The hypertrie backend translates a
/// <see cref="SparqlVariable"/> to a BGP <see cref="Core.Hypertrie.Query.Variable"/>
/// at the backend boundary.
/// </para>
/// <para>SPARQL <c>Var</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVar">SPARQL 1.2 §19.8 [Var]</see>.</para>
/// </remarks>
/// <param name="Name">The variable name without its <c>?</c> / <c>$</c> marker.</param>
[DebuggerDisplay("?{Name}")]
public readonly record struct SparqlVariable(Utf8String Name);
