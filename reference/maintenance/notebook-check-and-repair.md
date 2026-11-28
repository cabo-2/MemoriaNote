# Notebook check and repair

This guide documents the public notebook maintenance contract introduced in Memoria Note
`v0.4.0` (maintenance contract revision `1`). It explains how to inspect a format `2` notebook,
decide whether it can be repaired safely, interpret reports and exit status, and start using a
repaired notebook.

For concise command syntax and general CLI conventions, see the
[user guide](../user-guide.md). For the format identity and authoritative/derived-data boundary,
see the [notebook format 2 specification](../notebook/v2/schema.md).

## Choose the correct operation

`notebooks check` and `notebooks repair` operate only on current format `2` notebooks. They do not
migrate format `1`, downgrade an unknown future format, restore an archive, replace a source, or
change workspace selection.

| Input or goal | Operation |
| --- | --- |
| Inspect a format `2` notebook | `mn notebooks check <source>` |
| Rebuild safely readable format `2` data | `mn notebooks repair <source> --output <notebook>` |
| Convert a format `1` notebook or CM1 archive | `mn notebooks migrate` |
| Create a notebook from a CM2 archive | `mn notebooks restore` |

Both maintenance commands accept file paths resolved from the process current directory. They do
not interpret a source as a workspace notebook name or read or change workspace selection.

## Check a notebook

Run check before attempting repair:

```console
mn notebooks check "work.mnote"
```

Check evaluates these layers in order:

1. `identity` — Memoria Note application ID and notebook format version;
2. `schema` — tables, columns, constraints, indexes, and foreign keys;
3. `triggers` — required trigger definitions and behavior;
4. `integrity` — SQLite integrity and foreign keys;
5. `authoritative-values` — metadata and page value contracts;
6. `derived-data` — the exact `PageTags` projection of authoritative tags.

A layer that cannot be evaluated safely because an earlier layer failed is reported as `not
evaluated`, not as an additional failure. A completed layer is `passed` or `failed`.

### Check issue codes

Completed reports use the following stable issue codes. A location, expected value, and safe
actual summary may accompany a code; reports do not include private page bodies or complete
metadata values.

| Code | Layer | Meaning |
| --- | --- | --- |
| `identity-application-id-mismatch` | identity | SQLite application ID differs from format `2` |
| `identity-user-version-mismatch` | identity | SQLite user version differs from format `2` |
| `schema-object-mismatch` | schema | A non-trigger object differs from the release contract |
| `schema-object-missing` | schema | A required non-trigger object is absent |
| `schema-object-unexpected` | schema | An unexpected non-trigger object is present |
| `trigger-missing` | triggers | A required trigger is absent |
| `trigger-mismatch` | triggers | A trigger differs from the release contract |
| `sqlite-integrity-error` | integrity | SQLite reports an integrity problem |
| `foreign-key-violation` | integrity | SQLite reports a foreign-key violation |
| `metadata-missing` | authoritative-values | A required metadata key is absent |
| `metadata-duplicate` | authoritative-values | A metadata key occurs more than once |
| `metadata-invalid` | authoritative-values | Stored metadata violates the value contract |
| `metadata-retired` | authoritative-values | A retired metadata key is present |
| `page-id-invalid` | authoritative-values | A Page ID violates the value contract |
| `page-id-duplicate` | authoritative-values | Stable Page IDs are not unique |
| `page-name-duplicate` | authoritative-values | Page names are not unique |
| `page-name-invalid` | authoritative-values | A page name violates the value contract |
| `page-timestamp-invalid` | authoritative-values | A page timestamp violates the value contract |
| `page-revision-invalid` | authoritative-values | A page revision violates the value contract |
| `page-tags-invalid` | authoritative-values | Authoritative page tags violate the value contract |
| `derived-page-tags-missing` | derived-data | Required `PageTags` rows are absent |
| `derived-page-tags-unexpected` | derived-data | `PageTags` contains rows not in authoritative tags |

Check uses a read-only SQLite snapshot and leaves the main database and its WAL, SHM, and journal
file set unchanged on success, issue, failure, or cancellation. A completed report states whether
the notebook is valid, gives every layer's status, confirms `Source modified: no`, and reports the
issue count. An issue report suggests repair as a possible next action but does not promise that
the stricter repairability preflight will accept the source.

Missing files, invalid paths, format `1`, unsupported future formats, unreadable input, busy
snapshots, and access or storage failures are target-resolution errors rather than check issues. A
format `1` source receives migration guidance. An unknown future format requires a compatible
newer release. Unreadable or corrupt input receives backup or archive recovery guidance instead
of an unsafe repair recommendation.

## Repair a notebook

Repair requires a new output path:

```console
mn notebooks repair "work.mnote" --output "work-repaired.mnote"
```

Repair can accept a current format `2` notebook whose defect is limited to derived data or other
release-defined rebuildable objects. It refuses to copy when the authoritative schema cannot be
read exactly, SQLite integrity of authoritative storage cannot be established, or an authoritative
value violates the release contract.

### Repair refusal codes

| Code | Meaning |
| --- | --- |
| `authoritative-schema-mismatch` | A required authoritative table or column cannot be read exactly |
| `authoritative-integrity-failure` | Integrity of authoritative storage cannot be established |
| `authoritative-value-invalid` | One or more authoritative values violate the release contract |

Repair copies every required and extension metadata value and every logical page value. It does
not infer missing values, normalize text, skip records, or copy SQLite-local `InternalId` values.
The new notebook receives local internal IDs while preserving stable Page IDs.

The normalized output path must differ from the source and must not already exist or be a symbolic
link. There is no overwrite or `--force` mode. The output is first built at a private path in the
destination directory. Schema, integrity, values, derived data, logical equivalence, and
close/reopen behavior are validated before atomic publication to the requested path.

Failure or cancellation removes the private output and its owned sidecars and never publishes a
partial result. The source and any pre-existing destination remain unchanged. A successful report
includes record counts, copied authoritative categories, rebuilt derived categories, a logical
fingerprint, output validation, and publication status without exposing private page bodies or
metadata values.

## Concurrency and file safety

Check and repair wait up to 30 seconds for the read locks needed for a consistent snapshot. If a
writer prevents a safe snapshot, close it and retry; busy is not reported as corruption. Repair
holds one consistent authoritative snapshot across preflight and copy.

Keep the source database and any existing WAL, SHM, or journal sidecars together when backing up or
moving it. Do not delete an active sidecar or a non-empty WAL individually. The
[schema v1 to v2 migration guide](../migration/schema-v1-to-v2.md#sqlite-sidecars-and-manual-maintenance)
documents the separate manual-maintenance procedure for a legacy migration source.

## Reports, streams, and exit status

Maintenance contract revision `1` provides human-readable reports only. It does not provide JSON
or another versioned machine-readable report schema. Stable issue/refusal codes, exit-status
classification, and stream placement are the automation contract; scripts must not parse report
labels, prose, ordering, or whitespace.

| Result | Exit status | Output |
| --- | ---: | --- |
| Valid check or published repair | `0` | Human-readable report on standard output |
| Check issues or repair refusal | `2` | Complete report and next-action guidance on standard output |
| Invalid path, legacy/unsupported/unreadable source | `2` | Fatal diagnostic on standard error |
| Missing source | `3` | Fatal diagnostic on standard error |
| Busy snapshot or output conflict | `4` | Fatal diagnostic on standard error |
| Access, storage, or output-verification failure | `5` | Fatal diagnostic on standard error |
| Cancellation | `130` | Cancellation diagnostic on standard error |

Maintenance never uses partial exit status `6`.

## Decide the next action

| Result | Safe next action |
| --- | --- |
| Check passes | Continue using the source; keep normal backups |
| Only rebuildable defects are reported | Preserve the source and sidecars, repair to a new path, then check the output |
| Repair refuses an authoritative defect | Preserve the source and recover from an independent backup or CM2 archive |
| Source is format `1` | Follow the [migration guide](../migration/schema-v1-to-v2.md) |
| Source is a future format | Use a compatible newer Memoria Note release |
| Source is busy | Stop readers/writers that hold the snapshot, then retry |
| Source is unreadable or corrupt | Preserve it and recover from an independent backup or CM2 archive |

Do not replace or delete the source until a repaired output has been checked, reviewed, and backed
up independently.

## Use a repaired notebook

Repair does not register or select its output. To use it, place the new file directly in the
workspace root and select it explicitly:

```console
mn notebooks check "work-repaired.mnote"
mn notebooks list --long
mn use work-repaired
mn status
```

Keep the original and an independent backup during review. Source replacement and automatic
workspace registration are not maintenance operations in this release.
