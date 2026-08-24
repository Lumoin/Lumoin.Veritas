namespace Lumoin.Veritas.Geo;

/// <summary>
/// The severity a geometry-literal diagnosis carries. The four states are structural, not a judgment
/// table: each falls out of which layer of the datatype's own stack answered, so a diagnosis never
/// conflates a grammar violation with a body the engine merely cannot evaluate.
/// <see cref="UnsupportedDatatype"/> is the abstention at ordinal zero, so a defaulted value never
/// claims anything about a literal.
/// </summary>
public enum GeoLiteralDiagnosisStatus
{
    /// <summary>
    /// The datatype IRI names none of the geometry-literal datatypes this face answers for — the sound
    /// abstention, and the zero default. Nothing is claimed about the body.
    /// </summary>
    UnsupportedDatatype = 0,

    /// <summary>
    /// The body stands under its datatype: the lexical layer did not refuse it and the format's codec
    /// reader read it, or the body denotes the empty geometry, or the datatype's own layers certify it
    /// with no reader beyond them.
    /// </summary>
    Valid,

    /// <summary>
    /// The datatype's validator tolerates the body — it answers valid, or it abstains — yet the format's
    /// codec reader refuses it, so no evaluation over the literal can succeed. Structural thinness, an
    /// uncertified curve grammar, and a body past a reader's nesting bound land here.
    /// </summary>
    Warning,

    /// <summary>The body breaks its datatype's certified grammar: the validator itself answers invalid.</summary>
    Invalid,
}
