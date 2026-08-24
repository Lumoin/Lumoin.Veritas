// The shareable link's unit rows: which dataset an incoming address addresses, and what address the shell
// reflects back. A link is typed by hand, so a value that names no distributed dataset and is no https URL
// must address nothing rather than reach the loader; and the address the shell writes must carry the loaded
// dataset — id or remote URL — without dropping anything else the reader had in the address.

import { describe, expect, it } from 'vitest';
import { datasetLinkHref, isRemoteDatasetUrl, linkedDataset, remoteDatasetName } from './dataset-link';

/** The distributed dataset ids, as the manifest lists them. */
const ids = ['battery', 'social', 'campus'];

/** A remote Turtle document a link can address. */
const remote = 'https://data.example.org/graphs/orders.ttl';

describe('linkedDataset', () => {
  it('addresses the dataset a valid id names', () => {
    expect(linkedDataset('?dataset=campus', ids)).toBe('campus');
  });

  it('reads the parameter beside others, in either order', () => {
    expect(linkedDataset('?engine=wasm&dataset=social', ids)).toBe('social');
    expect(linkedDataset('dataset=social&engine=wasm', ids)).toBe('social');
  });

  it('addresses a remote Turtle document an absolute https URL names', () => {
    expect(linkedDataset(`?dataset=${encodeURIComponent(remote)}`, ids)).toBe(remote);
  });

  it('addresses a remote document even when the deployment distributes none', () => {
    expect(linkedDataset(`?dataset=${encodeURIComponent(remote)}`, [])).toBe(remote);
  });

  it('addresses nothing for an http URL', () => {
    expect(linkedDataset('?dataset=' + encodeURIComponent('http://data.example.org/orders.ttl'), ids)).toBeNull();
  });

  it('addresses nothing for a scheme other than https', () => {
    expect(linkedDataset('?dataset=' + encodeURIComponent('file:///c:/data/orders.ttl'), ids)).toBeNull();
    expect(linkedDataset('?dataset=' + encodeURIComponent('data:text/turtle,<a><b><c>.'), ids)).toBeNull();
    expect(linkedDataset('?dataset=' + encodeURIComponent('javascript:alert(1)'), ids)).toBeNull();
  });

  it('addresses nothing for a relative path', () => {
    expect(linkedDataset('?dataset=' + encodeURIComponent('/datasets/social.ttl'), ids)).toBeNull();
  });

  it('addresses nothing for an id no distributed dataset carries', () => {
    expect(linkedDataset('?dataset=nonsense', ids)).toBeNull();
  });

  it('addresses nothing when the parameter is absent', () => {
    expect(linkedDataset('?engine=wasm', ids)).toBeNull();
    expect(linkedDataset('', ids)).toBeNull();
  });

  it('addresses nothing for an empty value', () => {
    expect(linkedDataset('?dataset=', ids)).toBeNull();
  });

  it('addresses nothing when the deployment distributes no datasets and the value is an id', () => {
    expect(linkedDataset('?dataset=campus', [])).toBeNull();
  });
});

describe('isRemoteDatasetUrl', () => {
  it('accepts an absolute https URL', () => {
    expect(isRemoteDatasetUrl(remote)).toBe(true);
  });

  it('rejects every other scheme and unparseable text', () => {
    expect(isRemoteDatasetUrl('http://data.example.org/orders.ttl')).toBe(false);
    expect(isRemoteDatasetUrl('ftp://data.example.org/orders.ttl')).toBe(false);
    expect(isRemoteDatasetUrl('datasets/social.ttl')).toBe(false);
    expect(isRemoteDatasetUrl('campus')).toBe(false);
    expect(isRemoteDatasetUrl('')).toBe(false);
  });
});

describe('remoteDatasetName', () => {
  it('names the document by the URL last path segment', () => {
    expect(remoteDatasetName(remote)).toBe('orders.ttl');
  });

  it('decodes an escaped segment', () => {
    expect(remoteDatasetName('https://data.example.org/graphs/my%20orders.ttl')).toBe('my orders.ttl');
  });

  it('ignores a trailing slash and a query string', () => {
    expect(remoteDatasetName('https://data.example.org/graphs/orders.ttl/?v=2')).toBe('orders.ttl');
  });

  it('falls back to the host when the path names nothing', () => {
    expect(remoteDatasetName('https://data.example.org/')).toBe('data.example.org');
  });
});

describe('datasetLinkHref', () => {
  it('carries the loaded dataset in the address', () => {
    expect(datasetLinkHref('https://host/studio/', 'campus')).toBe('/studio/?dataset=campus');
  });

  it('carries a remote document URL back into the address', () => {
    expect(datasetLinkHref('https://host/studio/', remote))
      .toBe(`/studio/?dataset=${encodeURIComponent(remote)}`);
  });

  it('round-trips a remote document URL through the reader', () => {
    const href = datasetLinkHref('https://host/studio/', remote);
    expect(linkedDataset(new URL(href, 'https://host').search, ids)).toBe(remote);
  });

  it('replaces the id a stale or unknown link carried', () => {
    expect(datasetLinkHref('https://host/?dataset=nonsense', 'social')).toBe('/?dataset=social');
  });

  it('keeps every other parameter and the fragment', () => {
    expect(datasetLinkHref('https://host/?engine=wasm&sw=off#panel', 'social')).toBe('/?engine=wasm&sw=off&dataset=social#panel');
  });

  it('drops the parameter for a dataset no link can address', () => {
    expect(datasetLinkHref('https://host/?engine=wasm&dataset=social', null)).toBe('/?engine=wasm');
  });

  it('leaves an address that never carried the parameter untouched', () => {
    expect(datasetLinkHref('https://host/studio/?engine=wasm', null)).toBe('/studio/?engine=wasm');
  });
});
