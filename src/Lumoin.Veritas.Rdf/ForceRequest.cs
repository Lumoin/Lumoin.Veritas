namespace Lumoin.Veritas.Rdf;

/// <summary>
/// A request yielded by a <see cref="GraphKFold"/> algebra iterator to the
/// fold driver, describing what should happen before the algebra is resumed.
/// </summary>
/// <remarks>
/// <para>
/// Algebras written as iterator methods <c>yield return</c> values of this
/// type to communicate with the driver. The driver inspects the request,
/// takes the appropriate action (forcing a child node, for example), and
/// resumes the algebra on the next
/// <see cref="System.Collections.Generic.IEnumerator{T}.MoveNext"/> call.
/// </para>
/// <para>
/// An algebra ends by writing its final result via
/// <see cref="ChildHandles{TResult}.SetResult"/> and then completing the
/// iterator — either by reaching the end of the method or executing
/// <c>yield break</c>. The driver reads the result once
/// <see cref="System.Collections.Generic.IEnumerator{T}.MoveNext"/> returns
/// <c>false</c>.
/// </para>
/// <para>
/// The type is a <c>readonly record struct</c> so <c>yield return</c> does
/// not allocate. Pattern matching against <see cref="Kind"/> is the
/// expected dispatch mechanism on the driver side.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of action the driver should take before resumption.</param>
/// <param name="ChildIndex">
/// For <see cref="ForceRequestKind.Force"/> requests, the zero-based index
/// of the child to force. Indices correspond to the order of outgoing
/// triples the algebra received. Unused for <see cref="ForceRequestKind.Skip"/>.
/// </param>
public readonly record struct ForceRequest(ForceRequestKind Kind, int ChildIndex)
{
    /// <summary>
    /// Creates a request to force the child at <paramref name="childIndex"/>.
    /// </summary>
    /// <param name="childIndex">Zero-based child index.</param>
    /// <returns>A force request.</returns>
    public static ForceRequest Force(int childIndex) => new(ForceRequestKind.Force, childIndex);

    /// <summary>
    /// Creates a no-op request. The driver resumes the algebra immediately
    /// without any action.
    /// </summary>
    /// <returns>A skip request.</returns>
    public static ForceRequest Skip() => new(ForceRequestKind.Skip, -1);
}
