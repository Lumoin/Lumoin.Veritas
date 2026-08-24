using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// The direction a Hilbert curve traverses the triangular tile spanned by corners <c>u</c>, <c>v</c>
/// and <c>w</c>. Each member names the corner the curve starts at followed by the corner it ends
/// at — for example <see cref="WV"/> begins at corner <c>w</c> and ends at corner <c>v</c>.
/// </summary>
internal enum Orientation
{
    /// <summary>The curve starts at corner <c>u</c> and ends at corner <c>v</c>.</summary>
    UV,

    /// <summary>The curve starts at corner <c>v</c> and ends at corner <c>u</c>.</summary>
    VU,

    /// <summary>The curve starts at corner <c>u</c> and ends at corner <c>w</c>.</summary>
    UW,

    /// <summary>The curve starts at corner <c>w</c> and ends at corner <c>u</c>.</summary>
    WU,

    /// <summary>The curve starts at corner <c>v</c> and ends at corner <c>w</c>.</summary>
    VW,

    /// <summary>The curve starts at corner <c>w</c> and ends at corner <c>v</c>.</summary>
    WV,
}
