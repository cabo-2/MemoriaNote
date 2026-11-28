# Migrate notebook format 1 to format 2

This guide applies to Memoria Note `v0.4.0`. Migration creates a new notebook.
It never changes authoritative source content or main-database bytes and never renames, deletes,
or selects the source notebook or archive. A live SQLite source can acquire runtime sidecars as
described below.
Format `1` is not opened by ordinary `v0.4.0` commands.

## 1. Before migrating

Keep an independent backup of the source and verify that it can still be read
by the older application. Supported inputs are:

- a live notebook format `1` `.mnote` database; or
- archive format `1`, content model `1`, source notebook format `1`.

An archive without a manifest may receive legacy recovery guidance, but it is
not CM1 and is not accepted by this command. CM2 is already native format `2`
data and is restored with `notebooks restore`, not migrated.

A format `2` notebook is also not a migration input. Supplying one returns exit status `2`, creates
no output, and directs you to its complete read-only validation instead:

```console
mn notebooks check "current.mnote"
```

Migration performs only the identity check needed to classify this input; it does not run the
schema, integrity, authoritative-value, or derived-data checks owned by `notebooks check`.

Migration requires a separate output path:

```console
mn notebooks migrate "legacy.mnote" --output "migrated.mnote"
mn notebooks migrate "legacy.mnarchive" --output "migrated.mnote"
```

`--output` is required even for `--dry-run`. The command rejects an existing
output or SQLite sidecar, a symbolic-link output, a source/output conflict, and
a resolution/output conflict. There is no overwrite or `--force` mode.

### SQLite sidecars and manual maintenance

A live SQLite notebook is a file set. In addition to `legacy.mnote`, it can include
`legacy.mnote-wal`, `legacy.mnote-shm`, or `legacy.mnote-journal`. Keep the source database and
every existing sidecar together when copying, backing up, restoring, or diagnosing the source.

Migration reads a live format `1` database through a normal read-only SQLite snapshot so that it
sees committed data in an existing WAL. It does not use an immutable main-file-only view. When a
WAL-mode source has no sidecars at open time, SQLite can create `-wal` and `-shm` runtime files and
can leave an empty WAL after the last read-only connection closes. This does not change the
source's authoritative content or main-database bytes, but it means the source file set is not
guaranteed to have identical entries before and after migration.

`mn notebooks migrate` never deletes source sidecars. It cannot safely distinguish a sidecar it
observed or caused from one owned by a concurrent writer or an interrupted earlier process. Never
delete a sidecar individually while any reader or writer is running, and never delete a non-empty
WAL: it may contain committed data not yet checkpointed into the main database.

If sidecar cleanup is operationally necessary:

1. Make an independent backup of the complete source file set.
2. Verify that the backup can be read by an application compatible with schema v1.
3. Stop every reader, writer, backup, migration, and synchronization process using the source.
4. Open the source read-write with that schema v1-compatible application and close it cleanly.
5. Let SQLite perform its own checkpoint and sidecar cleanup; do not substitute manual deletion.
6. Verify the source remains readable before retrying migration.

This procedure belongs to the compatible schema v1 application and SQLite lifecycle. Migration is
not a checkpoint or cleanup command, and `notebooks check` applies only to format `2` notebooks.

## 2. Dry-run and report

A dry-run validates the complete source and calculates the transformation
without creating a database:

```console
mn notebooks migrate "legacy.mnote" \
  --output "migrated.mnote" \
  --dry-run
```

The human-readable report is written to standard output. Redirect it when a
persistent local record is useful:

```console
mn notebooks migrate "legacy.mnote" \
  --output "migrated.mnote" \
  --report-level info > migration-report.txt
```

`--report-level` accepts:

| Level | Entries |
| --- | --- |
| `error` | Pages that could not be migrated |
| `warning` | Errors plus user-visible or lossy conversions |
| `info` | Errors, warnings, and expected aggregate conversions |

The default is `warning`. A summary is always written. Routine conversions,
such as dropping all legacy page indexes, are aggregated. Page bodies, legacy
tag values, and unknown metadata values are not copied into diagnostics.

The report is intended for people and is not a versioned JSON interface. Its
issue codes and counts make review repeatable, but scripts must use exit status
rather than parse formatting or prose.

## 3. Metadata transformation

Migration uses an allowlist instead of copying all format `1` metadata:

| Format 1 input | Format 2 result |
| --- | --- |
| `Title` null | Empty `Title` |
| `Description`, `Author`, or `Tag` missing/null | Empty text |
| `ReadOnly` missing/null | `False` |
| `CreateTime` missing/null | Null |
| Valid `CreateTime` | Preserved without timezone inference |
| `Name`, `Version` | Validated as source contract, then dropped |
| Other keys | Dropped with one key-name/count warning |

Unknown legacy metadata values are never printed. Format `2` does allow
extension metadata, and native CM2 backup and restore preserve it, but legacy
unknown metadata is deliberately not promoted into that namespace.

## 4. Page transformation

Migration preserves stable Page IDs and bodies, allocates new SQLite-local
internal IDs, converts timestamps to the canonical seven-fraction form, and
converts valid legacy tags to canonical format `2` labels. Legacy page index,
content type, and erased-state fields do not exist in format `2`.

Format `2` page names are unique using case-sensitive SQLite `BINARY`
comparison. For duplicate valid names, the first page in stable legacy index,
row-ID, and Page-ID order keeps the base name. Later pages first try the legacy
index as a suffix, such as `Memo (2)`, then the next unused number. The base is
shortened by Unicode scalar when required to stay within 255 scalars. Each
automatic rename is a warning.

When a complete source row can be read but cannot be represented safely, only
that page is omitted. Examples include an invalid non-duplicate name, tags
that cannot produce valid labels, an invalid timestamp, an update before the
creation time, or an erased page without an explicit exclusion.

Page-local diagnostics identify Page ID or source row and a stable reason
without copying the rejected private value. For example, an overlong name uses
`reason="scalar-limit-exceeded"` with actual and limit counts.

## 5. Resolution document

Use `--resolution` for Page-ID-keyed decisions:

```console
mn notebooks migrate "legacy.mnote" \
  --output "migrated.mnote" \
  --resolution "migration-resolution.json"
```

Resolution document version `1` has this shape:

```json
{
  "version": 1,
  "renames": [
    {
      "pageId": "11111111-1111-4111-8111-111111111111",
      "name": "Recovered page"
    }
  ],
  "tagOverrides": [
    {
      "pageId": "11111111-1111-4111-8111-111111111111",
      "labels": ["legacy", "review"]
    }
  ],
  "erasedPageExclusions": [
    "22222222-2222-4222-8222-222222222222"
  ]
}
```

A tag override replaces the complete automatically converted label set. An
invalid document, unknown or duplicate decision, inapplicable Page ID,
non-erased exclusion, or rename/tag override combined with an erased exclusion
rejects the complete resolution document. Rename and tag values are then
checked against format `2`; an invalid value is a page-local error.

The structural contract is
[`schema-v1-to-v2-resolution.schema.json`](schema-v1-to-v2-resolution.schema.json).
Applicability and format `2` value validation remain command responsibilities.

## 6. Publication and exit status

A result with page-local errors is still created only after complete format `2`
validation and close/reopen verification. It omits the affected pages, writes
the report and summary to standard output, and exits with status `6`. Treat
status `6` as a validated partial output requiring review, not as complete
migration.

Fatal source, resolution, or destination failures publish no output:

| Status | Typical migration result |
| ---: | --- |
| `0` | Complete output, or successful dry-run |
| `2` | Invalid/unsupported source, invalid resolution, or invalid option |
| `3` | Source or resolution file not found |
| `4` | Destination/path conflict or busy source snapshot |
| `5` | I/O failure or generated-notebook integrity failure |
| `6` | Validated output completed with omitted page errors |
| `130` | Process cancellation |

Migration writes to private temporary storage. Fatal failure or cancellation
removes the pending database and its journal, WAL, or shared-memory sidecars.
The source and any pre-existing destination remain unchanged. After correcting
an input or resolution problem, rerun with the same command; no incomplete
published output must be cleaned up or resumed.

## 7. Workspace selection and review

Migration never changes `mn-workspace.toml`. To use the result, place the
output directly in the workspace root, review the report, and select it
explicitly:

```console
mn notebooks list --long
mn use migrated
mn status
```

Review the migrated page count, warnings, and any omitted pages before making
format `2` the working copy. Use the Page IDs printed by `ls --long` or `find`
to inspect pages with `cat --id` or `edit --id`.

## 8. Rollback and divergence

Keep the original format `1` notebook or CM1 archive until the new notebook is
verified and the rollback window has ended. Rollback means selecting the
unchanged original source with an older compatible application. It is not an
in-place down migration, and CM2 is not readable by CM1-only applications.

The old and new notebooks are independent after migration. Changes made in
one are not synchronized to the other. If both are edited, their data diverges
and rerunning migration from the old source does not merge changes from the
existing format `2` notebook.

See the [schema v1 specification](../notebook/v1/schema.md),
[schema v2 specification](../notebook/v2/schema.md), and
[archive v1 compatibility matrix](../archive/v1/README.md).
