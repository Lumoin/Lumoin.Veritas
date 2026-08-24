namespace Lumoin.Veritas.Shacl;

/// <summary>
/// The six node-kind values accepted by <c>sh:nodeKind</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.6.3, <c>sh:nodeKind</c> constrains each value node
/// to one of six kinds. The enum mirrors the SHACL IRIs one-to-one; the
/// shape loader rejects values outside this set.
/// </para>
/// </remarks>
public enum NodeKind
{
    /// <summary><c>sh:BlankNode</c></summary>
    BlankNode = 0,

    /// <summary><c>sh:IRI</c></summary>
    IRI = 1,

    /// <summary><c>sh:Literal</c></summary>
    Literal = 2,

    /// <summary><c>sh:BlankNodeOrIRI</c></summary>
    BlankNodeOrIRI = 3,

    /// <summary><c>sh:BlankNodeOrLiteral</c></summary>
    BlankNodeOrLiteral = 4,

    /// <summary><c>sh:IRIOrLiteral</c></summary>
    IRIOrLiteral = 5
}
