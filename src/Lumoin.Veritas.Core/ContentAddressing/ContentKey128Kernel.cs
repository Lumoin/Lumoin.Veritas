using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Core.ContentAddressing;

/// <summary>
/// The compare and XOR kernel for <see cref="ContentKey128"/>. A 128-bit
/// key is one <see cref="Vector128{T}"/> register, so equality and XOR are
/// each a single hardware op where the machine supports it; where it does
/// not, the same result falls out of word-level parallelism over the two
/// 64-bit words in two steps, which a superscalar core issues nearly as
/// fast. Both paths are exposed so the differential oracle proves they
/// agree, per the keep-measured-alternatives discipline.
/// </summary>
/// <remarks>
/// <para>
/// The public entry points pick the hardware path when
/// <see cref="Vector128.IsHardwareAccelerated"/> and otherwise the
/// portable path; the explicit per-path methods exist for measurement and
/// for the agreement test on hardware that supports the wide path.
/// </para>
/// </remarks>
public static class ContentKey128Kernel
{
    /// <summary>Combines two keys by XOR — the reconciliation operation under which matched items cancel.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>The XOR of the two keys.</returns>
    public static ContentKey128 Xor(ContentKey128 left, ContentKey128 right)
    {
        return Vector128.IsHardwareAccelerated ? XorVector128(left, right) : XorPortable(left, right);
    }

    /// <summary>Tests two keys for equality — the content-addressed dedup and lookup operation.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><c>true</c> when the keys are bitwise identical.</returns>
    public static bool AreEqual(ContentKey128 left, ContentKey128 right)
    {
        return Vector128.IsHardwareAccelerated ? AreEqualVector128(left, right) : AreEqualPortable(left, right);
    }

    /// <summary>The single-register XOR: load each key as a 128-bit vector and XOR once.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>The XOR of the two keys.</returns>
    internal static ContentKey128 XorVector128(ContentKey128 left, ContentKey128 right)
    {
        Vector128<ulong> result = Vector128.Create(left.Low, left.High) ^ Vector128.Create(right.Low, right.High);

        return new ContentKey128(result[0], result[1]);
    }

    /// <summary>The two-step word-parallel XOR: XOR each 64-bit word.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>The XOR of the two keys.</returns>
    internal static ContentKey128 XorPortable(ContentKey128 left, ContentKey128 right)
    {
        return new ContentKey128(left.Low ^ right.Low, left.High ^ right.High);
    }

    /// <summary>The single-register equality: compare both keys as 128-bit vectors at once.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><c>true</c> when the keys are bitwise identical.</returns>
    internal static bool AreEqualVector128(ContentKey128 left, ContentKey128 right)
    {
        return Vector128.Create(left.Low, left.High) == Vector128.Create(right.Low, right.High);
    }

    /// <summary>The two-step word-parallel equality: the words differ iff the OR of their XORs is non-zero.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><c>true</c> when the keys are bitwise identical.</returns>
    internal static bool AreEqualPortable(ContentKey128 left, ContentKey128 right)
    {
        return ((left.Low ^ right.Low) | (left.High ^ right.High)) == 0UL;
    }
}
