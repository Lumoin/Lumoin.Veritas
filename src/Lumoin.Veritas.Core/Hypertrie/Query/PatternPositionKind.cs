using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// Discriminator for <see cref="PatternPosition"/>: a position in
/// a triple pattern is either a bound term (a constant) or a
/// variable.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "PatternPositionKind is a discriminator stored alongside other fields inside the PatternPosition value type. A query carries up to three positions per pattern and many patterns per query; a one-byte discriminator keeps the position struct compact and packs cleanly with the other fields.")]
public enum PatternPositionKind: byte
{
    /// <summary>The position is a bound term — a constant value the pattern matches against.</summary>
    Bound = 0,

    /// <summary>The position is a query variable — values matched here will be bound by the query engine.</summary>
    Variable = 1,
}
