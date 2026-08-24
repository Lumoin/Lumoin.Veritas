using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The per-module interning table for the context clausifier (the DL-clause
/// grammar of the consequence-based SRIQ calculus, KR 2016, Section 2;
/// <see href="https://arxiv.org/abs/1602.04498"/>), mirroring the EL classifier's
/// discipline. Named concept atoms and directioned
/// roles intern to dense ids; fresh structural / automaton-state atoms and fresh
/// automaton-state roles carry a <see langword="null"/> name. Roles are
/// directioned: a role and its inverse take adjacent ids (<c>forward = 2k</c>,
/// <c>inverse = 2k+1</c>), so <see cref="Inverse"/> is a single bit flip. Skolem
/// function symbols are minted only at clausification, and their id order is the
/// total well-founded precedence the term order reads. After clausification
/// returns, exactly ONE bounded channel may mint: <see cref="MintGeneratedNominal"/>,
/// the Nom rule's generated-nominal supply, memoized per (prefix, role) so the
/// nominal label set stays a fixed function of the module and the label-depth
/// bound of the calculus (arXiv:1805.01396 Theorem 4) caps it. Every other
/// symbol sort stays clausification-frozen: the frozen signature remains the
/// termination substrate, amended by that one argued channel.
/// </summary>
internal sealed class ContextSymbolTable
{
    /// <summary>The interned name of the concept atom at each id; a fresh structural or automaton-state atom carries <see langword="null"/>. Seeded so <see cref="Top"/> is id 0 and <see cref="Bottom"/> is id 1.</summary>
    private List<Utf8String?> AtomNames { get; } = [OwlVocabulary.Thing, OwlVocabulary.Nothing];

    /// <summary>The id of each named concept atom.</summary>
    private Dictionary<Utf8String, int> AtomIds { get; }

    /// <summary>The interned base name of the role at each directioned id; a fresh automaton-state role carries <see langword="null"/>. The forward and inverse directions of one role share the base name at their adjacent ids.</summary>
    private List<Utf8String?> RoleNames { get; } = [];

    /// <summary>The forward (even) id of each named role.</summary>
    private Dictionary<Utf8String, int> RoleIds { get; } = [];

    /// <summary>The Skolem function symbols, index = dense id = precedence rank; each entry records the successor filler atom the symbol witnesses, for rendering.</summary>
    private List<int> FunctionSymbols { get; } = [];

    /// <summary>The interned name of the individual at each id; a generated nominal carries <see langword="null"/>. The id order is the global individual precedence the term order reads (mint order), and label monotonicity holds by construction: a generated nominal always interns after its prefix.</summary>
    private List<Utf8String?> IndividualNames { get; } = [];

    /// <summary>The id of each named input individual.</summary>
    private Dictionary<Utf8String, int> IndividualIds { get; } = [];

    /// <summary>The nominal label depth of the individual at each id: zero for an input individual, the prefix's depth plus one for a generated nominal — the statistic the termination wedge observes.</summary>
    private List<int> IndividualDepths { get; } = [];

    /// <summary>The introduction origin of the individual at each id, id-indexed alongside <see cref="IndividualNames"/> and <see cref="IndividualDepths"/>: the bit the key-join candidacy filter reads. A generated nominal carries <see cref="IndividualOrigin.IriDenoted"/> so the depth conjunct alone excludes it, keeping that conjunct load-bearing.</summary>
    private List<IndividualOrigin> IndividualOrigins { get; } = [];

    /// <summary>The first sibling id of the generated-nominal block minted for each (prefix individual, role) pair — the memo that makes <see cref="MintGeneratedNominal"/> return the SAME siblings on a re-fire, keeping the label set a fixed function of the module.</summary>
    private Dictionary<(int Prefix, int Role), int> GeneratedNominalBlocks { get; } = [];

    /// <summary>Whether a re-intern supplied an origin disagreeing with the one already recorded for a key — the un-namespaced blank-label/IRI key-collision residual: two individuals whose interning keys coincide but whose origins differ. The recorded origin is never overwritten; this marker records the collision for the key-join candidacy layer and has no production consumer at this stage.</summary>
    public bool HasIndividualOriginConflict { get; private set; }

    /// <summary>The first key whose re-intern disagreed on origin, or <see langword="null"/> when no such collision has occurred — the recorded witness of that collision.</summary>
    public Utf8String? ConflictingIndividualKey { get; private set; }

    /// <summary>The concept atom id of <c>owl:Thing</c> (the top concept).</summary>
    public const int Top = 0;

    /// <summary>The concept atom id of <c>owl:Nothing</c> (the bottom concept).</summary>
    public const int Bottom = 1;

    /// <summary>Initialises the table with the top and bottom concepts interned.</summary>
    public ContextSymbolTable()
    {
        AtomIds = new Dictionary<Utf8String, int>
        {
            [OwlVocabulary.Thing] = Top,
            [OwlVocabulary.Nothing] = Bottom,
        };
    }

    /// <summary>The number of fresh (structural and automaton-state) concept atoms minted so far.</summary>
    public int FreshAtoms { get; private set; }

    /// <summary>The number of fresh (automaton-state) roles minted so far.</summary>
    public int FreshRoles { get; private set; }

    /// <summary>The number of fresh counting roles (DL4 auxiliaries) minted so far — a subset of <see cref="FreshRoles"/>, since every counting role is a fresh role.</summary>
    public int CountingRoles { get; private set; }

    /// <summary>The number of Skolem function symbols minted so far.</summary>
    public int FunctionSymbolCount
    {
        get
        {
            return FunctionSymbols.Count;
        }
    }

    /// <summary>The number of directioned role ids interned so far — both directions of every named or fresh role.</summary>
    public int RoleCount
    {
        get
        {
            return RoleNames.Count;
        }
    }

    /// <summary>Interns a named concept atom, mapping <c>owl:Thing</c>/<c>owl:Nothing</c> to the seeded top/bottom ids.</summary>
    /// <param name="iri">The class IRI.</param>
    /// <returns>The concept atom id.</returns>
    public int AtomOf(Utf8String iri)
    {
        if(AtomIds.TryGetValue(iri, out int existing))
        {
            return existing;
        }

        int atom = AtomNames.Count;
        AtomNames.Add(iri);
        AtomIds[iri] = atom;

        return atom;
    }

    /// <summary>Mints a fresh, unnamed concept atom (a structural or automaton-state name).</summary>
    /// <returns>The fresh concept atom id.</returns>
    public int FreshAtom()
    {
        int atom = AtomNames.Count;
        AtomNames.Add(null);
        FreshAtoms++;

        return atom;
    }

    /// <summary>
    /// Returns a class reference for a fresh concept atom, assigning it a synthetic
    /// IRI the first time so a structural-transformation definition can re-enter
    /// the normalization worklist and resolve back to the same id.
    /// </summary>
    /// <param name="atom">The fresh concept atom id.</param>
    /// <returns>The class reference over the atom's synthetic IRI.</returns>
    public OwlClassReference AtomReference(int atom)
    {
        Utf8String name = AtomNames[atom] ?? Utf8Strings.From($"urn:veritas:ctx:a{atom}");
        AtomNames[atom] = name;
        AtomIds[name] = atom;

        return new OwlClassReference(new NamedNode(name));
    }

    /// <summary>Interns a named role in its forward direction.</summary>
    /// <param name="iri">The object-property IRI.</param>
    /// <returns>The forward (even) raw role id.</returns>
    public RawRoleId RoleOf(Utf8String iri)
    {
        if(RoleIds.TryGetValue(iri, out int existing))
        {
            return new RawRoleId(existing);
        }

        int forward = RoleNames.Count;
        RoleNames.Add(iri);
        RoleNames.Add(iri);
        RoleIds[iri] = forward;

        return new RawRoleId(forward);
    }

    /// <summary>Interns an object-property expression to its directioned raw role id — the forward id for a named property, its inverse for an <c>ObjectInverseOf</c>.</summary>
    /// <param name="expression">The object-property expression.</param>
    /// <returns>The directioned raw role id.</returns>
    public RawRoleId RoleOf(OwlObjectPropertyExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        RawRoleId forward = RoleOf(expression.Property.Iri);

        return expression.IsInverse ? Inverse(forward) : forward;
    }

    /// <summary>The interned base IRI of a named directioned role id — both directions share the base name; a fresh automaton-state or counting role has none.</summary>
    /// <param name="role">The dense directioned id.</param>
    /// <returns>The base IRI, or <see langword="null"/> for a fresh or out-of-range role.</returns>
    private Utf8String? RoleIriOf(int role)
    {
        return role >= 0 && role < RoleNames.Count ? RoleNames[role] : null;
    }

    /// <summary>The interned base IRI of a raw directioned role — both directions share the base name.</summary>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The base IRI, or <see langword="null"/> for an out-of-range role.</returns>
    public Utf8String? RoleIri(RawRoleId role)
    {
        return RoleIriOf(role.Value);
    }

    /// <summary>The interned base IRI of a representative directioned role — the class's minimal member's base name; a fresh automaton-state or counting role has none.</summary>
    /// <param name="role">The representative directioned role.</param>
    /// <returns>The base IRI, or <see langword="null"/> for a fresh role.</returns>
    public Utf8String? RoleIri(RoleRepresentative role)
    {
        return RoleIriOf(role.Value);
    }

    /// <summary>Mints a fresh, unnamed role together with its inverse direction (an automaton-state role). Fresh roles mint only after the RBox quotient, so the returned id self-represents.</summary>
    /// <returns>The fresh forward (even) representative role.</returns>
    public RoleRepresentative FreshRole()
    {
        int forward = RoleNames.Count;
        RoleNames.Add(null);
        RoleNames.Add(null);
        FreshRoles++;

        return new RoleRepresentative(forward);
    }

    /// <summary>Mints a fresh counting role (a DL4 auxiliary) together with its inverse direction, counting it both as a fresh role and as a counting role. Counting roles mint only after the RBox quotient, so the returned id self-represents.</summary>
    /// <returns>The fresh forward (even) representative role.</returns>
    public RoleRepresentative FreshCountingRole()
    {
        RoleRepresentative forward = FreshRole();
        CountingRoles++;

        return forward;
    }

    /// <summary>The inverse of a directioned role id — the adjacent id in the interleaved pair.</summary>
    /// <param name="role">The directioned role id.</param>
    /// <returns>The inverse role id.</returns>
    public static int Inverse(int role)
    {
        return role ^ 1;
    }

    /// <summary>The inverse of a raw directioned role — the adjacent id in the interleaved pair.</summary>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The inverse raw role.</returns>
    public static RawRoleId Inverse(RawRoleId role)
    {
        return new RawRoleId(role.Value ^ 1);
    }

    /// <summary>Whether a directioned role id is an inverse (odd) direction.</summary>
    /// <param name="role">The directioned role id.</param>
    /// <returns><see langword="true"/> for an inverse-direction role.</returns>
    public static bool IsInverse(int role)
    {
        return (role & 1) == 1;
    }

    /// <summary>The forward (even) id of the role pair a directioned id belongs to.</summary>
    /// <param name="role">The directioned role id.</param>
    /// <returns>The forward role id.</returns>
    public static int Forward(int role)
    {
        return role & ~1;
    }

    /// <summary>The forward (even) member of the raw role pair a directioned raw id belongs to.</summary>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The forward raw role.</returns>
    public static RawRoleId Forward(RawRoleId role)
    {
        return new RawRoleId(role.Value & ~1);
    }

    /// <summary>Mints a Skolem function symbol at clausification; its dense id is its precedence rank.</summary>
    /// <param name="fillerAtom">The successor filler atom the symbol witnesses (for rendering only).</param>
    /// <returns>The function symbol id.</returns>
    public int MintFunctionSymbol(int fillerAtom)
    {
        int symbol = FunctionSymbols.Count;
        FunctionSymbols.Add(fillerAtom);

        return symbol;
    }

    /// <summary>The number of individuals interned so far — input individuals and generated nominals together; the id space of <see cref="DlTermKind.Individual"/> payloads.</summary>
    public int IndividualCount
    {
        get
        {
            return IndividualNames.Count;
        }
    }

    /// <summary>The number of generated nominals minted so far — the individuals beyond the input population.</summary>
    public int GeneratedNominalCount { get; private set; }

    /// <summary>The deepest nominal label minted so far: zero while only input individuals exist, the longest generated-nominal label length otherwise.</summary>
    public int MaxNominalLabelDepth { get; private set; }

    /// <summary>Interns a named or blank-node input individual at clausification; the dense id order is the global individual precedence the term order reads. The <paramref name="origin"/> is recorded id-indexed; a re-intern of an already-known key never overwrites the recorded origin, and a disagreeing origin records the key-collision residual on <see cref="HasIndividualOriginConflict"/>.</summary>
    /// <param name="iri">The individual's interning key: the IRI of a named node or the label of a blank node.</param>
    /// <param name="origin">The individual's introduction origin, supplied by the mint site because the key alone cannot recover it.</param>
    /// <returns>The individual id.</returns>
    public int InternIndividual(Utf8String iri, IndividualOrigin origin)
    {
        if(IndividualIds.TryGetValue(iri, out int existing))
        {
            if(IndividualOrigins[existing] != origin && !HasIndividualOriginConflict)
            {
                HasIndividualOriginConflict = true;
                ConflictingIndividualKey = iri;
            }

            return existing;
        }

        int individual = IndividualNames.Count;
        IndividualNames.Add(iri);
        IndividualDepths.Add(0);
        IndividualOrigins.Add(origin);
        IndividualIds[iri] = individual;

        return individual;
    }

    /// <summary>
    /// The one in-saturation mint channel: returns the <paramref name="count"/>
    /// generated-nominal siblings <c>o_{rho·S^1}</c> … <c>o_{rho·S^count}</c> for a
    /// prefix individual <c>o_rho</c> and role <c>S</c>, minting them on the first
    /// call and returning the SAME contiguous id block on every later call — the
    /// nominal label set is a fixed function of the module, so a re-fired Nom rule
    /// never grows the signature. Mint order realizes the label-monotonicity
    /// requirement of the global order (a nominal interns after its prefix, so a
    /// longer label always outranks its prefixes). The caller charges the budget
    /// per NEWLY minted nominal.
    /// </summary>
    /// <param name="prefixIndividualId">The prefix individual <c>o_rho</c> whose label the siblings extend.</param>
    /// <param name="roleId">The directioned role <c>S</c> labelling the extension.</param>
    /// <param name="count">The sibling count <c>K</c>, the module's counting bound.</param>
    /// <param name="firstSiblingId">The first sibling's individual id; the block is contiguous, so sibling <c>i</c> is <c>firstSiblingId + i - 1</c>.</param>
    /// <returns><see langword="true"/> when the block was newly minted, <see langword="false"/> when the memoized block was returned.</returns>
    public bool MintGeneratedNominal(int prefixIndividualId, int roleId, int count, out int firstSiblingId)
    {
        if(GeneratedNominalBlocks.TryGetValue((prefixIndividualId, roleId), out firstSiblingId))
        {
            return false;
        }

        Debug.Assert(prefixIndividualId < IndividualNames.Count, "A generated nominal extends an already-interned prefix, so mint order realizes label monotonicity.");

        int depth = IndividualDepths[prefixIndividualId] + 1;
        firstSiblingId = IndividualNames.Count;
        for(int i = 0; i < count; i++)
        {
            IndividualNames.Add(null);
            IndividualDepths.Add(depth);

            //A generated nominal has no IRI; it takes the candidate origin so the depth
            //conjunct is its sole key-join exclusion, leaving the origin bit load-bearing
            //only for the input blank-node/IRI distinction.
            IndividualOrigins.Add(IndividualOrigin.IriDenoted);
        }

        GeneratedNominalCount += count;
        if(depth > MaxNominalLabelDepth)
        {
            MaxNominalLabelDepth = depth;
        }

        GeneratedNominalBlocks[(prefixIndividualId, roleId)] = firstSiblingId;

        return true;
    }

    /// <summary>The nominal label depth of an individual: zero for an input individual, the label length for a generated nominal.</summary>
    /// <param name="individualId">The individual id.</param>
    /// <returns>The label depth.</returns>
    public int IndividualDepth(int individualId)
    {
        return IndividualDepths[individualId];
    }

    /// <summary>The recorded introduction origin of an individual id — the bit the key-join candidacy filter conjoins with the label depth.</summary>
    /// <param name="individualId">The individual id.</param>
    /// <returns>The individual's origin.</returns>
    public IndividualOrigin OriginOf(int individualId)
    {
        return IndividualOrigins[individualId];
    }

    /// <summary>Whether an individual id is a key-join candidacy origin: an IRI-denoted individual at label depth zero. The origin conjunct excludes blank-node individuals; the depth conjunct excludes generated nominals, which <see cref="MintGeneratedNominal"/> interns at depth one or greater. Both conjuncts are required — the origin bit distinguishes an input IRI from an input blank node, the depth conjunct excludes the generated ids the bit alone does not.</summary>
    /// <param name="individualId">The individual id.</param>
    /// <returns><see langword="true"/> when the id is an IRI-denoted, depth-zero individual eligible for the key join.</returns>
    public bool IsKeyJoinCandidateOrigin(int individualId)
    {
        return IndividualOrigins[individualId] == IndividualOrigin.IriDenoted && IndividualDepths[individualId] == 0;
    }

    /// <summary>Looks up an interned individual id by its key without minting — the IRI of a named node or the label of a blank node.</summary>
    /// <param name="key">The individual's interning key.</param>
    /// <param name="individualId">The individual id when the key is interned, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the key is interned.</returns>
    public bool TryIndividualId(Utf8String key, out int individualId)
    {
        if(IndividualIds.TryGetValue(key, out individualId))
        {
            return true;
        }

        individualId = -1;

        return false;
    }

    /// <summary>Looks up an interned individual's key by its id — the IRI of a named node or the label of a blank node, the forward map the root-tier key join reads to index the key-value store by an individual's spelling. A generated nominal carries no stored key.</summary>
    /// <param name="individualId">The individual id.</param>
    /// <param name="key">The individual's interning key when one is stored, or the default.</param>
    /// <returns><see langword="true"/> when the id has a stored key; <see langword="false"/> for a generated nominal.</returns>
    public bool TryIndividualKey(int individualId, out Utf8String key)
    {
        if(IndividualNames[individualId] is Utf8String name)
        {
            key = name;

            return true;
        }

        key = default;

        return false;
    }

    /// <summary>Renders an individual id for the debugging renderer: the interned IRI, or a generated-nominal placeholder.</summary>
    /// <param name="individualId">The individual id.</param>
    /// <returns>The rendered individual name.</returns>
    public string RenderIndividual(int individualId)
    {
        return IndividualNames[individualId] is Utf8String name ? name.ToString() : $"_o{individualId}";
    }

    /// <summary>Renders a concept atom id for the debugging renderer: the top/bottom names, the interned IRI, or a fresh-atom placeholder.</summary>
    /// <param name="atom">The concept atom id.</param>
    /// <returns>The rendered atom name.</returns>
    public string RenderAtom(int atom)
    {
        return atom switch
        {
            Top => "Top",
            Bottom => "Bottom",
            _ => AtomNames[atom] is Utf8String name ? name.ToString() : $"_a{atom}",
        };
    }

    /// <summary>Renders a directioned role id for the debugging renderer: the interned base name (or a fresh-role placeholder) with an inverse marker.</summary>
    /// <param name="role">The directioned role id.</param>
    /// <returns>The rendered role name.</returns>
    public string RenderRole(int role)
    {
        string baseName = RoleNames[role] is Utf8String name ? name.ToString() : $"_r{Forward(role) / 2}";

        return IsInverse(role) ? $"{baseName}^-" : baseName;
    }

    /// <summary>Renders a raw directioned role for the clash reasons: the interned base name with an inverse marker.</summary>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The rendered role name.</returns>
    public string RenderRole(RawRoleId role)
    {
        return RenderRole(role.Value);
    }
}

/// <summary>
/// The introduction origin of an interned individual, threaded into
/// <see cref="ContextSymbolTable.InternIndividual"/> at every mint site: an
/// IRI-denoted input individual, an anonymous (blank-node) one, or an
/// engine-minted one. The value cannot be recovered from the interning key
/// alone, so each mint site supplies it; the closed shape makes minting
/// without an origin a compile error, the totality the key-join candidacy
/// filter relies on.
/// </summary>
internal enum IndividualOrigin
{
    /// <summary>An input individual denoted by an IRI (a <c>NamedNode</c>) — the sole key-join candidacy origin.</summary>
    IriDenoted = 0,

    /// <summary>An input individual introduced by a blank node — existential in denotation and barred from key-join candidacy.</summary>
    BlankNode = 1,

    /// <summary>An individual carried by an engine-minted node, keyed by its deterministic Skolem IRI — barred from key-join candidacy like a blank-node individual, and distinct in origin from an input IRI spelling the same key, so the key-collision residual marker records that spoof.</summary>
    EngineMinted = 2,
}
