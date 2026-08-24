namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// A directioned role id in intake (raw) space: the id
/// <see cref="ContextSymbolTable.RoleOf(Lumoin.Veritas.Core.Utf8String)"/> interns
/// for an asserted spelling, before the mutual-<c>⊑*</c>-inclusion quotient. The
/// ground slice — the asserted-edge graph, its RBox closure tables, and the clash
/// obligations — is keyed uniformly in this space. Raw and representative ids never
/// compare or mix directly; the sole narrowing producer is the clausifier's role
/// quotient, so a representative-keyed read against raw-keyed data cannot compile
/// by accident.
/// </summary>
/// <remarks>
/// The constructor is <see langword="internal"/> — the narrowest scope the language
/// admits for a cross-file producer. External assemblies cannot mint a raw role id;
/// inside the assembly the symbol table's interning and the quotient loop are the
/// sanctioned producers, the same accepted residue the canonical-term newtype
/// records.
/// </remarks>
internal readonly record struct RawRoleId
{
    /// <summary>The underlying dense directioned id (<c>forward = 2k</c>, <c>inverse = 2k + 1</c>).</summary>
    public int Value { get; }

    /// <summary>Wraps a directioned intake id; the symbol table and the quotient loop are the sanctioned callers.</summary>
    /// <param name="value">The dense directioned id.</param>
    internal RawRoleId(int value)
    {
        Value = value;
    }
}

/// <summary>
/// A directioned role id in representative (quotient) space: the canonical minimal
/// member of its mutual-<c>⊑*</c>-inclusion class, produced only by the
/// clausifier's <c>Rep</c> after the RBox quotient (a role interned after the
/// quotient self-represents). Clause emission, the role automata, the loop set,
/// and the successor-sharing keys all live in this space. Unwrapping through
/// <see cref="RawMemberId"/> is the visible, deliberate act this type exists to
/// force at every ground/clause crossing.
/// </summary>
/// <remarks>
/// The constructor is <see langword="internal"/> — the narrowest scope the
/// language admits for a cross-file producer; <c>Rep</c>, the post-quotient fresh
/// mints, and the packed-literal rehydration named on
/// <see cref="FromClauseSymbol"/> are the sanctioned in-assembly producers.
/// </remarks>
internal readonly record struct RoleRepresentative
{
    /// <summary>The underlying dense directioned id of the class's minimal member.</summary>
    public int Value { get; }

    /// <summary>
    /// The representative read as a member of its own class: a representative IS a
    /// raw directioned id (the class's minimal member), so asserting a raw-space
    /// fact under it asserts that member's fact. This widening is sound wherever
    /// the raw-space consumer closes over the mutual-inclusion arcs — the ground
    /// graph's closure lifts a representative-member edge onto every spelling of
    /// both coupled classes. The narrowing direction has no such member and exists
    /// only as the quotient's <c>Rep</c>.
    /// </summary>
    public RawRoleId RawMemberId
    {
        get
        {
            return new RawRoleId(Value);
        }
    }

    /// <summary>Wraps a representative id; the quotient, the post-quotient fresh mints, and <see cref="FromClauseSymbol"/> are the sanctioned callers.</summary>
    /// <param name="value">The dense directioned id of the class's minimal member.</param>
    internal RoleRepresentative(int value)
    {
        Value = value;
    }

    /// <summary>
    /// Rehydrates a packed DL-literal role symbol: clause literals carry
    /// representative-rewritten symbols by construction (the clausifier's role-atom
    /// builder is the sole packer and packs only representatives), so a symbol read
    /// back off a literal is a representative-space value. This is the one named
    /// read-boundary producer; every other conversion into this space goes through
    /// the quotient's <c>Rep</c>.
    /// </summary>
    /// <param name="symbol">The role symbol read from a packed DL literal.</param>
    /// <returns>The symbol as a representative-space id.</returns>
    public static RoleRepresentative FromClauseSymbol(int symbol)
    {
        return new RoleRepresentative(symbol);
    }
}
