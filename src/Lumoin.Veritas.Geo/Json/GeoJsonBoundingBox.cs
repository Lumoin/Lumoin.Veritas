using System;

namespace Lumoin.Veritas.Geo.Json;

/// <summary>
/// An RFC 7946 bounding box as a typed value: the planar form carries
/// west, south, east and north, the volumetric form adds the minimum and
/// maximum elevation, and the default instance means the bbox member is
/// absent. West may exceed east — the antimeridian-spanning convention —
/// and no value-order or range check exists: values are domain-free, only
/// the array form is validated where the value is parsed. Equality is
/// bitwise per ordinate, so a negative-zero bound stays distinguishable
/// from its positive twin.
/// </summary>
public readonly struct GeoJsonBoundingBox : IEquatable<GeoJsonBoundingBox>
{
    /// <summary>The stored form of a bounding-box value.</summary>
    private enum BoundingBoxForm : byte
    {
        /// <summary>No bbox member.</summary>
        Absent = 0,

        /// <summary>The four-element form.</summary>
        Planar = 1,

        /// <summary>The six-element form with elevations.</summary>
        Volumetric = 2,
    }

    /// <summary>The stored form discriminant.</summary>
    private BoundingBoxForm Form { get; }

    /// <summary>The westernmost longitude — the first array slot.</summary>
    public double West { get; }

    /// <summary>The southernmost latitude — the second array slot.</summary>
    public double South { get; }

    /// <summary>The easternmost longitude; less than west when the box spans the antimeridian.</summary>
    public double East { get; }

    /// <summary>The northernmost latitude.</summary>
    public double North { get; }

    /// <summary>The minimum elevation; meaningful only when <see cref="HasElevation"/>.</summary>
    public double MinimumElevation { get; }

    /// <summary>The maximum elevation; meaningful only when <see cref="HasElevation"/>.</summary>
    public double MaximumElevation { get; }

    /// <summary>True when a bbox value is present; the default instance is absent.</summary>
    public bool IsPresent => Form != BoundingBoxForm.Absent;

    /// <summary>True for the six-element volumetric form.</summary>
    public bool HasElevation => Form == BoundingBoxForm.Volumetric;

    /// <summary>Creates the planar four-element form in RFC 7946 slot order.</summary>
    /// <param name="west">The westernmost longitude.</param>
    /// <param name="south">The southernmost latitude.</param>
    /// <param name="east">The easternmost longitude.</param>
    /// <param name="north">The northernmost latitude.</param>
    public GeoJsonBoundingBox(double west, double south, double east, double north)
    {
        Form = BoundingBoxForm.Planar;
        West = west;
        South = south;
        East = east;
        North = north;
        MinimumElevation = 0;
        MaximumElevation = 0;
    }

    /// <summary>Creates the volumetric six-element form in RFC 7946 slot order.</summary>
    /// <param name="west">The westernmost longitude — slot one.</param>
    /// <param name="south">The southernmost latitude — slot two.</param>
    /// <param name="minimumElevation">The minimum elevation — slot three.</param>
    /// <param name="east">The easternmost longitude — slot four.</param>
    /// <param name="north">The northernmost latitude — slot five.</param>
    /// <param name="maximumElevation">The maximum elevation — slot six.</param>
    public GeoJsonBoundingBox(double west, double south, double minimumElevation, double east, double north, double maximumElevation)
    {
        Form = BoundingBoxForm.Volumetric;
        West = west;
        South = south;
        East = east;
        North = north;
        MinimumElevation = minimumElevation;
        MaximumElevation = maximumElevation;
    }

    /// <summary>Bitwise value equality: the form and every ordinate's bits.</summary>
    /// <param name="other">The bounding box to compare against.</param>
    /// <returns>True when the two values are bitwise identical.</returns>
    public bool Equals(GeoJsonBoundingBox other)
    {
        return Form == other.Form
            && BitConverter.DoubleToInt64Bits(West) == BitConverter.DoubleToInt64Bits(other.West)
            && BitConverter.DoubleToInt64Bits(South) == BitConverter.DoubleToInt64Bits(other.South)
            && BitConverter.DoubleToInt64Bits(East) == BitConverter.DoubleToInt64Bits(other.East)
            && BitConverter.DoubleToInt64Bits(North) == BitConverter.DoubleToInt64Bits(other.North)
            && BitConverter.DoubleToInt64Bits(MinimumElevation) == BitConverter.DoubleToInt64Bits(other.MinimumElevation)
            && BitConverter.DoubleToInt64Bits(MaximumElevation) == BitConverter.DoubleToInt64Bits(other.MaximumElevation);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is GeoJsonBoundingBox other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Form,
            BitConverter.DoubleToInt64Bits(West),
            BitConverter.DoubleToInt64Bits(South),
            BitConverter.DoubleToInt64Bits(East),
            BitConverter.DoubleToInt64Bits(North),
            BitConverter.DoubleToInt64Bits(MinimumElevation),
            BitConverter.DoubleToInt64Bits(MaximumElevation));
    }

    /// <summary>Bitwise value equality.</summary>
    /// <param name="left">The first bounding box.</param>
    /// <param name="right">The second bounding box.</param>
    /// <returns>True when the two values are bitwise identical.</returns>
    public static bool operator ==(GeoJsonBoundingBox left, GeoJsonBoundingBox right)
    {
        return left.Equals(right);
    }

    /// <summary>Bitwise value inequality.</summary>
    /// <param name="left">The first bounding box.</param>
    /// <param name="right">The second bounding box.</param>
    /// <returns>True when the two values differ in form or in any ordinate's bits.</returns>
    public static bool operator !=(GeoJsonBoundingBox left, GeoJsonBoundingBox right)
    {
        return !left.Equals(right);
    }
}
