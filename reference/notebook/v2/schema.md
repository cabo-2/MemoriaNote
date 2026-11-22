# MemoriaNote notebook database schema v2

- notebook format version: `2`
- specification revision: `1`
- release baseline: Memoria Note `v0.3.0`
- release source commit: finalized after ECLI-1180 is merged
- canonical DDL SHA-256: `070a59ea2ce61f4cf128124014bc7cac9871bcf881833bce401bdb8ae120d97e`
- last updated: 2026-11-22

## 1. Purpose and compatibility promise

A Memoria Note format `2` notebook is one SQLite database, conventionally
stored in a `.mnote` file. This document specifies its persisted format,
authoritative values, derived data, and recognition contract.

The notebook format, this prose specification revision, and the Memoria Note
application version are independent. Specification revision `1` is not stored
in the database. A correction that does not change persisted meaning may update
this document without changing the notebook format. A change to identity, DDL,
required values, or their meaning requires an explicit format-version decision.

Format `1` remains defined by the
[schema v1 specification](../v1/schema.md). Memoria Note `v0.3.0` does not open
format `1` for ordinary commands; it accepts it only as an explicit
[migration source](../../migration/schema-v1-to-v2.md).

## 2. Format identity and SQLite requirements

Format `2` is identified by both values:

| Signal | Required value |
| --- | ---: |
| `PRAGMA application_id` | `1782832723` (`0x6A43DE53`) |
| `PRAGMA user_version` | `2` |

`Metadata.Version`, a file extension, an arbitrary SQLite header, or the
presence of similarly named tables does not identify format `2`. `Version` is
a retired metadata key and is rejected.

The application ID is the first big-endian 32-bit word of SHA-256 over the
UTF-8 bytes of `mn`, with the sign bit clear. It is fixed for this format; an
implementation must use the numeric value above rather than recomputing it.

The schema requires SQLite support for foreign keys, triggers, `WITHOUT
ROWID`, JSON functions including `json_valid`, `json_type`, and `json_each`,
and the identity pragmas above. The official self-contained release bundles
its SQLite runtime. Foreign-key enforcement is enabled by the application for
every write connection. The format does not require FTS5 and contains no FTS
table, shadow table, synchronization trigger, or persisted body-search index.

Recognition and ordinary opening are read-only with respect to the file. They
do not apply migrations, repair data, rebuild derived objects, or rewrite
values. An identity mismatch, DDL mismatch, SQLite integrity failure, invalid
authoritative value, or derived-data mismatch prevents an ordinary open.

## 3. Data ownership

| Classification | Objects or values | Contract |
| --- | --- | --- |
| Authoritative | `Metadata` | Required managed properties and unknown extension metadata |
| Authoritative | `Pages` | Stable Page ID, name, timestamps, revision, canonical tags, and body |
| Derived | `PageTags` | Rebuildable tag-label projection of `Pages.Tags` |
| Synchronization | `Pages_TagsInsert`, `Pages_TagsUpdate` | Maintains `PageTags` after page writes |
| Integrity guard | `Pages_RejectInternalIdUpdate`, `Pages_RejectPageIdUpdate` | Rejects identity changes |
| SQLite-local | `Pages.InternalId`, `sqlite_sequence`, indexes and physical layout | Local implementation state, not portable identity |

Only `Metadata` and the logical values in `Pages` are portable notebook data.
`PageTags` is required for a valid normal-open database but can be recreated
from `Pages.Tags`. Body search in `v0.3.0` scans authoritative `Pages.Text`; it
does not introduce a derived FTS object.

## 4. Canonical DDL

The following UTF-8 text, including its final LF, is the fixed DDL embedded in
the `v0.3.0` release candidate. Its SHA-256 is recorded in the header.

```sql
PRAGMA application_id = 1782832723;
PRAGMA user_version = 2;

CREATE TABLE "Metadata" (
    "Key" TEXT NOT NULL CONSTRAINT "PK_Metadata" PRIMARY KEY,
    "Value" TEXT NULL
);

CREATE TABLE "Pages" (
    "InternalId" INTEGER NOT NULL CONSTRAINT "PK_Pages" PRIMARY KEY AUTOINCREMENT,
    "PageId" TEXT NOT NULL COLLATE BINARY,
    "Name" TEXT NOT NULL COLLATE BINARY,
    "CreateTime" TEXT NOT NULL,
    "UpdateTime" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    "Tags" TEXT NOT NULL,
    "Text" TEXT NOT NULL,
    CONSTRAINT "CK_Pages_InternalId_Positive" CHECK (InternalId > 0),
    CONSTRAINT "CK_Pages_PageId_Canonical" CHECK (
        length(PageId) = 36
        AND PageId = lower(PageId)
        AND substr(PageId, 9, 1) = '-'
        AND substr(PageId, 14, 1) = '-'
        AND substr(PageId, 19, 1) = '-'
        AND substr(PageId, 24, 1) = '-'
        AND replace(PageId, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND PageId <> '00000000-0000-0000-0000-000000000000'
    ),
    CONSTRAINT "CK_Pages_Name_Length" CHECK (length(Name) BETWEEN 1 AND 255),
    CONSTRAINT "CK_Pages_Name_OuterSpace" CHECK (Name = trim(Name)),
    CONSTRAINT "CK_Pages_CreateTime_Canonical" CHECK (
        length(CreateTime) = 27
        AND substr(CreateTime, 5, 1) = '-'
        AND substr(CreateTime, 8, 1) = '-'
        AND substr(CreateTime, 11, 1) = ' '
        AND substr(CreateTime, 14, 1) = ':'
        AND substr(CreateTime, 17, 1) = ':'
        AND substr(CreateTime, 20, 1) = '.'
        AND replace(replace(replace(replace(CreateTime, '-', ''), ' ', ''), ':', ''), '.', '')
            NOT GLOB '*[^0-9]*'
    ),
    CONSTRAINT "CK_Pages_UpdateTime_Canonical" CHECK (
        length(UpdateTime) = 27
        AND substr(UpdateTime, 5, 1) = '-'
        AND substr(UpdateTime, 8, 1) = '-'
        AND substr(UpdateTime, 11, 1) = ' '
        AND substr(UpdateTime, 14, 1) = ':'
        AND substr(UpdateTime, 17, 1) = ':'
        AND substr(UpdateTime, 20, 1) = '.'
        AND replace(replace(replace(replace(UpdateTime, '-', ''), ' ', ''), ':', ''), '.', '')
            NOT GLOB '*[^0-9]*'
    ),
    CONSTRAINT "CK_Pages_Revision_Positive" CHECK (Revision > 0),
    CONSTRAINT "CK_Pages_Tags_JsonArray" CHECK (
        json_valid(Tags)
        AND json_type(Tags) = 'array'
    )
);

CREATE UNIQUE INDEX "IX_Pages_PageId"
    ON "Pages" ("PageId" COLLATE BINARY);

CREATE UNIQUE INDEX "IX_Pages_Name"
    ON "Pages" ("Name" COLLATE BINARY);

CREATE TABLE "PageTags" (
    "PageInternalId" INTEGER NOT NULL,
    "Label" TEXT NOT NULL COLLATE BINARY,
    CONSTRAINT "PK_PageTags" PRIMARY KEY ("PageInternalId", "Label"),
    CONSTRAINT "FK_PageTags_Pages_PageInternalId" FOREIGN KEY ("PageInternalId")
        REFERENCES "Pages" ("InternalId") ON DELETE CASCADE,
    CONSTRAINT "CK_PageTags_Label_Length" CHECK (length(Label) BETWEEN 1 AND 64),
    CONSTRAINT "CK_PageTags_Label_OuterSpace" CHECK (Label = trim(Label))
) WITHOUT ROWID;

CREATE INDEX "IX_PageTags_Label_PageInternalId"
    ON "PageTags" ("Label" COLLATE BINARY, "PageInternalId");

CREATE TRIGGER "Pages_TagsInsert"
AFTER INSERT ON "Pages"
BEGIN
    INSERT INTO "PageTags"("PageInternalId", "Label")
        SELECT new.InternalId, value
        FROM json_each(new.Tags);
END;

CREATE TRIGGER "Pages_TagsUpdate"
AFTER UPDATE OF Tags ON "Pages"
WHEN old.Tags IS NOT new.Tags
BEGIN
    DELETE FROM "PageTags"
        WHERE "PageInternalId" = old.InternalId;
    INSERT INTO "PageTags"("PageInternalId", "Label")
        SELECT new.InternalId, value
        FROM json_each(new.Tags);
END;

CREATE TRIGGER "Pages_RejectInternalIdUpdate"
BEFORE UPDATE OF InternalId ON "Pages"
WHEN old.InternalId IS NOT new.InternalId
BEGIN
    SELECT RAISE(ABORT, 'Pages.InternalId is immutable');
END;

CREATE TRIGGER "Pages_RejectPageIdUpdate"
BEFORE UPDATE OF PageId ON "Pages"
WHEN old.PageId IS NOT new.PageId
BEGIN
    SELECT RAISE(ABORT, 'Pages.PageId is immutable');
END;
```

SQLite-generated SQL formatting, root pages, auto-index names, file page order,
journal mode, and sidecar files are not portable values. The semantic schema
inventory, constraints, collation, foreign keys, indexes, and trigger behavior
are required even where SQLite normalizes stored SQL text.

## 5. Metadata contract

`Metadata.Key` is non-null, unique text using SQLite `BINARY` comparison.
`Metadata.Value` is nullable text. Keys and values are not normalized.

Exactly one row for each application-managed key is required:

| Key | Value contract | New-notebook default |
| --- | --- | --- |
| `Author` | Non-null text | Empty string |
| `CreateTime` | Null, or valid `yyyyMMddhhmmss` text | UTC creation time in that form |
| `Description` | Non-null text | Empty string |
| `ReadOnly` | Exact ordinal `True` or `False` | `False` |
| `Tag` | Non-null text | Empty string |
| `Title` | Non-null text | Requested notebook title |

The metadata timestamp has no timezone marker and uses a 12-hour `hh` field.
Its stored value is preserved without inferring AM, PM, or an offset.

`Name` and `Version` are retired reserved keys and are rejected. Other keys are
authoritative extension metadata. Their case-sensitive key and null, empty, or
non-empty value are preserved losslessly by native CM2 backup and restore. An
extension key does not change format identity and must not shadow a managed key.

## 6. Page value contract

| Column | Contract |
| --- | --- |
| `InternalId` | Positive signed 64-bit SQLite-local identity; immutable |
| `PageId` | Non-zero lowercase UUID in exact `D` layout; unique; immutable |
| `Name` | Unique ordinal name; 1-255 Unicode scalars; no control or outer-whitespace characters |
| `CreateTime` | Valid canonical `yyyy-MM-dd HH:mm:ss.fffffff` timestamp |
| `UpdateTime` | Same canonical representation as `CreateTime` |
| `Revision` | Positive signed 64-bit optimistic-concurrency revision |
| `Tags` | Canonical compact JSON string array |
| `Text` | Non-null body text; empty text and Unicode are preserved |

`PageId` is the stable public identity used by command output, archive CM2, and
targeting with `--id`. `InternalId` may change after restore and is not exposed
as a portable identity.

Names are compared by ordinal SQLite `BINARY` value. They are not trimmed,
case-folded, or Unicode-normalized. Validation rejects invalid Unicode and
Unicode control-category scalars in addition to the SQL checks.

Page timestamps contain exactly seven fractional-second digits and no timezone
suffix. The application interprets canonical values as UTC but preserves their
exact storage text through CM2 backup and restore. Calendar and clock validity
are checked beyond the lexical SQL constraint.

Tags are sorted by UTF-8 byte sequence, unique by ordinal comparison, and
encoded as a compact JSON array with no optional whitespace. Each label has
1-64 Unicode scalars, no control characters, and no leading or trailing
whitespace. No tags are represented by exact text `[]`. Logically equivalent
but byte-different JSON is non-canonical and invalid.

## 7. Derived data and search

`PageTags` contains one `(PageInternalId, Label)` row for every decoded label
in `Pages.Tags`. It has no independent authoritative value. Integrity requires
an exact projection: no missing, unexpected, duplicate, or mismatched row. A
rebuild deletes its rows and repopulates them from `Pages.Tags` in one
transaction, then validates the result before commit.

The two tag triggers maintain this projection after insert and after a changed
`Tags` value. Page deletion cascades through the foreign key. The identity
guard triggers reject direct changes to `InternalId` and `PageId`.

The release contains no body-search index. `find` uses a bounded Scan workflow
over `Pages.Text`. Search result order, limits, case behavior, and snippets are
application contracts documented in the [user guide](../../user-guide.md), not
additional persisted schema objects.

## 8. Portable and local boundaries

Native archive CM2 preserves every metadata key/value and these logical page
values: `PageId`, `Name`, both timestamps, `Revision`, `Tags`, and `Text`.
CM2 records the source `InternalId` for deterministic input validation but does
not assign it to a restored target. It does not serialize `PageTags`, triggers,
indexes, `sqlite_sequence`, journal files, or SQLite file layout.

Backup reads one consistent snapshot. Restore creates a new private format `2`
database, inserts authoritative values, rebuilds derived data, validates schema,
SQLite integrity, values, and logical equivalence, closes and reopens the file,
and only then publishes it. See the
[archive v1 specification](../../archive/v1/README.md).

## 9. Validation boundary

The release validator checks, in layers:

1. the two persisted identity values;
2. schema objects, columns, constraints, indexes, foreign keys, and triggers;
3. `PRAGMA integrity_check` and `PRAGMA foreign_key_check`;
4. required and retired metadata, page value rules, and uniqueness;
5. the exact `PageTags` projection.

Ordinary opening performs validation but never repair. The `v0.3.0` release
does not publish `notebooks check` or `notebooks repair`; those commands and a
public maintenance contract are follow-up work. Migration, native restore, and
release rollback are the recovery operations documented for this release.

## 10. Provenance

Schema v2 originated from SchemaLab handoff manifest version `1`, freeze
commit `5da289582ccf475dfe4c1d7e6dcbf3bb6b4ec4a7`. The initial Memoria Note
baseline was commit `3ddede8a6240a6abe91f3f431f84a2764ed2faa6`.
ECLI-1120a recorded the dual-index amendment, ECLI-1145a amended metadata, and
ECLI-1180 selected the Scan-only release schema and removed both candidate FTS
indexes.

The canonical DDL hash above is the ECLI-1180 release-candidate value. The
final ECLI-1180 squash commit is recorded here after that PR is merged and
before ECLI-1190 is merged. Supporting implementation resources and tests are
not public format extensions; they provide traceability to this contract.
