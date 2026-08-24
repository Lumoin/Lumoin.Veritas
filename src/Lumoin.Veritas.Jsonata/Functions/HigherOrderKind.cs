namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// Which higher-order JSONata function a <see cref="JsonataHigherOrderFunction"/> value applies. A higher-order
/// function applies a user-supplied function once per element (or once per comparison, for <c>$sort</c>), so it
/// cannot run as a synchronous built-in delegate; the evaluator dispatches on this kind to drive its resident
/// cursor instead. The array kinds (<see cref="Map"/>/<see cref="Filter"/>/<see cref="Single"/>/
/// <see cref="Reduce"/>) iterate an array's elements; the object kinds (<see cref="Sift"/>/<see cref="Each"/>)
/// iterate an object's entries; <see cref="Sort"/> orders an array.
/// </summary>
internal enum HigherOrderKind
{
    /// <summary><c>$map(array, fn)</c>: apply <c>fn(value, index, array)</c> per element and collect the non-undefined results.</summary>
    Map,

    /// <summary><c>$filter(array, fn)</c>: apply the predicate per element and keep the elements whose predicate result is truthy.</summary>
    Filter,

    /// <summary><c>$single(array[, fn])</c>: return the single element whose predicate result is truthy, asserting exactly one match.</summary>
    Single,

    /// <summary><c>$reduce(array, fn[, init])</c>: left-fold <c>fn(accumulator, value, index, array)</c> across the elements.</summary>
    Reduce,

    /// <summary><c>$sort(array[, comparator])</c>: order the array's elements, by the native ascending order or by a supplied comparator.</summary>
    Sort,

    /// <summary><c>$sift(object, predicate)</c>: apply <c>predicate(value, key, object)</c> per entry and keep the (key, value) pairs whose result is truthy.</summary>
    Sift,

    /// <summary><c>$each(object, fn)</c>: apply <c>fn(value, key, object)</c> per entry and collect the non-undefined results.</summary>
    Each
}
