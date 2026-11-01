# MemoriaNote archive v1 content model v2 draft specification

- format identifier: `memoria-note-archive`
- archive format version: `1`
- content model version: `2`
- source notebook format version: `2`
- specification revision: `draft-1`
- specification status: ECLI-1130a draft reference package; not a published compatibility promise
- last updated: 2026-11-01

## 1. Purpose and compatibility status

This document defines the draft content model version `2` payload for a MemoriaNote archive format version `1`
container. It is a logical backup of one notebook format version `2` database. It preserves notebook metadata and
logical page values without copying a SQLite database file or its derived indexes.

The container, entry names, UTF-8 and JSON framing, checksums, and resource limits remain archive format version `1`.
The page payload changes in content model version `2`:

- `revision` is added;
- `index`, `contentType`, and `isErased` are removed;
- content model version 1 `rowid` becomes `sourceInternalId`;
- content model version 1 `uuid` becomes `pageId`;
- remaining properties adopt the notebook format version `2` nullability and canonical-value rules.

`sourceInternalId` records the positive SQLite-local `Pages.InternalId` value observed in the source snapshot. It
supports source fidelity, deterministic writer order, validation, and safe diagnostics. It is not a public page
identity and is not assigned to the restore target. The stable identity used for restore equivalence is `pageId`.

This draft extends the published
[archive v1 content model version 1 specification](../v1/format.md) for notebook format version `2`. It does not
redefine the published tuple `(archive format 1, content model 1, source notebook format 1)`. That tuple remains
governed by its published specification and compatibility promise.

This package is a review and implementation input for ECLI-1130b. No released MemoriaNote version promises to write,
read, or restore the candidate tuple `(1, 2, "2")` yet. Publishing this package does not add that tuple to the
application compatibility matrix. ECLI-1190 will reconcile the accepted contract with the released implementation
and reorganize the archive reference documents.

Archive format version, content model version, source notebook format version, application version, and this prose
specification revision are independent version axes. `draft-1` identifies only this reference draft and is not stored
in an archive.

## 2. Container

The content model version `2` archive uses the archive format version `1` ZIP container with exactly three file
entries at its root.

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
- Integer fields use a JSON number token containing decimal digits only. Fractional, exponent, signed, and
  leading-zero forms are rejected, except that the single token `0` remains structurally valid where a field permits
  zero.
- Strings are not Unicode-normalized. Escaped and unescaped representations may differ at the byte level while
  decoding to the same string.

All integer properties in a page record are positive, so their semantic validation rejects zero even though the
common lexical rule can recognize it. Data set counts and lengths permit zero.

The standard writer uses the property order shown in the schemas and examples. This ordering improves review and
stable fixture diffs but is not a reader precondition.

## 4. Manifest

`manifest.json` conforms to [manifest.schema.json](schema/manifest.schema.json). Its exact candidate version tuple is:

```json
{
  "format": "memoria-note-archive",
  "archiveFormatVersion": 1,
  "contentModelVersion": 2,
  "sourceNotebookFormatVersion": "2"
}
```

The complete top-level properties are:

| Property | Meaning |
| --- | --- |
| `format` | Exact format identifier, `memoria-note-archive` |
| `archiveFormatVersion` | ZIP layout and encoding rules; integer `1` |
| `contentModelVersion` | Metadata and page record contract; integer `2` |
| `sourceNotebookFormatVersion` | Exact non-null value stored in the source `Metadata.Version` row; string `2` |
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
  "value": "2"
}
```

`key` is a non-null string. `value` is a string or JSON null. Empty strings and null are distinct values. Metadata is
not represented as a JSON object because unknown keys and the stored row contract must remain explicit.

The standard writer orders rows by `key` using ordinal comparison. Readers do not depend on row order. Keys must be
unique using ordinal, case-sensitive comparison. Unknown metadata keys are valid and their values are preserved.

For the candidate source notebook format version `2` tuple:

- `Name`, `Title`, and `Version` rows occur exactly once.
- `Version.value` is the non-null string `2` and equals `sourceNotebookFormatVersion` exactly.
- `Name.value` and `Title.value` are non-null.
- Null, empty, and non-empty values of optional and unknown keys remain distinct and are preserved without
  interpretation.
- No metadata value is normalized, inferred, or regenerated by the archive codec.

The decoded key length is limited to 1,024 Unicode scalar values and 4,096 UTF-8 bytes. The whole entry limit remains
authoritative if a metadata value is large.

## 6. Pages data set

Each non-empty line of `pages.ndjson` conforms structurally to
[page.schema.json](schema/page.schema.json). Every property is required and none permits JSON null.

| Property | JSON type | Preservation and validation |
| --- | --- | --- |
| `sourceInternalId` | integer | Positive signed 64-bit source-local value; unique in the data set; not assigned to the restore target |
| `pageId` | string | Non-zero lowercase UUID in exact `D` layout; unique in the data set |
| `name` | string | Canonical format 2 page name; unique by ordinal value |
| `createTime` | string | Canonical format 2 creation timestamp |
| `updateTime` | string | Canonical format 2 last-update timestamp |
| `revision` | integer | Positive signed 64-bit optimistic-concurrency revision |
| `tags` | string | Stored canonical JSON array text is preserved exactly |
| `text` | string | Stored body, Unicode, newlines, and empty string are preserved |

`index`, `contentType`, and `isErased` are not content model version `2` properties. Their presence is an
unknown-property validation failure.

An example page record is:

```json
{"sourceInternalId":42,"pageId":"11111111-2222-3333-4444-555555555555","name":"example","createTime":"2026-10-31 10:20:30.0000000","updateTime":"2026-10-31 11:22:33.0000000","revision":7,"tags":"[\"archive\",\"example\"]","text":"Page body"}
```

### 6.1 Source-local and public identity

`sourceInternalId` is the exact positive `Pages.InternalId` value read from the source snapshot. Its maximum is
`9,223,372,036,854,775,807`. It is preserved in the archive and validated for positivity and uniqueness. The standard
writer orders page records by `sourceInternalId`.

A reader does not depend on record order. Restore and migration must not use `sourceInternalId` as the target primary
key, public page identity, conflict key, or equivalence key. A restore target allocates its own positive
`Pages.InternalId` values. A later backup of the restored notebook may therefore contain different
`sourceInternalId` values.

`pageId` is the stable public identity. It contains a lowercase, hyphenated, non-zero UUID in exact
`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` layout. Uniqueness uses the parsed 128-bit UUID value. The archive preserves
this text exactly and restore uses it for page identity and equivalence.

### 6.2 Name

`name` contains 1 through 255 Unicode scalar values. It has no leading or trailing whitespace, contains no control
character, and is not Unicode-normalized or case-folded. Names are unique within the data set using ordinal,
case-sensitive comparison.

### 6.3 Timestamps and revision

`createTime` and `updateTime` use exactly:

```text
yyyy-MM-dd HH:mm:ss.fffffff
```

They contain 27 characters, exactly seven fractional-second digits, and no timezone suffix or numeric offset.
Validation checks the Gregorian calendar date and clock values as well as lexical form. The stored text is copied
without conversion or fractional-second rewriting.

`revision` is a positive signed 64-bit integer with a maximum of `9,223,372,036,854,775,807`. It is an authoritative
optimistic-concurrency value and is preserved exactly during backup and restore. Restore does not reset it to `1`.

### 6.4 Tags

`tags` is non-null text containing one canonical JSON array. No tags are represented by the exact text `[]`.

Every array element is a string label with these rules:

- 1 through 64 Unicode scalar values;
- not empty or whitespace-only;
- no leading or trailing whitespace;
- no control characters;
- no trimming, Unicode normalization, or case folding;
- unique by ordinal, case-sensitive comparison within one page.

Labels are sorted by their UTF-8 bytes. The canonical codec emits valid Unicode scalars directly, escapes `"` as
`\"` and `\` as `\\`, and emits no optional JSON whitespace or optional Unicode escapes. The decoded labels and the
stored JSON text must both be canonical. Logically equivalent but byte-different JSON is rejected rather than
rewritten.

### 6.5 Body

`text` is a non-null string. Empty text is represented by `""`. Unicode, escaped newlines, and all other valid JSON
string content are preserved. The archive codec does not normalize or reinterpret the body.

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

The limits permit large notebooks and page bodies while bounding memory, disk, and decompression work.
Implementations stream page records and must not require the entire pages data set in memory.

## 9. Version compatibility matrix

This draft describes a candidate tuple without changing the support status or meaning of any published tuple:

| Archive format | Content model | Source notebook format | Contract status | Released write | Released read | Intended operation or target |
| ---: | ---: | --- | --- | --- | --- | --- |
| `1` | `1` | `1` | published CM1 contract | yes | yes | Format 1 restore under the published specification; explicit migration to format 2 is a separate contract |
| `1` | `2` | `2` | ECLI-1130a draft candidate | no | no | Proposed lossless logical restore to a new format 2 notebook |

A future format 2 application implementing this draft writes only `(1, 2, "2")` for a native format 2 backup. It may
recognize and validate `(1, 1, "1")` for an explicit migration path, but must not reinterpret that payload as content
model version `2`.

Applications that support only `(1, 1, "1")` reject `(1, 2, "2")` as an unsupported version tuple before interpreting
page payloads. Rollback therefore requires retention of the older notebook or an archive created by a compatible
older application.

Mixed tuples such as `(1, 1, "2")` and `(1, 2, "1")` are unsupported. An unsupported value in any version field
produces an unsupported-version result, not a malformed-JSON result. Unknown versions are never interpreted as the
nearest known version.

`archiveFormatVersion` changes when container layout, entry encoding, checksum placement, or framing changes.
`contentModelVersion` changes when payload properties, types, nullability, or meaning change. The property and
semantic changes in this draft therefore increment only `contentModelVersion`.

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

ZIP input without `manifest.json` is never treated as archive format version zero. Existing archive v1 classification
rules for a legacy candidate, unrelated ZIP, and malformed ZIP remain unchanged.

## 11. Writer requirements

A future archive v1 content model version `2` writer conforming to this draft:

1. reads `Metadata` and `Pages` from one consistent format 2 database snapshot;
2. reads stored authoritative values directly without using `PageTags`, FTS, or another derived representation as an
   independent source;
3. maps `Pages.InternalId` to `sourceInternalId` and `Pages.PageId` to `pageId`;
4. validates source rows against the exact `(1, 2, "2")` tuple;
5. writes payloads and computes counts, lengths, and SHA-256 while streaming;
6. writes the completed manifest;
7. closes and reopens the temporary archive with a reader supporting this tuple;
8. publishes it only after complete validation succeeds.

The writer emits metadata rows in ordinal key order and pages in ascending `sourceInternalId` order. This canonical
writer order is not a promise that a restored database will have identical internal IDs, physical row placement,
SQLite bytes, or archive bytes after another backup.

## 12. Restore equivalence

Restore equivalence compares source and restored logical authoritative values, not SQLite-local identities or
physical state.

The following values must round-trip exactly:

- every `Metadata.Key` and `Metadata.Value`, including null for optional or unknown metadata;
- every page `pageId`, `name`, `createTime`, `updateTime`, `revision`, `tags`, and `text`.

`sourceInternalId` must be preserved accurately in the archive and validated, but is deliberately excluded from
restore equivalence. The restore target allocates a new positive `Pages.InternalId` for each page and maintains an
internal mapping from `pageId` to that target-local value while loading and verifying derived data. The source value
must never be used as the target primary key.

SQLite file bytes, physical row layout, `sqlite_sequence`, `PageTags`, FTS5 tables and shadow tables, triggers,
indexes, and other SQLite internal state are not copied values. The restore target creates the current format 2
schema normally, inserts logical authoritative values, rebuilds all required derived data, and validates schema,
SQLite integrity, authoritative values, derived data, and logical equivalence before publishing the new `.mnote`
file.

Failure or cancellation must not modify the source archive or publish an incomplete target. An existing destination
is never replaced.

## 13. Schemas and examples

- [Published archive v1 content model version 1 specification](../v1/format.md)
- [Manifest JSON Schema](schema/manifest.schema.json)
- [Metadata JSON Schema](schema/metadata.schema.json)
- [Page record JSON Schema](schema/page.schema.json)
- [Valid, invalid, and compatibility examples](examples/README.md)

JSON Schema covers document shape, JSON types, required and unknown properties, basic lexical patterns, and declared
limits. Rules involving ZIP structure, UTF-8 bytes, integer token spelling, checksums, cross-document equality,
uniqueness, canonical tags, calendar validity, source-local identity handling, restore equivalence, and version tuples
are normative semantic requirements in this specification and must also be enforced by a future archive validator.
