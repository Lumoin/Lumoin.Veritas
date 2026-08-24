using System;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The set of supplied-value symbols a signature parameter accepts, one bit per JSONata value kind plus the
/// <see cref="Missing"/> bit for the undefined value. A parameter's accepted-symbol set is the union of the
/// flags its type letter contributes; a supplied value matches the parameter when its single
/// <see cref="JsonataSignatureValidator"/> symbol bit is present in that set.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[Flags]
internal enum SignatureType : byte
{
    /// <summary>The empty accepted-symbol set.</summary>
    None = 0,

    /// <summary>The string symbol (the <c>s</c> type letter).</summary>
    String = 1,

    /// <summary>The number symbol (the <c>n</c> type letter).</summary>
    Number = 2,

    /// <summary>The boolean symbol (the <c>b</c> type letter).</summary>
    Boolean = 4,

    /// <summary>The null symbol (the <c>l</c> type letter).</summary>
    Null = 8,

    /// <summary>The array symbol (the <c>a</c> type letter).</summary>
    Array = 16,

    /// <summary>The object symbol (the <c>o</c> type letter).</summary>
    Object = 32,

    /// <summary>The function symbol (the <c>f</c> type letter).</summary>
    Function = 64,

    /// <summary>The missing/undefined symbol (the <c>m</c> type letter); every non-function, non-<c>j</c> type letter also admits it.</summary>
    Missing = 128
}
