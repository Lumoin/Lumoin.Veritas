using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The query-scoped bump arena: slices read and write their own runs, runs that
/// spill across slabs stay independent and valid, disposal returns every rented
/// slab exactly once, and it all holds over the real <see cref="VeritasMemoryPool{T}"/>
/// whose slab rentals carry non-zero offsets. Pins the explicit single lifetime
/// the factorised buffers will share.
/// </summary>
[TestClass]
internal sealed class FactorizedArenaTests
{
    [TestMethod]
    public void SlicesInOneSlabAreIndependentAndReadBack()
    {
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool);

        ArenaSlice a = arena.Allocate(3);
        ArenaSlice b = arena.Allocate(2);

        a.Span[0] = 10;
        a.Span[1] = 11;
        a.Span[2] = 12;
        b.Span[0] = 20;
        b.Span[1] = 21;

        Assert.AreEqual(3, a.Length);
        Assert.AreEqual(2, b.Length);
        Assert.AreEqual(10u, a[0]);
        Assert.AreEqual(11u, a[1]);
        Assert.AreEqual(12u, a[2]);
        Assert.AreEqual(20u, b[0]);
        Assert.AreEqual(21u, b[1]);
    }

    [TestMethod]
    public void RunsThatSpillAcrossSlabsStayValid()
    {
        //A slab of four elements with three-element runs forces a fresh slab per
        //run after the first; every slice must keep reading its own slab.
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool, slabElements: 4);

        List<ArenaSlice> slices = [];
        for(uint i = 0; i < 10; i++)
        {
            ArenaSlice slice = arena.Allocate(3);
            slice.Span[0] = i;
            slice.Span[1] = i + 100;
            slice.Span[2] = i + 200;
            slices.Add(slice);
        }

        for(uint i = 0; i < 10; i++)
        {
            Assert.AreEqual(i, slices[(int)i][0]);
            Assert.AreEqual(i + 100, slices[(int)i][1]);
            Assert.AreEqual(i + 200, slices[(int)i][2]);
        }
    }

    [TestMethod]
    public void ARunLargerThanTheSlabGetsADedicatedSlab()
    {
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool, slabElements: 4);

        ArenaSlice big = arena.Allocate(100);
        for(int i = 0; i < 100; i++)
        {
            big.Span[i] = (uint)(i * 7);
        }

        for(int i = 0; i < 100; i++)
        {
            Assert.AreEqual((uint)(i * 7), big[i]);
        }
    }

    [TestMethod]
    public void DisposeReturnsEverySlabExactlyOnce()
    {
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool, slabElements: 4);

        for(int i = 0; i < 5; i++)
        {
            arena.Allocate(3);
        }

        Assert.IsGreaterThan(0, pool.Outstanding);

        arena.Dispose();
        Assert.AreEqual(0, pool.Outstanding);

        //Idempotent: a second disposal returns nothing further.
        arena.Dispose();
        Assert.AreEqual(0, pool.Outstanding);
    }

    [TestMethod]
    public void AnEmptyAllocationConsumesNoSlab()
    {
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool);

        ArenaSlice empty = arena.Allocate(0);

        Assert.AreEqual(0, empty.Length);
        Assert.AreEqual(0, pool.Outstanding);
    }

    [TestMethod]
    public void AllocatingAfterDisposeThrows()
    {
        using CountingPool pool = new();
        using FactorizedArena arena = new(pool);
        arena.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => arena.Allocate(1));
    }

    [TestMethod]
    public void HoldsOverTheRealMemoryPool()
    {
        //The real pool slices rentals from shared slabs, so a rental's backing
        //array carries a non-zero offset — the arena must honour it.
        using VeritasMemoryPool<uint> pool = new();
        using FactorizedArena arena = new(pool, slabElements: 4);

        List<ArenaSlice> slices = [];
        for(uint i = 0; i < 6; i++)
        {
            ArenaSlice slice = arena.Allocate(3);
            slice.Span[0] = i * 3;
            slice.Span[1] = (i * 3) + 1;
            slice.Span[2] = (i * 3) + 2;
            slices.Add(slice);
        }

        for(uint i = 0; i < 6; i++)
        {
            Assert.AreEqual(i * 3, slices[(int)i][0]);
            Assert.AreEqual((i * 3) + 1, slices[(int)i][1]);
            Assert.AreEqual((i * 3) + 2, slices[(int)i][2]);
        }
    }

    /// <summary>A <see cref="MemoryPool{T}"/> over array-backed rentals that tracks outstanding count, so a test can assert every slab is returned.</summary>
    private sealed class CountingPool: MemoryPool<uint>
    {
        /// <summary>The number of rentals not yet returned.</summary>
        private int outstanding;

        /// <summary>The number of rentals not yet returned.</summary>
        public int Outstanding => outstanding;

        /// <summary>The maximum rentable size — unbounded.</summary>
        public override int MaxBufferSize => int.MaxValue;

        /// <summary>Rents an array-backed buffer of the requested size, counting the rental.</summary>
        /// <param name="minBufferSize">The minimum size.</param>
        /// <returns>The rental.</returns>
        public override IMemoryOwner<uint> Rent(int minBufferSize = -1)
        {
            Interlocked.Increment(ref outstanding);

            return new Owner(this, minBufferSize <= 0 ? 1 : minBufferSize);
        }

        /// <summary>No unmanaged state to release.</summary>
        /// <param name="disposing">Whether managed disposal is in progress.</param>
        protected override void Dispose(bool disposing)
        {
        }

        /// <summary>An array-backed rental that decrements its pool's outstanding count once on disposal.</summary>
        private sealed class Owner: IMemoryOwner<uint>
        {
            /// <summary>The pool to credit on return.</summary>
            private readonly CountingPool pool;

            /// <summary>The backing array.</summary>
            private readonly uint[] buffer;

            /// <summary>Whether this rental has been returned.</summary>
            private bool disposed;

            /// <summary>Constructs an array-backed rental of the given size.</summary>
            /// <param name="pool">The owning pool.</param>
            /// <param name="size">The buffer size.</param>
            public Owner(CountingPool pool, int size)
            {
                this.pool = pool;
                buffer = new uint[size];
            }

            /// <summary>The rented memory, array-backed so the arena can extract the backing array.</summary>
            public Memory<uint> Memory => buffer;

            /// <summary>Returns the rental once.</summary>
            public void Dispose()
            {
                if(!disposed)
                {
                    disposed = true;
                    Interlocked.Decrement(ref pool.outstanding);
                }
            }
        }
    }
}
