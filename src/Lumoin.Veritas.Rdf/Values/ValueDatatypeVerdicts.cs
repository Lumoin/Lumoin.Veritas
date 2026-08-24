namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// The three-valued lexical validity of one lexical form within a value datatype's lexical space. The
/// abstention value is <see cref="Indeterminate"/> at ordinal zero, so <c>default(ValueLexicalValidity)</c>
/// never asserts validity or invalidity — an abstaining verdict leaves the engine's built-in treatment of
/// the lexical form standing.
/// </summary>
public enum ValueLexicalValidity
{
    /// <summary>Validity could not be decided — the sound abstention, and the zero default.</summary>
    Indeterminate = 0,

    /// <summary>The lexical form is provably in the datatype's lexical space.</summary>
    Valid,

    /// <summary>The lexical form is provably outside the datatype's lexical space.</summary>
    Invalid,
}

/// <summary>
/// The three-valued value identity of two lexical forms within one value datatype. The abstention value is
/// <see cref="Indeterminate"/> at ordinal zero, so <c>default(ValueIdentity)</c> never asserts sameness or
/// distinctness — an abstaining verdict leaves the engine's exact term-identity semantics standing.
/// </summary>
public enum ValueIdentity
{
    /// <summary>Identity could not be decided — the sound abstention, and the zero default.</summary>
    Indeterminate = 0,

    /// <summary>The two lexical forms denote the same data value.</summary>
    Same,

    /// <summary>The two lexical forms denote distinct data values.</summary>
    Distinct,
}
