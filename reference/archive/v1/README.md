# MemoriaNote archive format version 1

- format identifier: `memoria-note-archive`
- archive format version: `1`
- specification revision: `1`
- release baseline: Memoria Note `v0.3.0`
- last updated: 2026-11-22

## 1. Purpose and document map

Archive format version `1` is a logical, portable backup of one Memoria Note
notebook. It is a ZIP container, not a copy of SQLite database bytes. This
document specifies the common container, framing, checksum, resource-limit,
and version-dispatch contract. The selected content-model document specifies
the metadata and page payloads:

- [content model version 1](content-models/v1/format.md) represents a notebook
  format `1` source and is retained as the legacy migration input contract;
- [content model version 2](content-models/v2/format.md) represents a notebook
  format `2` source and is the native `v0.3.0` backup and restore contract.

The tuple of `archiveFormatVersion`, `contentModelVersion`, and
`sourceNotebookFormatVersion` is matched exactly. A reader never treats an
unknown tuple as the nearest known version. The content model changes payload
meaning; the archive format changes the container or framing contract.

Checksums detect damage and incomplete output. They are not signatures and do
not establish authenticity or provenance.

## 2. Container

An archive contains exactly three file entries at the ZIP root:

```text
archive
|-- manifest.json
|-- metadata.json
`-- pages.ndjson
```

Entry names are case-sensitive ASCII strings. A reader rejects a missing,
duplicate, additional, or differently cased entry; a directory entry; an entry
below a directory; an encrypted entry; or an unsupported compression method.
Writers may store entries or use ZIP Deflate. ZIP entry order, timestamps, host
attributes, comments, and compression level are not part of the contract.
Readers process entry content as streams and never extract an entry name as a
filesystem path.

ZIP64 size fields are allowed and are required when an allowed uncompressed
payload cannot be represented by ZIP32. ZIP64 does not change the archive
format version.

The standard writer writes `metadata.json` and `pages.ndjson` before
`manifest.json`, because the manifest contains their final lengths and
checksums. Readers do not depend on entry order.

## 3. Encoding and JSON framing

All entries use UTF-8 without a byte-order mark. Invalid UTF-8 and a BOM are
rejected.

`manifest.json` and `metadata.json` each contain one JSON text. The standard
writer uses two-space indentation, LF line endings, and one final LF. Readers
accept JSON whitespace permitted by RFC 8259 and do not depend on object
property order.

`pages.ndjson` contains one JSON object per LF-delimited record. A literal CR,
a blank line, or a JSON value spanning physical lines is rejected. A non-empty
file ends with LF; an empty page set is an empty file. The standard writer
emits compact page records.

For every JSON object:

- property names are case-sensitive and duplicates are rejected;
- unknown properties are rejected;
- integer fields use decimal JSON number tokens; content-model documents state
  any additional lexical or range restrictions;
- JSON object property order is insignificant;
- strings are not Unicode-normalized.

The standard property order shown by schemas and examples improves stable
review but is not a reader precondition.

## 4. Manifest and payload descriptors

`manifest.json` contains these top-level properties:

| Property | Contract |
| --- | --- |
| `format` | Exact string `memoria-note-archive` |
| `archiveFormatVersion` | Integer `1` |
| `contentModelVersion` | Content-model selector |
| `sourceNotebookFormatVersion` | String source-format selector |
| `createdBy` | Writer application name and version |
| `createdAtUtc` | Archive creation time |
| `dataSets` | Descriptors for `metadata.json` and `pages.ndjson` |

`createdAtUtc` uses exactly `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`. It describes
archive creation and is not a notebook value.

Each data-set descriptor contains the exact entry name, record count,
uncompressed byte length, and lowercase hexadecimal SHA-256 of the complete
uncompressed entry bytes. The manifest is not included in a recursive
checksum. ZIP CRC and strict manifest parsing detect ordinary manifest damage.

The supported release tuples are:

| Archive | Content model | Source notebook | `v0.3.0` operation |
| ---: | ---: | --- | --- |
| `1` | `1` | `1` | Read only by `notebooks migrate` |
| `1` | `2` | `2` | Native `notebooks backup` and `notebooks restore` |

Mixed or unknown tuples are unsupported-version failures. A ZIP without
`manifest.json` does not imply archive format zero. A matching pre-v1 root-entry
layout may be classified as a legacy candidate for recovery guidance, but it
is not CM1 and is not accepted by migration or restore.

## 5. Checksums and counts

SHA-256 covers the exact uncompressed entry bytes, including whitespace and a
final LF. It is not calculated over decoded or reserialized values. During
preflight a reader checks actual byte length, record count, checksum, framing,
and content-model rules. ZIP header lengths are early screening values; the
streamed byte count is authoritative.

## 6. Resource limits

Limits are inclusive and apply before allocating storage proportional to the
input where possible.

| Resource | Maximum |
| --- | ---: |
| ZIP file length | 2,147,483,648 bytes (2 GiB) |
| ZIP entries | exactly 3 |
| `manifest.json` uncompressed length | 65,536 bytes (64 KiB) |
| `metadata.json` uncompressed length | 16,777,216 bytes (16 MiB) |
| metadata rows | 65,536 |
| `pages.ndjson` uncompressed length | 8,589,934,592 bytes (8 GiB) |
| page records | 1,000,000 |
| one page record, excluding LF | 67,108,864 bytes (64 MiB) |
| per-entry and total compression ratio | 1,000:1 |

Compression ratio is `uncompressedLength / max(compressedLength, 1)`. An empty
`pages.ndjson` has ratio zero. A reader checks declared lengths and actual
decompressed bytes and stops as soon as an actual-byte limit is crossed.

## 7. Validation and safe publication

A reader validates in this order:

1. physical file limit and ZIP readability;
2. entry set, names, directories, declared sizes, and compression ratio;
3. manifest encoding, structure, format identifier, and exact version tuple;
4. payload limits, encoding, JSON or NDJSON framing, and field structure;
5. actual counts, lengths, and checksums;
6. content-model semantics and uniqueness.

No destination notebook is created before preflight succeeds. A writer reads
one consistent notebook snapshot, validates the selected tuple, writes to a
private output, closes and reopens that output with the matching reader, and
publishes it only after complete validation. Failure or cancellation does not
modify the source or replace an existing destination.

## 8. Compatibility and navigation

Memoria Note `v0.3.0` creates format `2` notebooks and CM2 archives. A format
`1` notebook or CM1 archive is a supported migration source, not a normal
notebook or native restore input. See the
[schema v1 to v2 migration guide](../../migration/schema-v1-to-v2.md).

An older application that supports only CM1 rejects CM2. Rollback therefore
requires retaining the original format `1` notebook or a CM1 archive created
by the older application. Changes made only in format `2` do not have an
automatic down-conversion.

The [notebook format 2 specification](../../notebook/v2/schema.md) defines the
native authoritative-data and derived-data boundary. The
[user guide](../../user-guide.md#backup-restore-and-data-transfer) documents
commands and operational workflows.
