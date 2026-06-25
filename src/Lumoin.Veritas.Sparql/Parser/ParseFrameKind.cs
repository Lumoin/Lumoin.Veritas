namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// The grammar production a <see cref="ParseFrame"/> represents. The driver
/// dispatches on the kind together with the frame's stage counter to advance a
/// production one step at a time, pushing child frames for sub-productions that
/// admit unbounded nesting — so the whole grammar is parsed iteratively over an
/// explicit stack, never by method recursion.
/// </summary>
/// <remarks>
/// Public because the completion seam surfaces the enclosing-production chain (the open frame kinds at a
/// caret) to editors; it is a stable grammar vocabulary with no behavior. The <c>ParseFrame</c> /
/// <c>ParseStatus</c> machinery stays internal.
/// </remarks>
public enum ParseFrameKind
{
    /// <summary>The top-level request: prologue, then query-form dispatch.</summary>
    Request,

    /// <summary>The <c>BASE</c> / <c>PREFIX</c> prologue.</summary>
    Prologue,

    /// <summary>A <c>SELECT</c> clause: modifiers and the projection list (or <c>*</c>).</summary>
    SelectClause,

    /// <summary>A <c>CONSTRUCT</c> template.</summary>
    ConstructTemplate,

    /// <summary>A <c>DESCRIBE</c> target list (or <c>*</c>).</summary>
    DescribeTargets,

    /// <summary>The <c>WHERE</c> clause wrapping a group graph pattern.</summary>
    WhereClause,

    /// <summary>A group graph pattern: the members within a <c>{ ... }</c> block.</summary>
    GroupGraphPattern,

    /// <summary>A contiguous block of triple patterns (a basic graph pattern block).</summary>
    TripleBlock,

    /// <summary>One triple or triple-with-property-list within a block.</summary>
    Triple,

    /// <summary>A predicate-object list attached to a subject.</summary>
    PropertyList,

    /// <summary>An object list attached to a predicate.</summary>
    ObjectList,

    /// <summary>A property-path expression (its own precedence sub-driver).</summary>
    PropertyPath,

    /// <summary>An expression (the Pratt precedence sub-driver).</summary>
    Expression,

    /// <summary>A <c>FILTER</c> constraint.</summary>
    Filter,

    /// <summary>A <c>BIND(expr AS ?var)</c> member.</summary>
    Bind,

    /// <summary>An <c>OPTIONAL { ... }</c> member.</summary>
    OptionalPattern,

    /// <summary>A <c>MINUS { ... }</c> member.</summary>
    MinusPattern,

    /// <summary>A <c>{ ... } UNION { ... }</c> alternation.</summary>
    UnionPattern,

    /// <summary>A <c>GRAPH term { ... }</c> indirection.</summary>
    GraphPattern,

    /// <summary>A <c>SERVICE term { ... }</c> federated pattern.</summary>
    ServicePattern,

    /// <summary>A nested sub-<c>SELECT</c>.</summary>
    SubSelect,

    /// <summary>An inline <c>VALUES</c> data block.</summary>
    Values,

    /// <summary>An <c>ORDER BY</c> clause.</summary>
    OrderBy,

    /// <summary>A <c>GROUP BY</c> clause.</summary>
    GroupBy,

    /// <summary>A <c>HAVING</c> clause.</summary>
    Having,

    /// <summary>A blank-node property list <c>[ ... ]</c> used as a term.</summary>
    BlankNodePropertyList,

    /// <summary>An RDF collection <c>( ... )</c> used as a term.</summary>
    Collection,

    /// <summary>An RDF 1.2 reified triple <c>&lt;&lt; ... &gt;&gt;</c>.</summary>
    ReifiedTriple,

    /// <summary>An RDF 1.2 triple term <c>&lt;&lt;( ... )&gt;&gt;</c>.</summary>
    TripleTerm,

    /// <summary>An RDF 1.2 annotation block <c>{| predicate-object list |}</c> attached to an object.</summary>
    AnnotationBlock,

    /// <summary>A parenthesised, comma-separated argument or expression list: built-in / function call arguments, a <c>COALESCE</c> / <c>IF</c> list, or an <c>IN</c> set.</summary>
    ArgumentList,

    /// <summary>A path sequence: <c>p1 / p2 / ...</c> (the <c>/</c>-separated level of a property path).</summary>
    PathSequence,

    /// <summary>A single path element: an optional inverse <c>^</c>, a path primary, and an optional <c>?</c> / <c>*</c> / <c>+</c> quantifier.</summary>
    PathElement,

    /// <summary>A negated property set: <c>!iri</c> or <c>!( ... )</c>.</summary>
    PathNegatedSet,

    /// <summary>One SPARQL Update operation (<c>INSERT DATA</c>, <c>DELETE WHERE</c>, a modify, a graph-management op, …).</summary>
    UpdateOperation,

    /// <summary>A quad block <c>{ triples … GRAPH g { … } … }</c>: the body of <c>INSERT</c>/<c>DELETE DATA</c> and the templates of a modify.</summary>
    Quads,

    /// <summary>A modify operation body: optional <c>WITH</c>, optional <c>DELETE</c>/<c>INSERT</c> templates, <c>USING</c> clauses, and the <c>WHERE</c> pattern.</summary>
    Modify
}
