# Migrate notebook format 1 to format 2

This guide applies to the Memoria Note release that introduces notebook format 2.
Migration creates a new notebook. It does not modify, rename, delete, or select the
source notebook or archive.

## Command

Migrate either a live format-1 `.mnote` database or an archive-v1 content-model-1
file:

```console
mn notebooks migrate "legacy.mnote" --output "migrated.mnote"
mn notebooks migrate "legacy.mnarchive" --output "migrated.mnote"
```

The output is required even for `--dry-run`. The command rejects an existing output,
SQLite sidecar, symbolic link, source/output conflict, and resolution/output conflict.
There is no overwrite or `--force` mode.

Use a dry-run to validate the source and calculate the exact transformation without
creating a database:

```console
mn notebooks migrate "legacy.mnote" \
  --output "migrated.mnote" \
  --dry-run
```

## Report levels

The migration report is written to standard output. Redirect it when a persistent
local report is useful:

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

The default is `warning`. A summary is always written. Routine conversions such as
dropping every legacy page index are aggregated rather than reported once per page.
Complete page bodies and legacy tag values are not copied into diagnostics.

## Duplicate page names

Format 2 requires page names to be unique by case-sensitive SQLite `BINARY`
comparison. Migration resolves duplicate valid names automatically and reports each
rename as a warning.

The first page in stable legacy index, row-ID, and Page-ID order keeps the base name.
Later pages first try the legacy index as a suffix, such as `Memo (2)`. If that name
already exists, migration uses the next unused number. The base is shortened by
Unicode scalar when necessary to keep the final name within 255 scalars.

An explicit rename in a resolution document is applied before automatic duplicate
resolution.

## Page-local errors and partial results

When a complete source record can be read but cannot be represented safely in format
2, only that page is omitted. Examples include:

- an invalid non-duplicate page name;
- legacy tags that cannot produce valid format-2 labels;
- an invalid timestamp or an update time before the creation time;
- an erased page that was not explicitly excluded.

The source remains unchanged, the new output is fully validated, and the command
returns partial exit code `6`. Scripts must treat code `6` as a completed output that
requires review rather than as a complete migration.

Structural source problems stop the operation without publishing an output. These
include an unsupported format, SQLite corruption, archive checksum or framing
failure, missing required notebook metadata, ambiguous Page IDs, and failure to
obtain a consistent source snapshot.

## Optional resolution document

Use `--resolution` to supply Page-ID-keyed decisions:

```console
mn notebooks migrate "legacy.mnote" \
  --output "migrated.mnote" \
  --resolution "migration-resolution.json"
```

Version 1 has this form:

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

A tag override replaces the complete automatically converted label set. Unknown Page
IDs, duplicate decisions, invalid values, an exclusion for a non-erased page, and a
rename or tag override combined with an erased exclusion reject the resolution
document. The machine-readable contract is
[`schema-v1-to-v2-resolution.schema.json`](schema-v1-to-v2-resolution.schema.json).

## Compatibility and rollback

Migration preserves all metadata keys and distinguishes null, empty, and missing
values. It assigns new SQLite-local internal IDs while preserving stable Page IDs.
Derived tables and search indexes are rebuilt from migrated authoritative values.

Keep the original format-1 notebook or CM1 archive until the new notebook has been
reviewed. Rollback means using that original source with the older application; it is
not an in-place down migration. Migration never changes the workspace's current
notebook selection.
