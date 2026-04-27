using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Resolves a shape-identifying <see cref="TermId"/> to a
/// <see cref="Shape"/> instance. Used by <see cref="ShapeRegistry"/> to
/// decouple the registry's public API from any specific backing
/// storage model.
/// </summary>
/// <remarks>
/// <para>
/// The default in-memory implementation binds this delegate to
/// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>
/// on a fully-populated dictionary. A future storage-backed
/// implementation would bind this to a lazy-hydrating lookup against
/// an mmap-packed shape store; evaluator code is unaffected.
/// </para>
/// <para>
/// <b>Nullability contract.</b> The <c>out</c> parameter is annotated
/// <see cref="MaybeNullWhenAttribute"/>(<c>false</c>): when the method
/// returns <c>true</c>, <paramref name="shape"/> is non-null; when it
/// returns <c>false</c>, <paramref name="shape"/> may be null. This
/// matches <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>
/// so a dictionary's method group is directly assignable to this
/// delegate.
/// </para>
/// <para>
/// Implementations must be thread-safe if the containing
/// <see cref="ShapeRegistry"/> is shared across validation threads.
/// The default dictionary-backed implementation is thread-safe for
/// concurrent reads because the underlying dictionary is not mutated
/// after the loader returns.
/// </para>
/// </remarks>
/// <param name="id">The shape's term identifier.</param>
/// <param name="shape">On success, the resolved shape.</param>
/// <returns><c>true</c> if a shape exists for <paramref name="id"/>; <c>false</c> otherwise.</returns>
public delegate bool ShapeLookupDelegate(TermId id, [MaybeNullWhen(false)] out Shape shape);
