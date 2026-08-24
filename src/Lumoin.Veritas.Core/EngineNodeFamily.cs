namespace Lumoin.Veritas.Core;

/// <summary>
/// The mint-family discriminator of an <see cref="EngineNode"/>: which engine seam minted the node and how its
/// key components are to be read. The set is open — an engine claims a code and declares a named accessor for it
/// beside its mint sites — but codes are unique across the codebase, because two families sharing a code would
/// conflate their node spaces through <see cref="EngineNode"/> equality.
/// </summary>
/// <param name="Code">The family's discriminator code.</param>
public readonly record struct EngineNodeFamily(byte Code)
{
    /// <summary>Creates the family value for a discriminator code — the rehydration seam a persistence reader uses; a mint site prefers its family's named accessor.</summary>
    /// <param name="code">The family's discriminator code.</param>
    /// <returns>The family value.</returns>
    public static EngineNodeFamily Create(byte code)
    {
        return new EngineNodeFamily(code);
    }
}
