using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// A loaded SHACL shape graph as a navigable registry. Returned by
/// <see cref="ShapeLoader"/> and consumed by the validator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delegate-based lookup.</b> The registry does not own the storage
/// model for its shapes. A <see cref="ShapeLookupDelegate"/> supplied
/// at construction provides the <see cref="TryGetShape"/> behavior;
/// <see cref="AllShapes"/> supplies the enumeration. The default
/// factory <see cref="FromDictionary"/> wires both to a populated
/// in-memory <see cref="Dictionary{TKey, TValue}"/>. Future
/// implementations — lazy-hydration against an mmap-packed store,
/// evictable cache fronting a durable database, remote-fetch stubs
/// for tests — plug in by supplying different delegates; no evaluator
/// code changes.
/// </para>
/// <para>
/// <b>Why a named type rather than <see cref="IReadOnlyDictionary{TKey, TValue}"/>.</b>
/// A registry is not a dictionary. It carries semantic helpers
/// (<see cref="NodeShapes"/>, <see cref="PropertyShapes"/>) that would
/// have no home on a bare dictionary interface, and the "lookup by
/// term id" operation is the one and only access pattern — no
/// indexer, no enumerator-of-pairs, no collection-initializer
/// semantics. Giving it a named type documents intent and leaves room
/// to grow the API if real needs appear.
/// </para>
/// <para>
/// <b>Immutability.</b> <see cref="ShapeRegistry"/> is constructed
/// once by the loader and handed to callers. The current design does
/// not provide mutation operations; future incremental-loading
/// scenarios (merge, evict) would introduce a derived type or a
/// dedicated mutable registry, keeping the post-load base type
/// immutable by contract.
/// </para>
/// </remarks>
public sealed class ShapeRegistry
{
    /// <summary>
    /// Initializes a registry with a lookup strategy and an enumeration
    /// of all known shapes.
    /// </summary>
    /// <param name="lookup">
    /// The lookup delegate. May encapsulate in-memory dictionary access,
    /// lazy storage hydration, or any other resolution strategy.
    /// </param>
    /// <param name="allShapes">
    /// Enumeration of every shape in the registry. Must agree with
    /// <paramref name="lookup"/>: every shape yielded here should be
    /// resolvable through <paramref name="lookup"/> by its
    /// <see cref="Shape.Id"/>.
    /// </param>
    public ShapeRegistry(ShapeLookupDelegate lookup, IEnumerable<Shape> allShapes)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(allShapes);
        Lookup = lookup;
        AllShapes = allShapes;
    }

    /// <summary>
    /// Enumeration of every shape known to this registry. Order is
    /// not specified and may differ across implementations.
    /// </summary>
    public IEnumerable<Shape> AllShapes { get; }

    private ShapeLookupDelegate Lookup { get; }

    /// <summary>
    /// Resolves a shape-identifying <see cref="TermId"/> to a
    /// <see cref="Shape"/>.
    /// </summary>
    /// <param name="id">The shape's term identifier.</param>
    /// <param name="shape">On success, the resolved shape.</param>
    /// <returns><c>true</c> if a shape exists for <paramref name="id"/>; <c>false</c> otherwise.</returns>
    public bool TryGetShape(TermId id, [MaybeNullWhen(false)] out Shape shape)
        => Lookup(id, out shape);

    /// <summary>
    /// Every <see cref="NodeShape"/> in this registry. Produced by
    /// filtering <see cref="AllShapes"/>.
    /// </summary>
    public IEnumerable<NodeShape> NodeShapes
    {
        get
        {
            foreach(Shape shape in AllShapes)
            {
                if(shape is NodeShape node)
                {
                    yield return node;
                }
            }
        }
    }

    /// <summary>
    /// Every <see cref="PropertyShape"/> in this registry. Produced by
    /// filtering <see cref="AllShapes"/>.
    /// </summary>
    public IEnumerable<PropertyShape> PropertyShapes
    {
        get
        {
            foreach(Shape shape in AllShapes)
            {
                if(shape is PropertyShape prop)
                {
                    yield return prop;
                }
            }
        }
    }

    /// <summary>
    /// Builds a registry backed by a fully-populated in-memory
    /// dictionary. The default construction path used by
    /// <see cref="ShapeLoader"/>.
    /// </summary>
    /// <param name="shapes">
    /// Dictionary mapping each shape's <see cref="Shape.Id"/> to the
    /// shape record. The registry does not clone the dictionary;
    /// callers must not mutate it after handoff.
    /// </param>
    /// <returns>A registry whose lookup binds directly to the dictionary.</returns>
    public static ShapeRegistry FromDictionary(Dictionary<TermId, Shape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        return new ShapeRegistry(shapes.TryGetValue, shapes.Values);
    }
}
