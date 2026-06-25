// Web-storage persistence for the in-browser deployment, over the Origin Private File System (OPFS).
// The in-browser engine is memory-resident — it builds its store in RAM per session — so this persists
// the dataset SOURCE (the bytes the engine rehydrates from on boot), giving warm start without a server.
// It is NOT the engine's columnar durable tier (that is the engine's own designed durable story); when
// that lands, its OPFS-backed ColumnSource supersedes this. Sync access handles (fastest) need a Worker;
// this main-thread surface uses the portable createWritable/getFile API.

/** A named-blob store for dataset sources, keyed by a simple name. */
export interface DatasetStore {
  /** Writes (replacing) the bytes under a name. */
  save(name: string, bytes: Uint8Array): Promise<void>;

  /** Reads the bytes under a name, or null when absent. */
  load(name: string): Promise<Uint8Array | null>;

  /** Removes the entry under a name, if present. */
  remove(name: string): Promise<void>;
}

/** The async-key surface of a directory handle (not yet in every DOM lib), kept minimal and typed. */
interface DirectoryKeys {
  keys(): AsyncIterableIterator<string>;
}

/** An OPFS-backed {@link DatasetStore} under a single sub-directory of the origin's private root. */
export class OpfsDatasetStore implements DatasetStore {
  /** @param directory The sub-directory of the OPFS root the datasets live under. */
  constructor(private readonly directory: string = 'veritas-studio') {}

  /** Opens (creating) the store's directory under the OPFS root. */
  private async open(): Promise<FileSystemDirectoryHandle> {
    const root = await navigator.storage.getDirectory();

    return root.getDirectoryHandle(this.directory, { create: true });
  }

  /** @inheritdoc */
  async save(name: string, bytes: Uint8Array): Promise<void> {
    const directory = await this.open();
    const handle = await directory.getFileHandle(name, { create: true });
    const writable = await handle.createWritable();
    try {
      // Copy into a fresh ArrayBuffer-backed array: the write overload requires a non-shared buffer,
      // and an arbitrary Uint8Array may be backed by a SharedArrayBuffer.
      const chunk = new Uint8Array(bytes.byteLength);
      chunk.set(bytes);
      await writable.write(chunk);
    } finally {
      await writable.close();
    }
  }

  /** @inheritdoc */
  async load(name: string): Promise<Uint8Array | null> {
    try {
      const directory = await this.open();
      const handle = await directory.getFileHandle(name);
      const file = await handle.getFile();

      return new Uint8Array(await file.arrayBuffer());
    } catch {
      // A missing entry surfaces as a not-found exception; absence is null, not an error.
      return null;
    }
  }

  /** @inheritdoc */
  async remove(name: string): Promise<void> {
    const directory = await this.open();
    try {
      await directory.removeEntry(name);
    } catch {
      // Removing an absent entry is a no-op.
    }
  }

  /** Lists the dataset names currently held. */
  async list(): Promise<string[]> {
    const directory = (await this.open()) as unknown as DirectoryKeys;
    const names: string[] = [];
    for await (const name of directory.keys()) {
      names.push(name);
    }

    return names;
  }
}

/** Whether the Origin Private File System is available in this context (it is not, in a bare Node test). */
export function opfsAvailable(): boolean {
  return typeof navigator !== 'undefined' && navigator.storage !== undefined && 'getDirectory' in navigator.storage;
}
