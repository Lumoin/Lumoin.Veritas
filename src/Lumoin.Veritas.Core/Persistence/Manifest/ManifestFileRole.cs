using System;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// The role a file plays in a persisted manifest generation — which artifact kind it holds. Modelled
/// as a first-class, consumer-extensible value with a stable on-disk code so a new artifact kind is a
/// new row in the manifest, not a format revision: a reader skips a role it does not recognise rather
/// than failing the whole manifest. Built-ins are exposed as named static instances; a deployment adds
/// its own with <see cref="Create"/>.
/// </summary>
/// <remarks>
/// <para>
/// The code is a wire contract — once a manifest is written naming a role by its code, that code must
/// keep meaning the same role. Code 0 is reserved and is not a valid role.
/// </para>
/// </remarks>
public readonly struct ManifestFileRole : IEquatable<ManifestFileRole>
{
    /// <summary>The stable on-disk role code; 0 is reserved and is not a valid role.</summary>
    public int Code { get; }

    /// <summary>A short human-readable name.</summary>
    public string Name { get; }

    /// <summary>Creates a role with a non-zero code and a name.</summary>
    /// <param name="code">The stable on-disk role code (non-zero).</param>
    /// <param name="name">A short human-readable name.</param>
    private ManifestFileRole(int code, string name)
    {
        if(code == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "Manifest file-role code 0 is reserved and is not a valid role.");
        }

        ArgumentException.ThrowIfNullOrEmpty(name);

        Code = code;
        Name = name;
    }

    /// <summary>The durable system-of-record tier: an immutable native segment holding the canonical items.</summary>
    public static ManifestFileRole DataSegment { get; } = new(1, "DataSegment");

    /// <summary>A re-derivable execution structure (the columnar/succinct index); damage is repaired by rebuild, never in place.</summary>
    public static ManifestFileRole Sidecar { get; } = new(2, "Sidecar");

    /// <summary>The persisted integrity sketch — the coded-symbol stream over the system-of-record's item hashes.</summary>
    public static ManifestFileRole Sketch { get; } = new(3, "Sketch");

    /// <summary>An optional locally-stored coded-symbol parity prefix for offline or edge repair.</summary>
    public static ManifestFileRole Parity { get; } = new(4, "Parity");

    /// <summary>The cached statistics copy.</summary>
    public static ManifestFileRole Stats { get; } = new(5, "Stats");

    /// <summary>The persisted term dictionary: the identifier-to-term mapping. It is the source of term identity, so unlike the sidecar it is not re-derivable and is protected as system-of-record-class data.</summary>
    public static ManifestFileRole Dictionary { get; } = new(6, "Dictionary");

    /// <summary>A named graph's durable system-of-record segment, peer of <see cref="DataSegment"/> (the default graph) — an immutable native segment holding one named graph's canonical items, tagged with its graph-name term id by the artifact name. System-of-record-class, not re-derivable.</summary>
    public static ManifestFileRole NamedGraphSegment { get; } = new(7, "NamedGraphSegment");

    /// <summary>The durable loss record a repair publish co-versions with a healed generation whose repair named unrecoverable losses: a self-checksummed manifest-adjacent artifact naming each named loss (its kind, artifact role, artifact name, and item range) so a generation healed with losses stays visibly lossy across a restart rather than looking pristine. Attested by the manifest's whole-image digest like any named artifact but not otherwise re-derived or block-verified; a reader that does not recognise the role skips it.</summary>
    public static ManifestFileRole Losses { get; } = new(8, "Losses");

    /// <summary>Creates a custom manifest file role; the deployment is responsible for a globally-unique code.</summary>
    /// <param name="code">The stable on-disk role code (non-zero).</param>
    /// <param name="name">A short human-readable name.</param>
    /// <returns>The role.</returns>
    public static ManifestFileRole Create(int code, string name)
    {
        return new ManifestFileRole(code, name);
    }

    /// <summary>Determines whether this role has the same <see cref="Code"/> as another.</summary>
    /// <param name="other">The other role.</param>
    /// <returns><see langword="true"/> when the codes match.</returns>
    public bool Equals(ManifestFileRole other)
    {
        return Code == other.Code;
    }

    /// <summary>Determines whether this role equals another object.</summary>
    /// <param name="obj">The other object.</param>
    /// <returns><see langword="true"/> when it is a role with the same code.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ManifestFileRole other && Equals(other);
    }

    /// <summary>Gets a hash code derived from the role code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return Code;
    }

    /// <summary>Determines whether two roles have the same code.</summary>
    /// <param name="left">The left role.</param>
    /// <param name="right">The right role.</param>
    /// <returns><see langword="true"/> when the codes match.</returns>
    public static bool operator ==(ManifestFileRole left, ManifestFileRole right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two roles have different codes.</summary>
    /// <param name="left">The left role.</param>
    /// <param name="right">The right role.</param>
    /// <returns><see langword="true"/> when the codes differ.</returns>
    public static bool operator !=(ManifestFileRole left, ManifestFileRole right)
    {
        return !left.Equals(right);
    }
}
