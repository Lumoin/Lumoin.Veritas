using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The named reason a dotted-difference serve declines an exchange, carried on the reply header so the
/// requesting operator sees a name rather than inferring a cause from a closed connection. An extensible value
/// set: the named statics are the reasons this build mints, and <see cref="Create"/> maps any wire code to its
/// named value or to an <see cref="IsUnknown"/> carrier — an unrecognized future code parses to a typed value
/// and fails closed gracefully, never throws. The default value is <see cref="None"/>, the absent reason an
/// ACCEPTED reply carries; a real decline reason is never the default, so absence and refusal cannot be
/// confused (the distinctness pin).
/// </summary>
public readonly record struct DottedDifferenceDeclineReason
{
    /// <summary>The wire code of the highest reason this build names; codes above it parse as unknown.</summary>
    private const byte HighestNamedCode = 5;

    /// <summary>The reason's wire code.</summary>
    public byte Code { get; }

    /// <summary>Creates a reason over its wire code — the registry entrance <see cref="Create"/> and the named statics use.</summary>
    /// <param name="code">The wire code.</param>
    private DottedDifferenceDeclineReason(byte code)
    {
        Code = code;
    }

    /// <summary>The absent reason an accepted reply carries; never a real decline.</summary>
    public static DottedDifferenceDeclineReason None { get; } = new(0);

    /// <summary>The serving store is not remove-aware: it carries no host replica identity, or it awaits the explicit baseline step. The add-only lanes still serve.</summary>
    public static DottedDifferenceDeclineReason NotRemoveAware { get; } = new(1);

    /// <summary>The declared dictionary epoch differs from the serving store's: the dotted elements carry encoded term identifiers, so a cross-epoch exchange would mis-relate terms.</summary>
    public static DottedDifferenceDeclineReason EpochMismatch { get; } = new(2);

    /// <summary>The declared dotted contract differs from the serving endpoint's: the coded streams would not combine.</summary>
    public static DottedDifferenceDeclineReason ContractMismatch { get; } = new(3);

    /// <summary>The declared symbol cap is not positive: the exchange would have no bound to wind down under.</summary>
    public static DottedDifferenceDeclineReason SymbolCapInvalid { get; } = new(4);

    /// <summary>The serving store is remove-aware but keeps no durable dataset journal, so a crash could lose minted dots peers already cover and a reopen would re-mint them — the dotted wire exchanges only crash-durable causal history.</summary>
    public static DottedDifferenceDeclineReason NotDurable { get; } = new(5);

    /// <summary>Whether this reason's code is one a future build minted and this build does not name; the exchange still fails closed on it.</summary>
    public bool IsUnknown
    {
        get
        {
            return Code > HighestNamedCode;
        }
    }

    /// <summary>Maps a wire code to its reason value: a named reason for the codes this build mints, and the typed unknown carrier for any other — lenient by construction, never a throw.</summary>
    /// <param name="code">The wire code.</param>
    /// <returns>The reason.</returns>
    public static DottedDifferenceDeclineReason Create(byte code)
    {
        return new DottedDifferenceDeclineReason(code);
    }

    /// <summary>The reason's display name — the named reasons by name, an unknown code as <c>Unknown(code)</c>.</summary>
    /// <returns>The display name.</returns>
    public override string ToString()
    {
        return Code switch
        {
            0 => nameof(None),
            1 => nameof(NotRemoveAware),
            2 => nameof(EpochMismatch),
            3 => nameof(ContractMismatch),
            4 => nameof(SymbolCapInvalid),
            5 => nameof(NotDurable),
            _ => FormattableString.Invariant($"Unknown({Code})"),
        };
    }
}
