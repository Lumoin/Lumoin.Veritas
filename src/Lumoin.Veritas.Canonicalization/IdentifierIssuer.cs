using System.Collections.Generic;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Issues sequential blank node identifiers during the RDFC-1.0 canonicalization algorithm.
/// </summary>
/// <remarks>
/// The RDFC-1.0 algorithm maintains multiple issuer instances simultaneously:
/// one canonical issuer (prefix <c>c14n</c>) for final assignments, and temporary
/// issuers (prefix <c>b</c>) used during the n-degree hash computation. This type
/// tracks which original blank node identifiers have been mapped to issued identifiers
/// and supports cloning for the temporary issuer pattern.
/// </remarks>
internal sealed class IdentifierIssuer
{
    private readonly string prefix;
    private int counter;
    private readonly Dictionary<string, string> issuedMap = [];
    private readonly List<string> issuedOrder = [];

    /// <summary>
    /// Initializes a new <see cref="IdentifierIssuer"/> with the given prefix and starting counter.
    /// </summary>
    /// <param name="prefix">The prefix for issued identifiers (e.g., <c>"c14n"</c> or <c>"b"</c>).</param>
    /// <param name="startAt">The initial counter value. Defaults to zero.</param>
    internal IdentifierIssuer(string prefix, int startAt = 0)
    {
        this.prefix = prefix;
        counter = startAt;
    }

    /// <summary>
    /// Issues a new identifier for the given original blank node label.
    /// If an identifier has already been issued for this label, returns the existing one.
    /// </summary>
    /// <param name="original">The original blank node label to issue an identifier for.</param>
    /// <returns>The issued identifier (without the <c>_:</c> prefix).</returns>
    internal string Issue(string original)
    {
        if (issuedMap.TryGetValue(original, out string? existing))
        {
            return existing;
        }

        string issued = $"{prefix}{counter}";
        counter++;
        issuedMap[original] = issued;
        issuedOrder.Add(original);
        return issued;
    }

    /// <summary>
    /// The original blank node labels in the order identifiers were issued
    /// for them. RDFC-1.0 §4.5.3 step 6.3.1 assigns canonical identifiers in
    /// exactly this order, which is what disambiguates automorphic blank
    /// nodes deterministically.
    /// </summary>
    internal IReadOnlyList<string> IssuedOrder => issuedOrder;

    /// <summary>
    /// Determines whether an identifier has already been issued for the given label.
    /// </summary>
    internal bool HasIssued(string original)
    {
        return issuedMap.ContainsKey(original);
    }

    /// <summary>
    /// Reads the identifier already issued for a label without issuing a new one — the read-only lookup the
    /// "Hash Related Blank Node" step (RDFC-1.0 §4.8.2) uses to prefer a temporary id over a first-degree hash.
    /// </summary>
    /// <param name="original">The original blank node label.</param>
    /// <param name="issued">The issued identifier (without the <c>_:</c> prefix), when one exists.</param>
    /// <returns><see langword="true"/> when an identifier has been issued for <paramref name="original"/>.</returns>
    internal bool TryGetIssued(string original, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? issued)
    {
        return issuedMap.TryGetValue(original, out issued);
    }

    /// <summary>
    /// Creates an independent copy of this issuer with the same state.
    /// Used to branch during n-degree hash computation without affecting the original.
    /// </summary>
    internal IdentifierIssuer Clone()
    {
        IdentifierIssuer clone = new(prefix, counter);
        foreach(string original in issuedOrder)
        {
            clone.issuedMap[original] = issuedMap[original];
            clone.issuedOrder.Add(original);
        }

        return clone;
    }
}
