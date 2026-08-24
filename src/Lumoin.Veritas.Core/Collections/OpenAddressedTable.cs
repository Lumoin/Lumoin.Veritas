using System;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// An open-addressed hash table keyed on a 64-bit value — the reusable
/// open-addressing primitive the join layer keys on (packed join keys, intern
/// ids, bucket indices), so the probe/grow/hash mechanics live in one place
/// rather than re-hand-rolled at every call site. Keys, values, and a
/// one-byte occupancy control sit in three parallel arrays (structure-of-
/// arrays), linear-probed, so a lookup is a cache-local scan with no per-entry
/// node allocation or indirection.
/// </summary>
/// <remarks>
/// <para>
/// Structurally a Swiss-table without the SIMD control-group scan: the control
/// byte is empty-or-full per slot (no tombstone — the table is insert-and-read,
/// never deletes), and the key value <c>0</c> is ordinary because emptiness
/// rides on the control byte, not on a key sentinel. The hash is a SplitMix64
/// avalanche so linear probing stays cluster-free on packed keys.
/// </para>
/// <para>
/// Instantiated over a value type, the JIT specialises it to a concrete type
/// with non-virtual, inlinable accessors — the hot-path-concrete property the
/// join requires, with none of the boxing a comparer-driven generic map adds.
/// Single-writer then read-only by convention, like the streams it indexes.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The value stored per key.</typeparam>
public sealed class OpenAddressedTable<TValue>
{
    /// <summary>The control byte of an unoccupied slot.</summary>
    private const byte Empty = 0;

    /// <summary>The control byte of an occupied slot.</summary>
    private const byte Full = 1;

    /// <summary>The smallest slot count, a power of two.</summary>
    private const int MinimumCapacity = 16;

    //Parallel slot arrays reassigned together on grow; fields rather than
    //properties because the probe loops index them tightly and grow swaps them.
    private ulong[] keys;

    private TValue[] values;

    private byte[] control;

    private int mask;

    private int count;

    /// <summary>The number of occupied slots — the distinct keys inserted.</summary>
    public int Count => count;

    /// <summary>The slot count; the table's array footprint is this many keys, values, and control bytes.</summary>
    public int Capacity => keys.Length;

    /// <summary>Constructs an empty table sized to at least <paramref name="initialCapacity"/> slots, rounded up to a power of two.</summary>
    /// <param name="initialCapacity">The requested initial slot count; clamped to a minimum and rounded up to a power of two.</param>
    public OpenAddressedTable(int initialCapacity = MinimumCapacity)
    {
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(initialCapacity, MinimumCapacity));
        keys = new ulong[capacity];
        values = new TValue[capacity];
        control = new byte[capacity];
        mask = capacity - 1;
    }

    /// <summary>Reads the value stored for a key.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">Receives the stored value when the key is present.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool TryGetValue(ulong key, out TValue value)
    {
        int slot = (int)(Mix(key) & (ulong)mask);
        while(control[slot] != Empty)
        {
            if(keys[slot] == key)
            {
                value = values[slot];

                return true;
            }

            slot = (slot + 1) & mask;
        }

        value = default!;

        return false;
    }

    /// <summary>
    /// Stores <paramref name="value"/> for <paramref name="key"/>, replacing
    /// any prior value. When the key was already present its prior value is
    /// returned through <paramref name="previous"/> and the result is
    /// <see langword="true"/>; otherwise the key is inserted and the result is
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="previous">Receives the prior value when the key was present.</param>
    /// <returns><see langword="true"/> when the key was already present.</returns>
    public bool Exchange(ulong key, TValue value, out TValue previous)
    {
        //Grow before insert at a 0.75 load factor so probe runs stay short.
        if((count + 1) * 4 >= keys.Length * 3)
        {
            Grow();
        }

        int slot = (int)(Mix(key) & (ulong)mask);
        while(control[slot] != Empty)
        {
            if(keys[slot] == key)
            {
                previous = values[slot];
                values[slot] = value;

                return true;
            }

            slot = (slot + 1) & mask;
        }

        control[slot] = Full;
        keys[slot] = key;
        values[slot] = value;
        count++;
        previous = default!;

        return false;
    }

    /// <summary>An allocation-free forward enumerator over the occupied entries, in slot order — for consumers that built a table and need to walk its contents (e.g. flattening a hash trie). Mutating the table during enumeration is undefined.</summary>
    /// <returns>The entry enumerator.</returns>
    internal Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>A forward cursor over a table's occupied slots, yielding each key with its value.</summary>
    internal struct Enumerator: IEquatable<Enumerator>
    {
        private readonly OpenAddressedTable<TValue> table;

        private int slot;

        /// <summary>Constructs an enumerator positioned before the first slot.</summary>
        /// <param name="table">The table to walk.</param>
        public Enumerator(OpenAddressedTable<TValue> table)
        {
            ArgumentNullException.ThrowIfNull(table);

            this.table = table;
            slot = -1;
        }

        /// <summary>The key and value at the current slot.</summary>
        public (ulong Key, TValue Value) Current => (table.keys[slot], table.values[slot]);

        /// <summary>Advances to the next occupied slot.</summary>
        /// <returns><see langword="true"/> when an occupied slot was found.</returns>
        public bool MoveNext()
        {
            byte[] control = table.control;
            while(++slot < control.Length)
            {
                if(control[slot] != Empty)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether another enumerator walks the same table at the same slot.</summary>
        /// <param name="other">The enumerator to compare.</param>
        /// <returns><see langword="true"/> when both share a table and slot.</returns>
        public readonly bool Equals(Enumerator other)
        {
            return ReferenceEquals(table, other.table) && slot == other.slot;
        }

        /// <summary>Whether the object is an equal enumerator.</summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal enumerator.</returns>
        public readonly override bool Equals(object? obj)
        {
            return obj is Enumerator other && Equals(other);
        }

        /// <summary>A hash over the table identity and slot.</summary>
        /// <returns>The hash code.</returns>
        public readonly override int GetHashCode()
        {
            return HashCode.Combine(table, slot);
        }

        /// <summary>Whether two enumerators are equal.</summary>
        /// <param name="left">The first enumerator.</param>
        /// <param name="right">The second enumerator.</param>
        /// <returns><see langword="true"/> when equal.</returns>
        public static bool operator ==(Enumerator left, Enumerator right)
        {
            return left.Equals(right);
        }

        /// <summary>Whether two enumerators are unequal.</summary>
        /// <param name="left">The first enumerator.</param>
        /// <param name="right">The second enumerator.</param>
        /// <returns><see langword="true"/> when unequal.</returns>
        public static bool operator !=(Enumerator left, Enumerator right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>Doubles the slot arrays and re-probes every occupied entry into them; the keys are already distinct, so each finds a fresh empty slot.</summary>
    private void Grow()
    {
        ulong[] previousKeys = keys;
        TValue[] previousValues = values;
        byte[] previousControl = control;

        int capacity = previousKeys.Length * 2;
        keys = new ulong[capacity];
        values = new TValue[capacity];
        control = new byte[capacity];
        mask = capacity - 1;

        for(int i = 0; i < previousControl.Length; i++)
        {
            if(previousControl[i] == Empty)
            {
                continue;
            }

            int slot = (int)(Mix(previousKeys[i]) & (ulong)mask);
            while(control[slot] != Empty)
            {
                slot = (slot + 1) & mask;
            }

            control[slot] = Full;
            keys[slot] = previousKeys[i];
            values[slot] = previousValues[i];
        }
    }

    /// <summary>A 64-bit avalanche mix (the SplitMix64 finaliser) spreading the key across the slot range so linear probing stays cluster-free.</summary>
    /// <param name="value">The key value.</param>
    /// <returns>The mixed hash.</returns>
    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;

            return value;
        }
    }
}
