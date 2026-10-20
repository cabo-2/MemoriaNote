# MemoriaNote notebook database schema v1

- notebook format version: `1`
- specification revision: `1.0`
- release baseline: MemoriaNote `v0.2.0`
- last updated: 2026-10-20

## 1. Purpose and compatibility promise

A MemoriaNote notebook is one SQLite database, conventionally stored in a file
whose name ends in `.mnote`. This document specifies notebook format version
`1` as read and written by MemoriaNote `v0.2.0`.

The notebook format version is independent of the MemoriaNote application
version. More than one application release may read and write format `1`.
Specification revision `1.0` identifies this prose specification; it is not a
value stored in the database.

This specification describes the persisted contract, including legacy and
nullable values that the format can represent. A command may apply a stricter
policy when it creates or edits data, but that policy does not add a SQLite
constraint unless this document says so. Observed SQLite or Entity Framework
Core implementation details are identified separately from compatibility
requirements.

Published format `1` retains the meaning defined here. A future incompatible
database contract will use a new notebook format version and a separate
specification such as `reference/notebook/v2/schema.md`; it will not redefine
this document in place.

## 2. Format identity and recognition

### 2.1 Persisted identity

The current product recognizes format `1` using both application metadata and
EF Core migration history:

| Signal | Required value | Role |
| --- | --- | --- |
| `Metadata` row with key `Version` | non-null text `1` | Persisted notebook format version |
| Migration ID | `20230404004629_InitialCreate` | Creates the relational tables and indexes |
| Migration ID | `20230404004813_FtsIndexTable` | Creates FTS5 and synchronization triggers |

The applied migration IDs must exactly equal the ordered migration set known to
the application. The `ProductVersion` stored with each migration is provenance
from the migration writer and is not compared by the format validator.

The following properties do not positively identify a MemoriaNote notebook:

- the `.mnote` file extension;
- `PRAGMA application_id`, which is the SQLite default `0` in format `1`;
- `PRAGMA user_version`, which is the SQLite default `0` in format `1`;
- the presence of an arbitrary SQLite database header without the required
  metadata and migration IDs.

### 2.2 Current recognition behavior

The format validator reads metadata and migration history without applying
migrations or rebuilding derived data.

| Input state | Classification or result |
| --- | --- |
| File does not exist | Missing file; validation does not create a SQLite file |
| Required metadata is valid, `Version` is exactly `1`, and migration IDs exactly match | Current format |
| `Version` is a decimal integer greater than `1` | Unsupported newer notebook format |
| `Version` is lower, non-numeric, null, or otherwise not exactly `1` | Invalid or unrecognized notebook |
| `Name`, `Title`, or `Version` key is absent | Invalid notebook |
| Non-null `ReadOnly` or `CreateTime` has an invalid representation | Invalid notebook |
| Migration IDs are missing, additional, or out of the expected set | Invalid notebook |
| SQLite error codes `1`, `11`, or `26` during validation | Invalid notebook with the SQLite error preserved as the cause |

The current API does not expose separate legacy and corrupt classifications.
Recognition also does not compare every table, column, index, trigger, or FTS5
option. Successful recognition therefore does not by itself prove that all
derived objects are intact; Section 9 defines the separate read-model integrity
boundary.

## 3. Schema overview and data ownership

Data ownership is independent of which tool created an object.

| Classification | Objects | Contract |
| --- | --- | --- |
| Authoritative data | `Metadata` | Raw notebook metadata key-value rows |
| Authoritative data | `Pages` | Page identity, metadata, and body |
| Derived read model | `Contents` | Body-free projection of `Pages` used for listing and heading search |
| Derived search index | `FtsIndex` | FTS5 external-content index over selected `Pages` columns |
| Synchronization | `Pages_Insert`, `Pages_Update`, `Pages_Delete` | Maintains both derived objects after writes to `Pages` |
| Migration bookkeeping | `__EFMigrationsHistory` | Records the applied migration IDs used during recognition |
| EF implementation state | `__EFMigrationsLock` | Coordinates migration execution; not user data or a format discriminator |
| SQLite and FTS5 internal state | `sqlite_sequence`, FTS5 shadow tables, automatic indexes | Generated physical state, not independent notebook data |

Only `Metadata` and `Pages` are authoritative notebook data. `Contents` and
`FtsIndex` must agree with `Pages` during normal operation, but they are not an
independent source of truth and can be rebuilt.

## 4. Authoritative tables

### 4.1 `Metadata`

`Metadata` stores one raw text value per case-sensitive key.

```sql
CREATE TABLE "Metadata" (
    "Key" TEXT NOT NULL CONSTRAINT "PK_Metadata" PRIMARY KEY,
    "Value" TEXT NULL
);
```

The primary key uses SQLite `BINARY` comparison. Key text is not
Unicode-normalized. Unknown keys are allowed and are authoritative: archive v1
preserves them together with their nullable values.

### 4.2 `Pages`

`Pages` is the authoritative page table.

| Column | Declared type | Null | Key or index | Meaning |
| --- | --- | --- | --- | --- |
| `Rowid` | `INTEGER` | no | Primary key, `AUTOINCREMENT` | Internal row identity and FTS5 content row ID |
| `Uuid` | `TEXT` | no | Unique alternate key | Stable public Page ID |
| `Name` | `TEXT` | yes | First column of `IX_Pages_Name_Index` | Page heading |
| `Index` | `INTEGER` | no | Second column of `IX_Pages_Name_Index` | Ordinal within an exact-name group |
| `Tags` | `TEXT` | yes | none | Serialized tag dictionary |
| `ContentType` | `TEXT` | yes | none | Legacy content discriminator |
| `CreateTime` | `TEXT` | no | none | Page creation time |
| `UpdateTime` | `TEXT` | no | none | Last page update time |
| `IsErased` | `INTEGER` | no | none | Legacy erase flag |
| `Text` | `TEXT` | yes | none | Page body |

The table has no foreign keys and no check constraints. In particular, SQLite
does not enforce canonical UUID text, valid tag JSON, positive ordinals,
Boolean values limited to `0` and `1`, or a particular timestamp grammar.

`IX_Pages_Name_Index` is a non-unique, non-partial index over `Name` and
`Index`, in ascending order with `BINARY` collation. Duplicate `(Name, Index)`
pairs and null names remain representable. The unique `Uuid` constraint is the
only database-enforced page identity rule besides the primary key.

## 5. Metadata keys

The table permits a null `Value` for every key. Required below means that the
row must be present for current-format recognition; it does not change the
column nullability.

| Key | Presence | Stored representation and behavior |
| --- | --- | --- |
| `Name` | Required | Notebook name as text or null. Fresh creation writes the requested name. |
| `Title` | Required | Notebook title as text or null. Fresh creation writes the requested title. |
| `Version` | Required | Must be non-null text `1` for current format recognition. |
| `Description` | Optional | Text or null. Absence maps to null. |
| `Author` | Optional | Text or null. Absence maps to null. |
| `ReadOnly` | Optional | Non-null values are parsed as case-insensitive `True` or `False`; absence or null maps to false. This is logical page-write protection, not SQLite file mode. |
| `Tag` | Optional | Text or null. Absence maps to null. |
| `CreateTime` | Optional | Non-null values use `yyyyMMddhhmmss` with invariant culture; absence or null maps to the default application time value. |

`CreateTime` uses the 12-hour `hh` component without an AM/PM designator or
time-zone offset. The resulting ambiguity is part of the format `1` encoding
and must not be silently reinterpreted as an unambiguous UTC instant.

A present `Name` or `Title` row with a null value does not itself produce a
metadata-validation issue. A null `Version` cannot satisfy the exact format
version comparison. Empty strings, null values, missing optional keys, and
unknown keys are distinct stored states.

## 6. Page value representations

### 6.1 Identity and ordering

`Rowid` is an internal identity backed by an SQLite `INTEGER PRIMARY KEY
AUTOINCREMENT`. SQLite row IDs are signed 64-bit integers; the current
application maps this column to a signed 32-bit integer, and normal product and
archive writes use a positive value. It also supplies the `content_rowid` for
`FtsIndex`.

`Uuid` is the stable public Page ID. The product writes a non-zero GUID in
canonical `D` layout, for example
`15aa0763-b375-49c6-882e-e6d58f1bf8f4`. SQLite enforces uniqueness and
non-nullability but does not enforce that lexical form or reject the all-zero
GUID.

`Index` is managed by page operations as a positive, one-based ordinal among
pages with an exact matching `Name`. Positivity, contiguity, and uniqueness
within a name group are application policy rather than database constraints.

### 6.2 Tags and legacy fields

`Tags` normally contains a JSON object whose property names are strings and
whose values are strings or null. A null, empty, or JSON-whitespace value is
treated by the current model as an empty dictionary. SQLite does not validate
JSON, and reading other non-null representations through the application model
can fail. Values are stored without Unicode normalization.

`ContentType` is a nullable legacy discriminator. Current text-page creation
writes `T`; format `1` continues to represent other text or null values.

`IsErased` is a legacy Boolean written by the application as SQLite integer
`0` or `1`. The database accepts other integers because it has no check
constraint.

### 6.3 Timestamps and body

`CreateTime` and `UpdateTime` are non-null SQLite text values materialized as
application `DateTime` values. The current EF Core writer produces text such as
`2026-10-17 08:45:33.9818724`. The stored value has no `Z` suffix or numeric
offset. Product writes originate from a UTC clock, but UTC cannot be proven
from the database text alone.

`Text` stores the nullable page body. Text values, including line breaks,
Japanese text, emoji, and empty strings, are stored as SQLite text without
Unicode normalization. Null and empty text are distinct.

## 7. Derived read models and search index

### 7.1 `Contents`

`Contents` is a body-free projection of `Pages`. It has these columns in this
order:

```text
Uuid, Rowid, Name, Index, Tags, ContentType,
CreateTime, UpdateTime, IsErased
```

`Uuid` is the non-null text primary key. `Rowid`, `Index`, `CreateTime`,
`UpdateTime`, and `IsErased` are non-null; the remaining projected values are
nullable as in `Pages`. `IX_Contents_Name_Index` is a non-unique, non-partial
ascending index on `(Name, Index)` using `BINARY` collation.

There is no foreign-key constraint from `Contents` to `Pages`. Equality is
maintained by triggers and checked by application integrity logic. List and
heading-search operations use this table so they do not need to materialize
page bodies.

### 7.2 `FtsIndex`

The second migration creates this FTS5 external-content table:

```sql
CREATE VIRTUAL TABLE FtsIndex USING fts5(
    Uuid,
    Name,
    Tags,
    Text,
    content='Pages',
    content_rowid='Rowid',
    tokenize='unicode61'
);
```

All four visible columns are indexed; none is declared `UNINDEXED`. The FTS5
row ID is `Pages.Rowid`. `Pages` remains the content source and authoritative
record. FTS5 shadow tables are extension-managed index state, not additional
notebook records.

### 7.3 Synchronization triggers

| Trigger | Event | Derived writes |
| --- | --- | --- |
| `Pages_Insert` | `AFTER INSERT` on `Pages` | Inserts the complete `Contents` projection and one FTS5 row |
| `Pages_Delete` | `AFTER DELETE` on `Pages` | Sends the FTS5 external-content delete command with old indexed values, then deletes `Contents` by old UUID |
| `Pages_Update` | `AFTER UPDATE` on `Pages` | Deletes and reinserts the FTS5 row, deletes the old summary by UUID, and inserts the new summary |

The update trigger runs for every page update and is not column-selective.
Trigger writes participate in the same SQLite statement and transaction as the
authoritative write. A trigger failure causes that write to fail rather than
committing independently inconsistent derived data.

## 8. Indexes, triggers, and migration history

The application-created relational indexes are:

| Table | Logical constraint or index | Unique | Columns |
| --- | --- | --- | --- |
| `Metadata` | Primary key | yes | `Key` |
| `Pages` | Primary key | yes | `Rowid` |
| `Pages` | Alternate key | yes | `Uuid` |
| `Pages` | `IX_Pages_Name_Index` | no | `Name`, `Index` |
| `Contents` | Primary key | yes | `Uuid` |
| `Contents` | `IX_Contents_Name_Index` | no | `Name`, `Index` |
| `__EFMigrationsHistory` | Primary key | yes | `MigrationId` |

SQLite-generated names such as `sqlite_autoindex_Pages_1` describe a physical
implementation of a declared constraint. Those generated names and physical
B-tree layout are not stable application-level identifiers.

EF Core maintains this migration history shape:

```sql
CREATE TABLE "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);
```

For the `v0.2.0` baseline it contains the two migration IDs in Section 2 and
records `ProductVersion` `10.0.12`. The IDs participate in current-format
recognition; the recorded product version does not.

A fresh EF Core 10.0.12-created notebook also contains
`__EFMigrationsLock`. It is migration coordination state, is not checked by the
format validator, is not authoritative notebook data, and is not an archive
value. Its presence and column layout are not format `1` compatibility
requirements.

Similarly, `sqlite_sequence`, FTS5 shadow-table definitions, raw
`sqlite_schema.sql` formatting, quoting style, root-page numbers, page order,
and journal sidecars are generated or physical implementation details.

## 9. Integrity, validation, and rebuild

Format recognition and derived-data integrity have separate responsibilities:

- Format recognition checks required metadata semantics and the exact applied
  migration ID set. It does not repair or migrate a notebook.
- Read-model integrity compares every projected `Pages` value with `Contents`,
  detects missing, unexpected, duplicate, or mismatched summary rows, requires
  all three synchronization triggers, requires `FtsIndex`, and executes the
  FTS5 integrity command.
- SQLite-level verification used during archive restore also runs
  `PRAGMA integrity_check` and compares an authoritative-data fingerprint.

The rebuild operation runs in one transaction. It deletes and repopulates
`Contents` from `Pages`, executes the FTS5 `rebuild` command, and then repeats
the integrity check. It commits only if the resulting read models are
consistent. A failed post-check rolls back the rebuild; an infrastructure
failure also leaves earlier rebuild writes uncommitted.

Opening or running an ordinary read-only command must not implicitly apply EF
migrations, rebuild derived data, or otherwise modify the notebook. Creation,
explicit migration, archive restore, and explicit read-model maintenance are
separate write operations.

## 10. Archive and migration relationship

The [MemoriaNote archive v1 format specification](../../archive/v1/format.md)
defines a logical backup rather than a copy of SQLite bytes. Notebook and
archive versions are separate axes:

| Version axis | Value for this contract |
| --- | --- |
| Notebook format version | text `1` in `Metadata.Version` |
| Archive format version | integer `1` |
| Archive content model version | integer `1` |
| Archive source notebook format version | text `1` |

Archive v1 reads every `Metadata` row in key order and every listed `Pages`
column in `Rowid` order. It preserves unknown metadata keys, nullable values,
row IDs, and the raw text representation of authoritative fields.

Archive validation is deliberately stricter than the SQLite constraints. For
the supported tuple it requires, among other rules, positive signed 32-bit page
row IDs and ordinals, a non-zero canonical `D`-layout UUID, `IsErased` equal to
`0` or `1`, supported stored timestamp text, and null, empty/whitespace, or
flat-object tag JSON. A SQLite database can therefore satisfy its table
constraints while being rejected as an archive v1 source.

Archive v1 does not serialize `Contents`, `FtsIndex`, FTS5 shadow tables,
triggers, indexes, `sqlite_sequence`, journal settings, or EF migration state.
Restore creates a new current-format database through migrations, inserts the
authoritative rows, rebuilds the derived data, restores synchronization
objects, and validates the completed database before publishing it.

The planned boundary for a future incompatible schema upgrade is likewise a
validated new database, not an in-place mutation of the source format `1`
database. Defining that future target and its loss policy is outside this
specification.

## 11. Versioning policy and conformance baseline

This specification can receive an editorial revision such as `1.1` without
changing the persisted notebook format version. A change to persisted meaning,
required objects, value representation, or compatibility rules requires an
explicit format-version decision. An incompatible format will be documented in
a new version directory while this format `1` specification remains available
for recognition, archive conversion, and migration testing.

Specification revision `1.0` was verified against a notebook created by the
MemoriaNote `v0.2.0` release executable. Format identity, the complete fresh
schema inventory, stored value representations, trigger synchronization, FTS5
integrity and rebuild behavior, and the archive v1 preservation boundary were
reviewed as one conformance baseline.

Supporting implementation traceability and verification evidence are
maintained separately from this public specification. This specification does
not change production behavior, the database schema, or the archive format.
