using System;
using System.Buffers;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The per-message serializers for every reconciliation-envelope kind, discriminated by one leading kind byte
/// (the envelope's single-payload invariant maps one-to-one onto it). The channel framings own their header
/// kind bytes and compose this framing for every envelope leg, so the shard and dotted channels frame their
/// sessions byte-identically. The framing is constructed WITH the bound <see cref="ReconciliationContract"/>,
/// so the fixed-width legs — symbols, fetch items, elements items — are read at exactly the contract's widths
/// and a wrong-width frame is refused at the wire, never absorbed. The remove-aware legs (context, drop,
/// completion) are byte-defined here so a remove-aware binding changes no frame layout; an add-only session
/// already refuses them at dispatch. The element payload is the binding's injected pair of codec delegates; a
/// binding that transfers no elements injects none and an elements frame is refused as malformed. Every read
/// validates counts and lengths against the remaining bytes and refuses unknown kind bytes loudly.
/// </summary>
/// <typeparam name="TElement">The element type the elements messages carry.</typeparam>
internal sealed class ReconciliationEnvelopeFraming<TElement>
{
    /// <summary>The offer leg's kind byte; the envelope kinds start above the channel framings' header pair.</summary>
    private const byte OfferKind = 3;

    /// <summary>The context leg's kind byte.</summary>
    private const byte ContextKind = 4;

    /// <summary>The symbol-batch leg's kind byte.</summary>
    private const byte SymbolsKind = 5;

    /// <summary>The done leg's kind byte.</summary>
    private const byte DoneKind = 6;

    /// <summary>The fetch leg's kind byte.</summary>
    private const byte FetchKind = 7;

    /// <summary>The elements leg's kind byte.</summary>
    private const byte ElementsKind = 8;

    /// <summary>The drop leg's kind byte.</summary>
    private const byte DropKind = 9;

    /// <summary>The completion leg's kind byte, the highest envelope kind.</summary>
    private const byte CompletionKind = 10;

    /// <summary>The offer's fixed key-check width; the offer type pins exactly eight bytes, so the wire carries no length prefix for it.</summary>
    internal const int KeyCheckByteLength = 8;

    /// <summary>The widest item an inbound offer may declare — a structural sanity bound far above any real contract, refusing a hostile width before the session's own contract match runs.</summary>
    internal const int MaximumOfferItemWidth = 4096;

    /// <summary>The widest checksum an inbound offer may declare: the reconciliation symbol's own one-through-eight ceiling.</summary>
    internal const int MaximumOfferChecksumWidth = 8;

    /// <summary>The contract whose widths the fixed-width legs are framed at.</summary>
    private ReconciliationContract Contract { get; }

    /// <summary>Serializes one element, or <see langword="null"/> for a binding that transfers no elements — writing an elements frame then throws.</summary>
    private WriteReconciliationElementDelegate<TElement>? WriteElement { get; }

    /// <summary>Deserializes one element, or <see langword="null"/> for a binding that transfers no elements — an inbound elements frame is then refused as malformed.</summary>
    private ReadReconciliationElementDelegate<TElement>? ReadElement { get; }

    /// <summary>Creates the framing bound to a contract and an optional element codec.</summary>
    /// <param name="contract">The reconciliation contract whose widths the fixed-width legs are framed at.</param>
    /// <param name="writeElement">The element serializer, or <see langword="null"/> for an add-only binding that transfers no elements.</param>
    /// <param name="readElement">The element deserializer, or <see langword="null"/> for an add-only binding that transfers no elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    public ReconciliationEnvelopeFraming(ReconciliationContract contract, WriteReconciliationElementDelegate<TElement>? writeElement = null, ReadReconciliationElementDelegate<TElement>? readElement = null)
    {
        ArgumentNullException.ThrowIfNull(contract);

        Contract = contract;
        WriteElement = writeElement;
        ReadElement = readElement;
    }

    /// <summary>Whether a kind byte names an envelope leg this framing reads — the dispatch test a channel framing runs after its own header kinds.</summary>
    /// <param name="kind">The frame's leading kind byte.</param>
    /// <returns><see langword="true"/> when the kind is an envelope kind.</returns>
    public static bool IsEnvelopeKind(byte kind)
    {
        return kind is >= OfferKind and <= CompletionKind;
    }

    /// <summary>Writes one envelope's kind byte and payload, dispatching on the single payload it carries.</summary>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="NotSupportedException">The envelope carries an elements message and this binding injects no element serializer.</exception>
    /// <exception cref="InvalidOperationException">The envelope carries no payload this framing knows.</exception>
    public void WriteEnvelope(ReconciliationEnvelope<TElement> envelope, IBufferWriter<byte> output)
    {
        if(envelope.Offer is { } offer)
        {
            ReconciliationWireCodec.WriteByte(output, OfferKind);
            ReconciliationWireCodec.WriteByte(output, (byte)offer.ItemDomain);
            ReconciliationWireCodec.WriteInt(output, offer.ItemWidth);
            ReconciliationWireCodec.WriteInt(output, offer.ChecksumWidth);
            output.Write(offer.KeyCheck.Span);

            return;
        }

        if(envelope.Context is { } context)
        {
            ReconciliationWireCodec.WriteByte(output, ContextKind);
            ReconciliationWireCodec.WriteInt(output, context.Clock.Entries.Length);
            foreach(ReplicaCounterEntry entry in context.Clock.Entries)
            {
                ReconciliationWireCodec.WritePrefixedBytes(output, entry.Replica);
                ReconciliationWireCodec.WriteInt(output, entry.Count);
            }

            return;
        }

        if(envelope.Symbols is { } symbols)
        {
            ReconciliationWireCodec.WriteByte(output, SymbolsKind);
            ReconciliationWireCodec.WriteInt(output, symbols.StartIndex);
            ReconciliationWireCodec.WriteInt(output, symbols.Symbols.Length);
            foreach(ReconciliationSymbol symbol in symbols.Symbols)
            {
                output.Write(symbol.Sum.Span);
                output.Write(symbol.Checksum.Span);
            }

            return;
        }

        if(envelope.Done is { } done)
        {
            ReconciliationWireCodec.WriteByte(output, DoneKind);
            ReconciliationWireCodec.WriteInt(output, done.AbsorbedCount);

            return;
        }

        if(envelope.Fetch is { } fetch)
        {
            ReconciliationWireCodec.WriteByte(output, FetchKind);
            ReconciliationWireCodec.WriteInt(output, fetch.Items.Length);
            foreach(ReadOnlyMemory<byte> item in fetch.Items)
            {
                WriteItem(output, item);
            }

            return;
        }

        if(envelope.Elements is { } elements)
        {
            if(WriteElement is null)
            {
                throw new NotSupportedException("This channel's binding transfers no elements; an elements envelope cannot be framed.");
            }

            ReconciliationWireCodec.WriteByte(output, ElementsKind);
            ReconciliationWireCodec.WriteInt(output, elements.Entries.Length);
            foreach(ReconciliationElementEntry<TElement> entry in elements.Entries)
            {
                WriteItem(output, entry.Item);
                WriteElement(entry.Element, output);
            }

            return;
        }

        if(envelope.Drop is { } drop)
        {
            ReconciliationWireCodec.WriteByte(output, DropKind);
            ReconciliationWireCodec.WriteInt(output, drop.Dots.Length);
            foreach(DotState dot in drop.Dots)
            {
                ReconciliationWireCodec.WritePrefixedBytes(output, dot.Replica);
                ReconciliationWireCodec.WriteInt(output, dot.Counter);
            }

            return;
        }

        if(envelope.Completion is { } completion)
        {
            ReconciliationWireCodec.WriteByte(output, CompletionKind);
            ReconciliationWireCodec.WriteInt(output, completion.TransferCount);

            return;
        }

        throw new InvalidOperationException("A reconciliation envelope must carry exactly one payload.");
    }

    /// <summary>Reads one envelope from its payload bytes, dispatched by the kind byte the channel framing already consumed; every read value owns its content, so the frame buffer may be released after the call.</summary>
    /// <param name="kind">The frame's leading kind byte, an envelope kind.</param>
    /// <param name="reader">The frame cursor, positioned past the kind byte.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, carries an unknown kind byte, declares an out-of-range count or width, or carries an elements message this binding injects no element reader for.</exception>
    public ReconciliationEnvelope<TElement> ReadEnvelope(byte kind, ref SequenceReader<byte> reader)
    {
        switch(kind)
        {
            case OfferKind:
            {
                byte domain = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
                if(domain is not ((byte)ReconciliationItemDomain.ContentHash or (byte)ReconciliationItemDomain.Structural))
                {
                    throw new InvalidDataException($"A reconciliation offer carried an unknown item-domain byte {domain}.");
                }

                int itemWidth = ReconciliationWireCodec.ReadInt(ref reader);
                int checksumWidth = ReconciliationWireCodec.ReadInt(ref reader);
                if(itemWidth is < 1 or > MaximumOfferItemWidth || checksumWidth is < 1 or > MaximumOfferChecksumWidth)
                {
                    throw new InvalidDataException("A reconciliation offer declared out-of-range widths.");
                }

                Span<byte> keyCheck = stackalloc byte[KeyCheckByteLength];
                ReconciliationWireCodec.ReadExactly(ref reader, keyCheck);

                return ReconciliationEnvelope<TElement>.ForOffer(new ReconciliationOffer((ReconciliationItemDomain)domain, itemWidth, checksumWidth, keyCheck.ToArray()));
            }

            case ContextKind:
            {
                int count = ReconciliationWireCodec.ReadInt(ref reader);
                ReconciliationWireCodec.EnsureCountFits(count, minimumItemBytes: sizeof(int) + sizeof(int), reader.Remaining);
                ImmutableArray<ReplicaCounterEntry>.Builder entries = ImmutableArray.CreateBuilder<ReplicaCounterEntry>(count);
                for(int i = 0; i < count; i++)
                {
                    ImmutableArray<byte> replica = ReconciliationWireCodec.ReadPrefixedBytes(ref reader);
                    int counter = ReconciliationWireCodec.ReadInt(ref reader);
                    if(counter < 0)
                    {
                        throw new InvalidDataException("A reconciliation context carried a negative replica counter.");
                    }

                    entries.Add(new ReplicaCounterEntry(replica, counter));
                }

                return ReconciliationEnvelope<TElement>.ForContext(new ReconciliationContext(new VectorClockState(entries.MoveToImmutable())));
            }

            case SymbolsKind:
            {
                int startIndex = ReconciliationWireCodec.ReadInt(ref reader);
                int count = ReconciliationWireCodec.ReadInt(ref reader);
                int symbolWidth = Contract.ItemWidth + Contract.ChecksumWidth;
                ReconciliationWireCodec.EnsureCountFits(count, symbolWidth, reader.Remaining);
                ImmutableArray<ReconciliationSymbol>.Builder symbols = ImmutableArray.CreateBuilder<ReconciliationSymbol>(count);
                Span<byte> sum = stackalloc byte[Contract.ItemWidth];
                Span<byte> checksum = stackalloc byte[Contract.ChecksumWidth];
                for(int i = 0; i < count; i++)
                {
                    ReconciliationWireCodec.ReadExactly(ref reader, sum);
                    ReconciliationWireCodec.ReadExactly(ref reader, checksum);
                    symbols.Add(new ReconciliationSymbol(sum, checksum));
                }

                return ReconciliationEnvelope<TElement>.ForSymbols(new ReconciliationSymbolBatch(startIndex, symbols.MoveToImmutable()));
            }

            case DoneKind:
            {
                return ReconciliationEnvelope<TElement>.ForDone(new ReconciliationDone(ReconciliationWireCodec.ReadInt(ref reader)));
            }

            case FetchKind:
            {
                int count = ReconciliationWireCodec.ReadInt(ref reader);
                ReconciliationWireCodec.EnsureCountFits(count, Contract.ItemWidth, reader.Remaining);
                ImmutableArray<ReadOnlyMemory<byte>>.Builder items = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(count);
                for(int i = 0; i < count; i++)
                {
                    items.Add(ReadItem(ref reader));
                }

                return ReconciliationEnvelope<TElement>.ForFetch(new ReconciliationFetch(items.MoveToImmutable()));
            }

            case ElementsKind:
            {
                if(ReadElement is null)
                {
                    throw new InvalidDataException("An elements frame arrived on a channel whose binding transfers no elements.");
                }

                int count = ReconciliationWireCodec.ReadInt(ref reader);
                ReconciliationWireCodec.EnsureCountFits(count, Contract.ItemWidth, reader.Remaining);
                ImmutableArray<ReconciliationElementEntry<TElement>>.Builder entries = ImmutableArray.CreateBuilder<ReconciliationElementEntry<TElement>>(count);
                for(int i = 0; i < count; i++)
                {
                    ReadOnlyMemory<byte> item = ReadItem(ref reader);
                    TElement element = ReadElement(ref reader);
                    entries.Add(new ReconciliationElementEntry<TElement>(item, element));
                }

                return ReconciliationEnvelope<TElement>.ForElements(new ReconciliationElements<TElement>(entries.MoveToImmutable()));
            }

            case DropKind:
            {
                int count = ReconciliationWireCodec.ReadInt(ref reader);
                ReconciliationWireCodec.EnsureCountFits(count, minimumItemBytes: sizeof(int) + sizeof(int), reader.Remaining);
                ImmutableArray<DotState>.Builder dots = ImmutableArray.CreateBuilder<DotState>(count);
                for(int i = 0; i < count; i++)
                {
                    ImmutableArray<byte> replica = ReconciliationWireCodec.ReadPrefixedBytes(ref reader);
                    int counter = ReconciliationWireCodec.ReadInt(ref reader);
                    if(counter < 1)
                    {
                        throw new InvalidDataException("A reconciliation drop carried a non-positive dot counter.");
                    }

                    dots.Add(new DotState(replica, counter));
                }

                return ReconciliationEnvelope<TElement>.ForDrop(new ReconciliationDrop(dots.MoveToImmutable()));
            }

            case CompletionKind:
            {
                return ReconciliationEnvelope<TElement>.ForCompletion(new ReconciliationCompletion(ReconciliationWireCodec.ReadInt(ref reader)));
            }

            default:
            {
                throw new InvalidDataException($"A reconciliation channel frame carried an unknown kind byte {kind}.");
            }
        }
    }

    /// <summary>Writes one fixed-width reconciliation item, refusing a wrong-width item before any bytes cross.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="item">The item; exactly the contract's item width.</param>
    /// <exception cref="InvalidOperationException">The item's width is not the contract's item width.</exception>
    private void WriteItem(IBufferWriter<byte> output, ReadOnlyMemory<byte> item)
    {
        if(item.Length != Contract.ItemWidth)
        {
            throw new InvalidOperationException($"A reconciliation item must be exactly {Contract.ItemWidth} bytes; got {item.Length}.");
        }

        output.Write(item.Span);
    }

    /// <summary>Reads one fixed-width reconciliation item into an owned array.</summary>
    /// <param name="reader">The frame cursor, advanced past the item.</param>
    /// <returns>The item, owning its bytes.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated.</exception>
    private ReadOnlyMemory<byte> ReadItem(ref SequenceReader<byte> reader)
    {
        byte[] item = new byte[Contract.ItemWidth];
        ReconciliationWireCodec.ReadExactly(ref reader, item);

        return item;
    }
}
