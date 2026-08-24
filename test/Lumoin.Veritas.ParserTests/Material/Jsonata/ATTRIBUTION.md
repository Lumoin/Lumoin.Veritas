# JSONata test suite — provenance and license

The files under `groups/` (the `case###.json` test cases and their `*.jsonata`
expression files), `datasets/` (the shared input documents), and `TESTSUITE.md`
(the test-case format) in this directory are vendored verbatim from the JSONata
reference implementation's language-neutral conformance suite.

- **Source repository:** https://github.com/jsonata-js/jsonata
- **Path in source:** `test/test-suite/`
- **Pinned commit:** `06fc08cacff16315edddec9632e98c8c7342785b`
- **License:** MIT

These are vendored as a pinned, read-only corpus (a copy, not a maintained
clone): the upstream working tree is not retained. The corpus is consumed by the
`RunJsonataTest` conformance harness, which reads it directly from the source
tree, and is not compiled or copied into the build output.

## License (MIT)

MIT license

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
