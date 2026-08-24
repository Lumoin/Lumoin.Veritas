using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The shared vocabulary of the two-process replicate batteries: launching and seeding replicate-command
/// children, the per-replica identity-directory convention, the machine-parseable line helpers, and the
/// dictionary-stable triple-file writer. One home for both the add-only and the dotted process batteries, so
/// the production-wiring proofs cannot drift on their harness.
/// </summary>
internal static class ReplicateProcessBatteryHelpers
{
    /// <summary>The loopback host every replica listens on and dials.</summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>The fixed byte width of a replica identity axis, which is also the width of the file a replica's identity is persisted in.</summary>
    public const int IdentityByteWidth = 32;

    /// <summary>Declares the row inconclusive when the command-line executable has not been built — the same precondition discipline the workbench process tests use.</summary>
    public static void RequireExecutable()
    {
        if(ReplicateProcess.FindExecutable() is null)
        {
            Assert.Inconclusive("The command-line executable was not found under src/Lumoin.Veritas.Cli/bin; build the solution first.");
        }
    }

    /// <summary>The per-replica identity directory beside a store directory — every replicate process on this one machine needs its own, since replica-identity distinctness is the deployment obligation the host default cannot provide to a battery of colocated replicas.</summary>
    /// <param name="storeDirectory">The replica's store directory.</param>
    /// <returns>The identity directory path.</returns>
    public static string IdentityDirectoryFor(string storeDirectory)
    {
        return storeDirectory + "-identity";
    }

    /// <summary>Seeds one store directory through a short-lived replicate run, then copies it per replica — the lineage seed: every copy shares the seeded dictionary, its epoch, and the seed's causal history; identity never rides the copy.</summary>
    /// <param name="seedDirectory">The directory the seed run persists into.</param>
    /// <param name="seedFile">The seed data file.</param>
    /// <param name="replicaDirectories">The per-replica directories the seeded store is copied to.</param>
    /// <param name="cancellationToken">Bounds the run.</param>
    /// <returns>A task that completes when every copy exists.</returns>
    public static async Task SeedAndCopyAsync(string seedDirectory, string seedFile, IReadOnlyList<string> replicaDirectories, CancellationToken cancellationToken)
    {
        await SeedStoreAsync(seedDirectory, seedFile, cancellationToken).ConfigureAwait(false);
        foreach(string replica in replicaDirectories)
        {
            CopyStoreDirectory(seedDirectory, replica);
        }
    }

    /// <summary>Runs one replicate process that seeds an empty store from a data file, quits, and exits cleanly.</summary>
    /// <param name="storeDirectory">The empty store directory.</param>
    /// <param name="seedFile">The seed data file.</param>
    /// <param name="cancellationToken">Bounds the run.</param>
    /// <returns>A task that completes when the seed run has exited.</returns>
    public static async Task SeedStoreAsync(string storeDirectory, string seedFile, CancellationToken cancellationToken)
    {
        ReplicateProcess seeder = ReplicateProcess.Start("--store", storeDirectory, "--data", seedFile, "--identity-dir", IdentityDirectoryFor(storeDirectory));
        await using(seeder.ConfigureAwait(false))
        {
            _ = await seeder.SendAndWaitAsync("quit", "quit persisted=", cancellationToken).ConfigureAwait(false);
            int exitCode = await seeder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            Assert.AreEqual(0, exitCode, $"The seed run must exit cleanly; stderr: {string.Join(" | ", seeder.ErrorLines())}");
        }
    }

    /// <summary>
    /// Pre-mints one replica's 32 identity bytes into its identity directory and answers the axis as the
    /// 64-character lowercase hex the founder options take. A metadata chain's founder list must be known before
    /// any founder starts — every host mints the same chain identity from it — so a battery writes the identity
    /// files first rather than starting each host once to read the axis line back.
    /// </summary>
    /// <param name="identityDirectory">The replica's identity directory; created when missing.</param>
    /// <returns>The replica's identity axis as lowercase hex.</returns>
    public static string WriteIdentityFile(string identityDirectory)
    {
        Directory.CreateDirectory(identityDirectory);
        RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.Bytes, default, IdentityByteWidth, default));
        byte[] identity = value.Bytes.ToArray();
        File.WriteAllBytes(Path.Combine(identityDirectory, "replica-identity"), identity);

        return Convert.ToHexStringLower(identity);
    }


    /// <summary>
    /// Writes the metadata store's incarnation marker for one host, so a battery can name that host in a
    /// founder list before the host has ever run. It is the store half of a founder, written here for the
    /// reason the identity is: a battery states what the hosts will hold rather than starting each one once to
    /// read what it minted.
    /// </summary>
    /// <param name="identityDirectory">The host's identity directory, beside which its metadata store lives.</param>
    /// <returns>The store incarnation in lower-case hexadecimal.</returns>
    public static string WriteStoreMarkerFile(string identityDirectory)
    {
        string metadataDirectory = Path.Combine(identityDirectory, "metadata");
        Directory.CreateDirectory(metadataDirectory);
        RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.Bytes, default, StoreIncarnation.Size, default));
        byte[] incarnation = value.Bytes.ToArray();
        File.WriteAllBytes(Path.Combine(metadataDirectory, MetadataNodeStore.IncarnationFileName), incarnation);

        return Convert.ToHexStringLower(incarnation);
    }


    /// <summary>One founder argument's value: the axis the host serves under, and the store admitted to answer for it.</summary>
    /// <param name="axisHex">The host's identity axis in hexadecimal.</param>
    /// <param name="storeHex">That host's store incarnation in hexadecimal.</param>
    /// <returns>The token the command parses one founder from.</returns>
    public static string FounderToken(string axisHex, string storeHex)
    {
        return FormattableString.Invariant($"{axisHex}:{storeHex}");
    }


    /// <summary>The axis half of the startup line, which prints the axis and the store the host holds.</summary>
    /// <param name="axisLine">The line's payload, as <see cref="AxisLineAsync"/> returns it.</param>
    /// <returns>The axis in hexadecimal.</returns>
    public static string AxisOf(string axisLine)
    {
        ArgumentNullException.ThrowIfNull(axisLine);

        return axisLine.Split(' ')[0];
    }


    /// <summary>The store half of the startup line.</summary>
    /// <param name="axisLine">The line's payload, as <see cref="AxisLineAsync"/> returns it.</param>
    /// <returns>The store incarnation in hexadecimal.</returns>
    public static string StoreOf(string axisLine)
    {
        ArgumentNullException.ThrowIfNull(axisLine);

        return axisLine.Split(' ')[2];
    }

    /// <summary>The repeated <c>--metadata-founder</c> argument tokens for one founder set, in the order given; the command mints the chain in canonical order, so the listing order is the caller's convenience.</summary>
    /// <param name="founderHexes">The founders' identity axes as hex.</param>
    /// <returns>The argument tokens.</returns>
    public static string[] FounderArguments(params string[] founderHexes)
    {
        ArgumentNullException.ThrowIfNull(founderHexes);

        string[] arguments = new string[founderHexes.Length * 2];
        for(int i = 0; i < founderHexes.Length; i++)
        {
            arguments[i * 2] = "--metadata-founder";
            arguments[(i * 2) + 1] = founderHexes[i];
        }

        return arguments;
    }

    /// <summary>Waits for a replica's full-width axis startup line and parses the axis — the value an operator copies into every host's founder list.</summary>
    /// <param name="replica">The replica.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The replica's identity axis as lowercase hex.</returns>
    public static async Task<string> AxisLineAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        string line = await replica.WaitForAnyLineAsync("axis ", cancellationToken).ConfigureAwait(false);

        return line["axis ".Length..];
    }

    /// <summary>Waits for a replica's metadata-plane composition line — the chain it runs on, the derived quorum, whether its consensus host came back from its store, and where that store is.</summary>
    /// <param name="replica">The replica.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The composition line.</returns>
    public static Task<string> PlaneLineAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        return replica.WaitForAnyLineAsync("plane chain=", cancellationToken);
    }

    /// <summary>Binds one metadata endpoint on a live replica through the <c>metadata-route</c> verb and asserts the acknowledgement, which is how a battery tells a host where a fellow's ephemeral port landed.</summary>
    /// <param name="replica">The replica whose endpoint map is bound.</param>
    /// <param name="memberHex">The member's identity axis as hex.</param>
    /// <param name="port">The member's loopback port.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The acknowledgement line.</returns>
    public static async Task<string> MetadataRouteAsync(ReplicateProcess replica, string memberHex, int port, CancellationToken cancellationToken)
    {
        string line = await replica.SendAndWaitAsync(FormattableString.Invariant($"metadata-route {memberHex}={LoopbackHost}:{port}"), "plane route ", cancellationToken).ConfigureAwait(false);
        Assert.StartsWith("plane route ok ", line, $"The route must bind; the host answered: {line}");

        return line;
    }

    /// <summary>Waits for a replica's resolved-port startup line and parses the port.</summary>
    /// <param name="replica">The replica.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The resolved loopback port.</returns>
    public static async Task<int> ListeningPortAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        string line = await replica.WaitForAnyLineAsync("listening ", cancellationToken).ConfigureAwait(false);

        return int.Parse(line["listening ".Length..], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a replica's committed-set fingerprint — the order-independent 128-bit fold the convergence assertions compare.</summary>
    /// <param name="replica">The replica.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>The fingerprint hex token.</returns>
    public static async Task<string> FingerprintAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        string line = await replica.SendAndWaitAsync("fingerprint", "fingerprint ", cancellationToken).ConfigureAwait(false);
        string[] tokens = line.Split(' ');

        return tokens[1];
    }

    /// <summary>Quits a replica gracefully and asserts the clean exit, so its store handles are released before the battery reads or deletes the directory.</summary>
    /// <param name="replica">The replica.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>A task that completes when the replica has exited.</returns>
    public static async Task QuitAsync(ReplicateProcess replica, CancellationToken cancellationToken)
    {
        _ = await replica.SendAndWaitAsync("quit", "quit persisted=", cancellationToken).ConfigureAwait(false);
        int exitCode = await replica.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, exitCode, $"The replica must exit cleanly; stderr: {string.Join(" | ", replica.ErrorLines())}");
    }

    /// <summary>Writes an N-Triples file of dictionary-stable triples over the fixed term universe: subject and object indexes stay inside the universe, so every later file over the same universe reuses the seed's terms and ingesting it mints nothing.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="universe">The subject and object term-universe size.</param>
    /// <param name="shift">The object-index shift pairing each subject with a different object than the seed's, so shifted files hold distinct triples over the same terms.</param>
    /// <param name="startIndex">The first subject index.</param>
    /// <param name="count">The number of triples.</param>
    /// <returns>The file path, for inline use.</returns>
    public static string WriteTriplesFile(string path, int universe, int shift, int startIndex, int count)
    {
        List<string> lines = new(count);
        for(int i = startIndex; i < startIndex + count; i++)
        {
            int subject = i % universe;
            int obj = (i + shift) % universe;
            lines.Add(FormattableString.Invariant($"<http://example.org/s/{subject}> <http://example.org/p> <http://example.org/o/{obj}> ."));
        }

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>Writes a SPARQL DELETE DATA update file retracting one triple of the fixed term universe — the retraction a dotted process row drives through the <c>update</c> verb.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="universe">The term-universe size the seed was written over.</param>
    /// <param name="shift">The seed's object-index shift.</param>
    /// <param name="index">The seed index of the triple to retract.</param>
    /// <returns>The file path, for inline use.</returns>
    public static string WriteRetractionFile(string path, int universe, int shift, int index)
    {
        int subject = index % universe;
        int obj = (index + shift) % universe;
        File.WriteAllText(path, FormattableString.Invariant($"DELETE DATA {{ <http://example.org/s/{subject}> <http://example.org/p> <http://example.org/o/{obj}> . }}"));

        return path;
    }

    /// <summary>Copies a flat store directory per replica.</summary>
    /// <param name="source">The seeded store directory.</param>
    /// <param name="destination">The replica's directory.</param>
    public static void CopyStoreDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    /// <summary>The latest default-graph system-of-record artifact in a store directory — the persisted segment a damage row garbages a block of.</summary>
    /// <param name="storeDirectory">The store directory.</param>
    /// <returns>The artifact file path.</returns>
    public static string LatestSystemOfRecordFile(string storeDirectory)
    {
        string? latest = null;
        foreach(string file in Directory.EnumerateFiles(storeDirectory, "sor-*.sor"))
        {
            if(latest is null || string.CompareOrdinal(file, latest) > 0)
            {
                latest = file;
            }
        }

        Assert.IsNotNull(latest, "The seeded store must hold a persisted system-of-record artifact.");

        return latest;
    }

    /// <summary>Extracts the value token following <paramref name="key"/> in a machine-parseable output line.</summary>
    /// <param name="line">The output line.</param>
    /// <param name="key">The key text including its equals sign.</param>
    /// <returns>The value token.</returns>
    public static string TokenAfter(string line, string key)
    {
        int at = line.IndexOf(key, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, at, $"The line must carry '{key}': {line}");
        int start = at + key.Length;
        int end = line.IndexOf(' ', start);

        return end < 0 ? line[start..] : line[start..end];
    }
}
