// Shareable dataset links. The address bar carries the dataset the page is showing, so copying the address at
// any moment shares that session: one `dataset` parameter naming either a distributed dataset's id or an
// absolute https URL of a Turtle document to fetch. An incoming value is validated — a link is typed by hand
// and outlives any one deployment's dataset roster — and a value that is neither a listed id nor an https URL
// addresses nothing, so the shell opens what it would have opened anyway.

/** The query parameter naming the dataset a link opens. */
export const DATASET_PARAM = 'dataset';

/**
 * Whether a link value addresses a remote Turtle document: an absolute URL over https. Any other scheme —
 * http, file, data, a relative path, or unparseable text — addresses no remote document.
 * @param value The link parameter's value.
 * @returns True when the value is an absolute https URL.
 */
export function isRemoteDatasetUrl(value: string): boolean {
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * The dataset a link addresses: a distributed dataset's id, or an absolute https URL of a Turtle document.
 * @param search The address's query string, with or without the leading `?`.
 * @param ids The dataset ids the manifest lists.
 * @returns The addressed id or URL, or null when the parameter is absent, empty, or addresses neither.
 */
export function linkedDataset(search: string, ids: readonly string[]): string | null {
  const requested = new URLSearchParams(search).get(DATASET_PARAM);
  if (requested === null) {
    return null;
  }

  return ids.includes(requested) || isRemoteDatasetUrl(requested) ? requested : null;
}

/**
 * The display name a remote dataset carries: the URL's last path segment, or its host when the path names
 * nothing.
 * @param url The remote document's absolute URL.
 * @returns The name to show in the active-dataset readout.
 */
export function remoteDatasetName(url: string): string {
  const parsed = new URL(url);
  const segment = parsed.pathname.split('/').filter((part) => part.length > 0).pop();

  return segment === undefined ? parsed.host : decodeURIComponent(segment);
}

/**
 * The address that shares the loaded dataset: the current one carrying the `dataset` parameter for a
 * distributed dataset or a remote URL, or stripped of it for a dataset no link can address — an opened local
 * file lives on the reader's disk, so a link naming it would not load. Every other parameter and the fragment
 * survive, so an engine override already in the address is never dropped.
 * @param href The current address.
 * @param dataset The distributed dataset's id or the remote document's URL, or null when the loaded dataset
 * is not link-addressable.
 * @returns The address to reflect, relative to the origin.
 */
export function datasetLinkHref(href: string, dataset: string | null): string {
  const url = new URL(href);
  if (dataset === null) {
    url.searchParams.delete(DATASET_PARAM);
  } else {
    url.searchParams.set(DATASET_PARAM, dataset);
  }

  return `${url.pathname}${url.search}${url.hash}`;
}
