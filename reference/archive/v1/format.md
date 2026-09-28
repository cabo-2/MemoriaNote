# MemoriaNote archive v1 format specification

- format identifier: `memoria-note-archive`
- archive format version: `1`
- content model version: `1`
- specification status: NCLI-400 format baseline
- last updated: 2026-09-28

## 1. Purpose and compatibility promise

Archive v1 is a logical backup of one MemoriaNote notebook. It preserves every authoritative value in the current
`Metadata` and `Pages` tables without depending on the current persistence entities. It is not a copy of a SQLite
database file.

Once published, the tuple of archive format version `1` and content model version `1` remains readable even if the
application model or database schema changes. A reader must accept only version combinations listed in its explicit
compatibility matrix. It must not interpret an unknown version as the nearest known version.

Archive v1 is intended for data portability and migration. Its checksums detect corruption and incomplete output;
they are not digital signatures and do not establish the origin or authenticity of an archive.

## 2. Container

An archive is a ZIP container with exactly three file entries at its root.

```text
archive
├── manifest.json
├── metadata.json
└── pages.ndjson
```

Entry names are case-sensitive ASCII strings and must match the names above exactly. A reader rejects:

- a missing, duplicate, additional, or differently cased entry;
- a directory entry;
- an entry below a directory or with a path separator;
- an encrypted entry;
- a compression method it does not support.

Writers may store entries without compression or use ZIP Deflate. ZIP entry order, timestamps, host attributes,
comments, and compression level are not part of the contract. Readers process entry content as a stream and never
extract an entry name as a file-system path.

ZIP64 size fields are permitted and are required when an allowed uncompressed payload cannot be represented by ZIP32.
Using ZIP64 does not change the archive format version.

The standard writer writes `metadata.json` and `pages.ndjson` before `manifest.json` because the manifest contains
the final payload lengths and checksums. Readers do not depend on entry order.

## 3. Encoding and JSON rules

All three entries use UTF-8 without a byte-order mark. Invalid UTF-8 and a BOM are rejected.

`manifest.json` and `metadata.json` each contain one JSON text. The standard writer uses two-space indentation, LF
line endings, and one final LF. Readers may accept JSON whitespace permitted by RFC 8259 and do not depend on object
property order.

`pages.ndjson` uses one JSON object per LF-delimited record. A literal CR, a blank line, and a JSON value spanning
physical lines are rejected. JSON string newlines remain escaped. A non-empty file ends with LF; an empty page data
set is represented by an empty file. The standard writer emits each record as compact JSON.

The following rules apply to every JSON object:

- Property names are case-sensitive.
- Duplicate property names are rejected, even when a general-purpose JSON parser would retain the last value.
- Unknown properties are rejected. A compatible extension therefore requires a content model or archive format
  version decision.
- JSON object property order is not significant.
- Integer fields use a JSON number token containing decimal digits only. Fractional and exponent forms are rejected.
- Strings are not Unicode-normalized. Escaped and unescaped representations may differ at the byte level while
  decoding to the same stored string.

The standard writer uses the property order shown in the schemas and examples. This ordering improves review and
stable fixture diffs but is not a reader precondition.

## 4. Manifest

`manifest.json` conforms to [manifest.schema.json](schema/manifest.schema.json). Its top-level properties are:

| Property | Meaning |
| --- | --- |
| `format` | Exact format identifier, `memoria-note-archive` |
| `archiveFormatVersion` | ZIP layout and encoding rules; integer `1` |
| `contentModelVersion` | Metadata and page record contract; integer `1` |
| `sourceNotebookFormatVersion` | Exact non-null value stored in the source `Metadata.Version` row |
| `createdBy` | Application name and application version reported by the writer |
| `createdAtUtc` | Archive creation time in canonical UTC form |
| `dataSets` | Descriptor for each authoritative payload |

`createdAtUtc` uses exactly `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`. This value describes archive creation and is not a
source database value. Its Gregorian calendar date and clock value must be valid. A writer may normalize this one
value to UTC.

Each data set descriptor contains:

| Property | Meaning |
| --- | --- |
| `entry` | Exact ZIP entry name |
| `recordCount` | Number of metadata rows or NDJSON page records |
| `uncompressedByteLength` | Number of bytes in the complete uncompressed entry |
| `sha256` | Lowercase hexadecimal SHA-256 of the complete uncompressed entry bytes |

The manifest itself is not included in a recursive checksum. ZIP CRC and strict manifest parsing detect ordinary
manifest damage; archive v1 does not provide tamper resistance.

## 5. Metadata data set

`metadata.json` conforms structurally to [metadata.schema.json](schema/metadata.schema.json). It is an array of row
objects with exactly two properties:

```json
{
  "key": "Version",
  "value": "1"
}
```

`key` is a non-null string. `value` is a string or JSON null. Empty strings and null are distinct values. Metadata is
not represented as a JSON object because unknown keys and the stored row contract must remain explicit.

The standard writer orders rows by `key` using ordinal comparison. Readers do not depend on row order. Keys must be
unique using ordinal, case-sensitive comparison. Unknown metadata keys are valid and their values are preserved.

For a source notebook format version `1` archive:

- `Name`, `Title`, and `Version` rows occur exactly once.
- `Version.value` is non-null and equals `sourceNotebookFormatVersion` exactly.
- `Name.value` and `Title.value` remain nullable because the current table permits null; they are not rewritten.
- If `ReadOnly.value` is non-null, it is parseable by the current Boolean contract (`True` or `False`, ignoring ASCII
  case). The original spelling is preserved.
- If `CreateTime.value` is non-null, it is a valid current metadata timestamp in `yyyyMMddhhmmss` form. The `hh`
  component is the current 12-hour storage field (`01` through `12`) and has no timezone. The original value is
  preserved.
- Other known and unknown values have no additional content-level interpretation in archive v1.

The decoded key length is limited to 1,024 Unicode scalar values and 4,096 UTF-8 bytes. The whole entry limit remains
authoritative if a metadata value is large.

## 6. Pages data set

Each non-empty line of `pages.ndjson` conforms structurally to [page.schema.json](schema/page.schema.json). Every
property is required; only properties corresponding to nullable database columns may contain JSON null.

| Property | JSON type | Preservation and validation |
| --- | --- | --- |
| `rowid` | integer | Positive signed 32-bit value; unique in the data set |
| `uuid` | string | Non-empty GUID in exact D layout; unique ignoring hexadecimal case |
| `name` | string or null | Stored text and null are preserved |
| `index` | integer | Positive signed 32-bit value |
| `tags` | string or null | Stored text is preserved; semantic rule below |
| `contentType` | string or null | Stored text and null are preserved |
| `createTime` | string | Valid current SQLite DateTime text; original text is preserved |
| `updateTime` | string | Valid current SQLite DateTime text; original text is preserved |
| `isErased` | integer | Exactly `0` or `1` |
| `text` | string or null | Stored text, Unicode, newlines, empty string, and null are preserved |

UUID validation accepts uppercase or lowercase hexadecimal digits but does not rewrite case. The all-zero GUID is
rejected. UUID uniqueness uses the parsed 128-bit value, so case variants are duplicates.

Page timestamps use `yyyy-MM-dd HH:mm:ss` with an optional period and one through seven fractional-second digits.
They have no timezone suffix. Validation checks the Gregorian calendar date and clock values in addition to the JSON
Schema pattern. No conversion to UTC, fractional-second padding, or other reformatting is performed.

A non-null `tags` value is valid when either:

- it is empty or contains only JSON whitespace, matching the current empty-tag behavior; or
- it is one complete JSON object whose property values are strings or null.

Duplicate property names, nested values, arrays, numbers, and Boolean tag values are rejected. The tag JSON text is
not parsed and serialized again when writing or restoring the archive.

The standard writer orders page records by `rowid`. Readers do not depend on record order and reject duplicate rowids
or UUIDs before creating a restore database.

## 7. Checksums and count validation

SHA-256 is calculated over the exact uncompressed entry bytes, including JSON whitespace and the final LF when one is
present. It is not calculated over decoded values or a reserialized document.

A reader streams each payload once during preflight and checks all of the following against its manifest descriptor:

1. the actual uncompressed byte length;
2. the actual number of rows or NDJSON records;
3. the SHA-256 digest;
4. the structural and semantic content rules.

Any mismatch rejects the archive. The ZIP header's declared length is an early screening value only; the streaming
byte count is authoritative.

## 8. Resource limits

Archive v1 readers enforce the following inclusive limits before allocating storage proportional to input where
possible. These are format-v1 safety limits, not writer recommendations.

| Resource | Maximum |
| --- | ---: |
| ZIP file length | 2,147,483,648 bytes (2 GiB) |
| ZIP file entries | exactly 3 |
| `manifest.json` uncompressed length | 65,536 bytes (64 KiB) |
| `metadata.json` uncompressed length | 16,777,216 bytes (16 MiB) |
| metadata rows | 65,536 |
| `pages.ndjson` uncompressed length | 8,589,934,592 bytes (8 GiB) |
| page records | 1,000,000 |
| one page record, excluding LF | 67,108,864 bytes (64 MiB) |
| per-entry and total compression ratio | 1,000:1 |

Compression ratio is `uncompressedLength / max(compressedLength, 1)`. An empty `pages.ndjson` has ratio zero. A reader
checks both ZIP-declared lengths and bytes actually produced by decompression. Crossing an actual-byte limit stops
decompression immediately.

The limits deliberately permit notebooks larger than the initial 10,000-page performance scenario and large page
bodies while bounding memory, disk, and decompression work. Implementations stream page records and must not require
the entire pages data set in memory.

## 9. Version compatibility matrix

The initial writer and reader support exactly this tuple:

| Archive format | Content model | Source notebook format | Write | Read | Restore target |
| ---: | ---: | --- | --- | --- | --- |
| `1` | `1` | `1` | yes | yes | current schema, format `1` |

An unsupported value in any version field produces an unsupported-version result, not a malformed-JSON result. A
future implementation may add a tuple only after defining its transformation and tests. Adding a new tuple does not
change the meaning of an already supported tuple.

`archiveFormatVersion` changes when container layout, entry encoding, checksum placement, or framing changes.
`contentModelVersion` changes when payload properties, types, nullability, or meaning change. A source database format
change does not by itself require either archive version to change if the existing content model still represents it;
the reader compatibility matrix must still list the new tuple explicitly.

## 10. Validation order and classification

A reader validates in this order so unsafe or unsupported input fails before database creation:

1. physical file limit and ZIP readability;
2. entry count, exact names, duplicates, directories, declared sizes, and compression ratio;
3. manifest encoding, JSON structure, format identifier, and supported version tuple;
4. payload streaming limits, encoding, JSON or NDJSON framing, and field structure;
5. actual counts, byte lengths, and checksums;
6. metadata and page semantic rules and uniqueness.

No restore database is created until all six stages succeed. A reader may stop at the first resource-limit or framing
failure. For other safe inputs it may collect multiple validation issues; issue ordering is not part of archive v1.

ZIP input without `manifest.json` is never treated as archive format version zero. A ZIP whose root entry structure
matches the documented legacy candidate pattern may be classified as a legacy candidate without reading its payload.
It remains unsupported by the archive v1 reader. A malformed ZIP and an unrelated ZIP receive separate
classifications.

## 11. Writer requirements

An archive v1 writer:

1. reads `Metadata` and `Pages` from one consistent database snapshot;
2. reads raw stored values without materializing `NotebookMetadata`, `Page`, or another value-normalizing entity;
3. validates source rows against the selected supported version tuple;
4. writes payloads and computes counts, lengths, and SHA-256 while streaming;
5. writes the completed manifest;
6. closes and reopens the temporary archive with an archive v1 reader;
7. publishes it only after the complete validation succeeds.

The writer emits metadata rows in ordinal key order and pages in ascending rowid order. This canonical writer order is
not a promise that a restored database will have identical physical row placement or identical SQLite bytes.

## 12. Restore equivalence

Restore equivalence means comparing source and restored authoritative tables in primary-key order:

- every `Metadata.Key` and `Metadata.Value`, including null;
- every listed `Pages` column, including text representation and null.

SQLite file bytes, physical row layout, `sqlite_sequence`, `Contents`, FTS internals, triggers, indexes, and EF migration
history are not copied values. The restore target creates the current schema normally, inserts authoritative values,
rebuilds `Contents` and FTS, and verifies them before publishing the new `.mnote` file.

## 13. Schemas and examples

- [Manifest JSON Schema](schema/manifest.schema.json)
- [Metadata JSON Schema](schema/metadata.schema.json)
- [Page record JSON Schema](schema/page.schema.json)
- [Valid and invalid expanded examples](examples/README.md)

JSON Schema covers document shape, JSON types, required and unknown properties, basic lexical patterns, and declared
limits. Rules involving ZIP structure, UTF-8 bytes, checksums, cross-document equality, uniqueness, Tags JSON, calendar
validity, and version tuples are normative semantic requirements in this specification and must also be enforced by
the archive validator.
