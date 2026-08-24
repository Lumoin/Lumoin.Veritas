namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The outcome of a <see cref="GraphHylo.HyloAsync{TSeed, TResult}"/> call.
/// </summary>
/// <remarks>
/// <para>
/// A hylomorphism can legitimately produce any value of type <typeparamref name="TResult"/>,
/// including the default — an <c>int</c> algebra that returns <c>0</c>, or a
/// reference-type algebra that returns <c>null</c>. This struct disambiguates
/// "no result was produced because the seed was empty" from "the algebra was
/// applied and its result happens to equal <c>default(TResult)</c>".
/// </para>
/// </remarks>
/// <typeparam name="TResult">The type produced by the hylomorphism.</typeparam>
/// <param name="HasResult">
/// <c>true</c> if the algebra was applied at least once; <c>false</c> if the
/// initial seed produced no triple and no further seeds.
/// </param>
/// <param name="Result">
/// The fold result when <paramref name="HasResult"/> is <c>true</c>; otherwise
/// <c>default(TResult)</c>.
/// </param>
public readonly record struct HyloOutcome<TResult>(bool HasResult, TResult Result)
{
    /// <summary>
    /// Gets an outcome signalling that no result was produced.
    /// </summary>
    public static HyloOutcome<TResult> Empty { get; } = new(false, default!);
}
