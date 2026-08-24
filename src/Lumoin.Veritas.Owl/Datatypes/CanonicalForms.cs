using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The shared canonical forms the data-range canonicalizer rewrites to, kept as
/// single instances so a canonical form is recognisable by reference identity.
/// </summary>
internal static class CanonicalForms
{
    /// <summary>
    /// The canonical bottom data range: the single shared empty enumeration an
    /// empty value space rewrites to. Reasoning consumers read it as bottom
    /// (an empty <see cref="OwlDataOneOf"/> is unsatisfiable, has no member, and
    /// holds no distinct value); it is reasoning-internal and never serialised.
    /// </summary>
    public static OwlDataOneOf EmptyRange { get; } = new([]);
}
