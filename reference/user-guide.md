# Memoria Note user guide

This guide documents the public command-line interface for Memoria Note `v0.4.0`. Use
`mn --help` and `mn <command> --help` for concise syntax at the terminal. This guide is the
maintained reference for installation, workflows, operations, recovery, and process behavior.

## Name

`mn` — a lightweight command-line notepad backed by SQLite notebook files.

## Synopsis

```text
mn [--workspace <directory>] [--version] [--help]
mn <command> [arguments] [options]
mn <command> --help
```

Running `mn` without a command is equivalent to `mn status`.

## Description

Memoria Note organizes data using three concepts:

- A **workspace** is one existing directory. Live `.mnote` notebook files and the optional
  `mn-workspace.toml` selection file are stored directly in that directory. Subdirectories are not
  searched for notebooks.
- A **notebook** is one SQLite `.mnote` file. A command can use the notebook selected in
  `mn-workspace.toml` or an invocation-only `--notebook` override.
- A **page** is a named text record inside a notebook. Each page also has an immutable Page ID that
  can disambiguate legacy pages with duplicate names.

The current directory is the workspace unless `--workspace <directory>` is supplied. Notebook
inputs are leaf file names, not paths. The lowercase `.mnote` suffix is optional and is added when
omitted. Creating a notebook does not select it, and the CLI never selects the only notebook
implicitly.

## Installation, update, and uninstall

### Supported release artifacts

The release provides two self-contained archives:

| Platform | Asset |
| --- | --- |
| 64-bit Linux | `mn-v<version>-linux-x64.tar.gz` |
| 64-bit Windows | `mn-v<version>-win-x64.zip` |

Download the appropriate archive and `SHA256SUMS` from
[GitHub Releases](https://github.com/cabo-2/MemoriaNote/releases). A separate .NET runtime is not
required. The archive root contains the executable, managed assemblies, native runtime libraries,
and `LICENSE`; keep the directory intact.

macOS, Arm, framework-dependent, single-file, Native AOT, trimmed, ReadyToRun, installer, and
package-manager distributions are not provided.

### Verify a download

On Linux, place the archive and `SHA256SUMS` in the same directory and run:

```bash
sha256sum --ignore-missing --check SHA256SUMS
```

Confirm that exactly the downloaded release archive reports `OK` and that its file name contains
the expected tag and RID.

On Windows, calculate the archive digest and compare it with the matching line in `SHA256SUMS`:

```powershell
Get-FileHash .\mn-v0.4.0-win-x64.zip -Algorithm SHA256
Select-String "mn-v0.4.0-win-x64.zip" .\SHA256SUMS
```

The hexadecimal values must be equal, ignoring letter case.

### Linux installation

Install each release beside earlier versions and expose it through a symbolic link. For `v0.4.0`:

```bash
tar -xzf mn-v0.4.0-linux-x64.tar.gz
mkdir -p "$HOME/.local/lib/memoria-note" "$HOME/.local/bin"
mv mn-v0.4.0-linux-x64 "$HOME/.local/lib/memoria-note/0.4.0"
ln -s "$HOME/.local/lib/memoria-note/0.4.0/mn" "$HOME/.local/bin/mn"
mn --version
```

Add `$HOME/.local/bin` to `PATH` if the login environment does not already include it. Do not copy
only the `mn` executable out of its installed directory.

### Windows installation

Extract the ZIP, then move or rename its complete root directory to a versioned location such as:

```text
%LOCALAPPDATA%\Programs\MemoriaNote\0.4.0
```

Add that version directory—not just `mn.exe`—to the user `PATH`. Open a new terminal and verify the
installed version:

```powershell
mn.exe --version
```

### Update and rollback

Verify and install a new version beside the old version before switching to it.

- On Linux, replace `$HOME/.local/bin/mn` with a symbolic link to the new version only after the new
  executable passes `mn --version`.
- On Windows, replace the old Memoria Note directory in the user `PATH` with the new version
  directory, then open a new terminal and verify it.
- Regenerate zsh completion after switching the executable.

For an application rollback, point the Linux link or Windows `PATH` entry back to the previous
intact version. Installing, updating, rolling back, or removing the application does not alter
workspaces, notebook files, workspace selection, or user configuration.

Before moving from a format `1` release to a format `2` release, keep the original notebook or a
CM1 archive; migration creates a separate format `2` notebook. For later updates, create a native CM2
backup with the currently working release. Keep the previous application and compatible backup
until the new release has opened and backed up the data successfully.

### Uninstall

Remove the Linux symbolic link or the Windows user `PATH` entry, then remove the installed version
directories. Remove the generated `_mn` completion file if it was installed. Do not delete
`.mnote` files, `mn-workspace.toml`, or the application-data directory unless the associated user
data is intentionally being removed.

## Quick start

Create a workspace and notebook, select it, and configure an editor:

```bash
mkdir notes
cd notes
mn create work
mn use work
mn config editor setup
```

Create and use a page:

```bash
mn new meeting-notes
mn ls
mn find meeting
mn cat meeting-notes
mn edit meeting-notes
mn rename meeting-notes project-notes
```

Preview a deletion, then confirm interactively or use `--force` in a non-interactive workflow:

```bash
mn delete project-notes --dry-run
mn delete project-notes
mn delete project-notes --force
```

Return to the virtual workspace root without deleting anything:

```bash
mn use --root
```

## Target resolution

### Workspace selection

Workspace-aware commands use the process current directory by default. The inherited
`--workspace <directory>` option selects a different existing directory for that invocation only:

```bash
mn --workspace /path/to/notes status
mn --workspace /path/to/notes ls --notebook work
```

The `config` and `completion` command groups do not use a workspace.

### Notebook selection

Commands that operate on one notebook resolve their target in this order:

1. `--notebook <notebook>` for that invocation;
2. the selection stored by `mn use <notebook>`;
3. no target, which is reported instead of selecting a notebook implicitly.

An invocation override does not read, repair, or change `mn-workspace.toml`. A missing, invalid, or
unsupported saved notebook is reported without falling back to another notebook. `mn use --root`
removes the saved selection; it does not remove notebook files.

Notebook file symbolic links are supported and their workspace leaf name is stored. The
`mn-workspace.toml` file itself must not be a symbolic link. Local filesystems are recommended
because SQLite locking and synchronized writes depend on filesystem behavior.

### Page selection

`edit`, `cat`, `rename`, and `delete` select a page by exact name or by `--id`, but never both. A
Page ID can be a complete UUID or a prefix of 4 to 32 hexadecimal characters:

```bash
mn cat --id 744ffba0-0000-0000-0000-000000000000
mn edit --id 744f
```

The prefix must match exactly one page. Use `mn ls --long` or `mn find` to obtain Page IDs and
select a page with `--id`. Rename keeps the Page ID unchanged.

## Command reference

All command groups and leaf commands accept `--help`. Help is written to standard output and
returns exit status `0` without initializing workspace, notebook, or user-configuration state.
Invoking `mn notebooks`, `mn config`, `mn config editor`, or `mn completion` without a leaf command
shows that group's help and also returns `0`.

### Global options

| Option | Behavior |
| --- | --- |
| `--workspace <directory>` | Use an existing directory as the workspace for this invocation |
| `--version` | Write `mn <informational-version>` and exit without runtime-state access |
| `--help` | Show root help |

`--workspace` is inherited by workspace-aware subcommands and may be placed before or after the
subcommand.

### `create`

```text
mn create <notebook> [--workspace <directory>]
```

Creates a format `2` notebook directly in the workspace. The `.mnote` suffix is optional. The
command does not overwrite an existing file or directory and does not select the new notebook.

### `use`

```text
mn use <notebook> [--workspace <directory>]
mn use --root [--workspace <directory>]
```

Validates and saves one notebook selection, or returns to the virtual workspace root. Exactly one
of `<notebook>` and `--root` is required. Selection is updated atomically.

### `status`

```text
mn status [--workspace <directory>]
```

Shows the physical workspace, virtual location, selected notebook, and one of `root`, `ready`,
`read-only`, `missing`, `invalid`, or `unsupported`. It may include a detail and safe next action.
`root`, `ready`, and `read-only` return success. See
[State-reporting output](#state-reporting-output) for nonzero cases.

### `notebooks list`

```text
mn notebooks list [-l|--long] [--workspace <directory>]
```

Lists validly named lowercase `.mnote` entries directly beneath the workspace in ordinal file-name
order. `--long` includes current-selection, validation, file-type, and format columns. Listing is
read-only; exceptional entries remain visible and do not make an otherwise completed listing fail.

### `notebooks check`

```text
mn notebooks check <source>
```

Checks one existing format `2` notebook without changing it. `<source>` is a file path resolved
from the process current directory; it is not a workspace notebook name, and the command does not
read or change workspace selection. The report covers format identity, schema, triggers, SQLite
integrity, authoritative values, and derived data.

A valid notebook returns `0`. A completed check that finds one or more issues writes its complete
human-readable report and repair guidance to standard output and returns `2`. Fatal target,
snapshot, access, or storage failures write a diagnostic to standard error instead of a report.
See [Notebook maintenance](#notebook-maintenance) for the issue codes and safety contract.

### `notebooks repair`

```text
mn notebooks repair <source> --output <notebook>
```

Runs a repairability preflight and, only when all authoritative data is safely readable, copies it
without transformation into a new format `2` notebook. It rebuilds derived data, validates the
new notebook, closes and reopens it for complete validation, and then publishes it at `--output`.

Both paths are resolved from the process current directory. `--output` is required and must be a
new path; an existing file, symbolic link, or source/output conflict is rejected. There is no
overwrite or `--force` mode. Repair does not replace the source, select the output, or change
workspace state. See [Notebook maintenance](#notebook-maintenance) for refusal conditions,
reporting, and how to select a repaired notebook.

### `notebooks backup`

```text
mn notebooks backup [<archive>] [--notebook <notebook>] [--workspace <directory>]
```

Creates a validated archive v1/CM2 backup of a format `2` notebook from one consistent snapshot. A file destination
must not already exist. When `<archive>` is omitted or exactly `-`, binary archive bytes are written
to redirected standard output with no human-readable text mixed into the stream:

```bash
mn notebooks backup work-backup.zip
mn notebooks backup --notebook work > work-backup.zip
```

The command refuses to write binary data directly to a terminal. Use a path such as `./-` when a
real file named `-` is intended.

### `notebooks restore`

```text
mn notebooks restore [<archive>] --target <notebook> [--dry-run]
```

Validates archive v1/CM2 and restores it to a new format `2` notebook. The target must not already exist; neither
the archive nor any existing notebook is modified. `--dry-run` performs full validation and reports
record counts without creating the target.

When `<archive>` is omitted or exactly `-`, binary data is read from redirected standard input:

```bash
mn notebooks restore work-backup.zip --target restored --dry-run
mn notebooks restore work-backup.zip --target restored
mn notebooks backup --notebook work | mn notebooks restore --target copied
```

The command refuses to read a binary archive from an interactive terminal. Restore does not select
the new notebook. CM1 is a migration input and is not accepted as native restore input.

### `notebooks migrate`

```text
mn notebooks migrate <source> --output <notebook>
    [--resolution <file>] [--dry-run]
    [--report-level error|warning|info]
```

Migrates a live format `1` notebook or CM1 archive into a separate format `2` notebook. The source,
existing destinations, and workspace selection are never modified. `--output` is required for a
dry-run as well as publication. The default report level is `warning`; page-local errors may
produce a fully validated partial output and exit status `6`.

Read the [schema v1 to v2 migration guide](migration/schema-v1-to-v2.md) before migrating. It
documents backup, loss and warning policy, the resolution document, reruns, workspace selection,
and rollback.

### `notebooks import`

```text
mn notebooks import <directory> [--notebook <notebook>]
    [--on-conflict fail|skip|replace] [--dry-run]
```

Imports a flat directory of lowercase `.txt` files containing strict UTF-8. The default conflict
policy is `fail`:

| Policy | Existing page name |
| --- | --- |
| `fail` | Reject the complete import |
| `skip` | Keep the existing page and count it as skipped |
| `replace` | Replace exactly one matching existing page; reject ambiguous legacy duplicates |

`--dry-run` validates and reports the planned counts without changing the notebook. Import is
atomic: validation or write failure does not leave a partial import. A logically read-only notebook
rejects imports.

### `notebooks export`

```text
mn notebooks export <directory> [--notebook <notebook>]
    [--name-conflict fail|id-suffix]
```

Exports pages as a new flat directory of UTF-8 `.txt` files. The destination path must not already
exist. The default `fail` policy rejects output-name collisions; `id-suffix` adds Page ID material
to produce unique names. Export does not change the notebook.

### `notebooks metadata`

```text
mn notebooks metadata [--notebook <notebook>]
mn notebooks metadata [--notebook <notebook>] <one-update-option>
```

With no update option, displays notebook metadata. Specify at most one of:

| Option | Value |
| --- | --- |
| `--title <value>` | Display title |
| `--description <value>` | Description |
| `--author <value>` | Author |
| `--tag <value>` | Tag |
| `--read-only true|false` | Logical page-write protection |

For text fields, an exact value of `-` reads strict text input from a pipe or redirected standard
input. It is rejected when standard input is interactive. Logical read-only protection prevents
page-changing commands; setting it back to `false` remains available.

### `new`

```text
mn new <page-name> [--notebook <notebook>]
    [--editor <executable> [--editor-arg <argument>]...]
```

Opens an external editor and creates the page only after the editor exits successfully and the
content passes validation. A duplicate current page name is rejected. If the editor makes no
change, no page is created.

### `edit`

```text
mn edit <page-name> [--notebook <notebook>]
mn edit --id <uuid-or-prefix> [--notebook <notebook>]
    [--editor <executable> [--editor-arg <argument>]...]
```

Opens the selected page in an external editor. The update is committed only when the editor exits
successfully, returns valid content, and the stored page has not changed since editing began. No
change is written when the editor content is unchanged.

### `ls`

```text
mn ls [<page-name-pattern>] [--notebook <notebook>]
    [--sort name|updated|created] [-r|--reverse]
    [--limit <positive-count>] [-l|--long]
```

Lists pages or matches complete page names with a case-sensitive Memoria Note glob. The default
sort is `name`; `updated` and `created` use stable name and Page ID tie-breaks, and `--reverse` reverses the
complete selected order. `--limit` returns at most the requested positive number. `--long` includes
all page metadata columns and the complete Page ID. Listing is read-only.

Glob metacharacters are `*`, `?`, character classes such as `[A-Z]`, negated classes `[!0-9]` or
`[^0-9]`, and backslash escape. Matching is ordinal, case-sensitive, and does not normalize
Unicode. Quote wildcard patterns so the shell passes one unchanged argument:

```bash
mn ls 'project-*'
mn ls "project-*"
mn ls 'literal\*name'
```

In zsh, an unquoted wildcard with no filesystem match is rejected by the default `NOMATCH` behavior
before `mn` starts. In bash, an unquoted wildcard that does match filesystem entries is expanded
before `mn` starts. Quoting is therefore recommended in every shell. In zsh, `mn ls project\*`
passes `project*`, while `mn ls "project\*"` preserves the backslash and asks Memoria Note to match
a literal `*`. `noglob mn ls project*` is an occasional zsh alternative; changing shell-wide
`NOMATCH` behavior is not recommended.

If output has already begun when storage or cancellation fails, `ls` reports the number of pages
already printed, identifies the output as incomplete, and exits with status `6`.

### `find`

```text
mn find <keyword>... [--notebook <notebook>]
    [--sort name|updated] [--limit <count>]
    [--case-sensitive] [--no-snippet]
```

Finds pages whose bodies contain every literal keyword. Keywords are split on Unicode whitespace
and combined with AND. Text such as `AND`, `OR`, `*`, and quotes remains literal; shell quoting does
not create a phrase. One- and two-scalar keywords are accepted. The default uses culture-independent
ordinal case-insensitive comparison; `--case-sensitive` uses ordinal case-sensitive comparison.
Neither mode applies Unicode normalization, diacritic removal, width or kana conversion, or word
boundaries.

The default sort is `name`, the default limit is `20`, and the maximum is `1000`. A result contains
the complete Page ID and escaped page name on its first line. Unless `--no-snippet` is used, the
second line contains a sanitized body window with matches marked by ASCII `[[` and `]]`:

```text
744ffba0-0000-0000-0000-000000000000 meeting-notes
  discuss [[release]] readiness
```

Use the Page ID directly, avoiding ambiguity and shell interpretation of the name:

```bash
mn cat --id 744ffba0-0000-0000-0000-000000000000
mn edit --id 744ffba0-0000-0000-0000-000000000000
```

Zero results are a successful invocation with no output. `find` searches page bodies; `ls` matches
page names. Neither command searches multiple notebooks or provides a tag filter. A format `1`
notebook produces migration guidance; an unknown newer format is rejected without assuming it can
be migrated. Invalid queries return status `2`, storage or timeout before output returns `5`, and
process cancellation returns `130`. A failure after one or more results reports incomplete output
and returns `6`.

### `cat`

```text
mn cat <page-name> [--notebook <notebook>]
mn cat --id <uuid-or-prefix> [--notebook <notebook>]
```

Writes exactly one page body to standard output. It does not add a heading or success message,
which makes it suitable for redirection and pipelines. Diagnostics remain on standard error.

### `rename`

```text
mn rename <page-name> <new-name> [--notebook <notebook>]
mn rename --id <uuid-or-prefix> <new-name> [--notebook <notebook>]
```

Renames one page without changing its Page ID or body. A current page with the replacement name is
a conflict. `--id` is the safe form for legacy duplicate names.

### `delete`

```text
mn delete <page-name> [--notebook <notebook>] [--dry-run] [--force]
mn delete --id <uuid-or-prefix> [--notebook <notebook>] [--dry-run] [--force]
```

`--dry-run` validates and identifies the fixed Page ID without prompting or deleting. Without
`--dry-run` or `--force`, deletion prompts on standard error only when standard input and the prompt
stream are both interactive. Only `y` or `yes`, ignoring case and surrounding whitespace, confirms.
Every other answer, an empty line, or EOF is a successful user cancellation and changes nothing.

Non-interactive deletion requires `--force`. It skips only the prompt; target resolution,
read-only checks, conflict checks, and execution-time revalidation still apply. `--dry-run` takes
precedence when both options are present.

### `config`

The user-configuration commands are independent of a workspace:

| Command | Behavior |
| --- | --- |
| `mn config path` | Show the configuration path and whether it exists |
| `mn config show` | Show file status, format version, stored editor selection, and effective command |
| `mn config validate` | Validate without writing; a missing file returns not-found |
| `mn config editor show` | Show only the editor selection and effective command |
| `mn config editor setup` | Interactively configure or replace the editor selection |
| `mn config editor unset` | Save an explicit unconfigured selection |

Read-only config commands do not create a missing file or repair invalid content. `editor setup`
requires interactive input and saves atomically after confirmation. Missing configuration and an
explicit `unset` selection are distinct states, although both require setup before `new` or `edit`.

### `completion zsh`

```text
mn completion zsh
```

Writes a deterministic static zsh completion function to standard output. It reads command
metadata but does not initialize or modify the workspace, user configuration, notebook database,
logs, or temporary application state. It does not query notebook or page data.

## External editor and user configuration

Run the interactive setup:

```bash
mn config editor setup
```

The workflow offers three selections:

1. **Environment variable** — enter a variable name, or press Enter for `EDITOR`. The variable's
   current value is resolved each time an editor is started.
2. **Program** — enter an executable and its arguments as separate values. They are not parsed as a
   shell command.
3. **Unset** — explicitly leave the editor unconfigured.

An editor argument may contain `{file}`, which is replaced with the temporary document path. If no
argument contains it, the path is appended. Arguments are passed directly to the child process.

For one invocation, `--editor` and repeatable `--editor-arg` options bypass all persisted editor
configuration:

```bash
mn edit project-notes --editor code --editor-arg=--wait --editor-arg={file}
```

Use the `=` form when an editor argument begins with `-`. `--editor-arg` is invalid without
`--editor`. An invocation override remains usable when `config.toml` is missing, invalid, or from an
unsupported future format.

Memoria Note waits for the editor process. A nonzero exit, unavailable executable, missing temporary
file, invalid UTF-8, validation failure, concurrent page change, or cancellation leaves notebook
content unchanged.

## Notebook maintenance

`notebooks check` and `notebooks repair` operate only on current format `2` notebooks. They do not
migrate format `1`, downgrade an unknown future format, restore an archive, replace a source, or
change workspace selection. Use `notebooks migrate` for a format `1` notebook or CM1 archive and
`notebooks restore` for a CM2 archive.

### Check layers and issue codes

Check evaluates layers in this order. A layer that cannot be evaluated safely because an earlier
layer failed is reported as `not evaluated`, not as an additional failure:

1. `identity` — Memoria Note application ID and notebook format version;
2. `schema` — tables, columns, constraints, indexes, and foreign keys;
3. `triggers` — required trigger definitions and behavior;
4. `integrity` — SQLite integrity and foreign keys;
5. `authoritative-values` — metadata and page value contracts;
6. `derived-data` — the exact `PageTags` projection of authoritative tags.

Completed reports use the following stable issue codes. Location, expected value, and a safe
actual summary may accompany a code; private page bodies and complete metadata values are not
included.

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
the notebook is valid, gives each layer's status, confirms `Source modified: no`, and reports the
issue count. An issue report suggests repair as a possible next action but does not promise that
the stricter repairability preflight will accept the source.

Missing files, invalid paths, format `1`, unsupported future formats, unreadable input, busy
snapshots, and access or storage failures are target-resolution errors rather than check issues.
A format `1` source receives migration guidance. An unknown future format requires a compatible
newer release. Unreadable or corrupt input receives backup or archive recovery guidance instead of
an unsafe repair recommendation.

### Repairability and publication

Repair can accept a current format `2` notebook whose defect is limited to derived data or other
release-defined rebuildable objects. It refuses to copy when the authoritative schema cannot be
read exactly, SQLite integrity of authoritative storage cannot be established, or an authoritative
value violates the release contract. Refusal reports use these stable codes:

| Code | Meaning |
| --- | --- |
| `authoritative-schema-mismatch` | A required authoritative table or column cannot be read exactly |
| `authoritative-integrity-failure` | Integrity of authoritative storage cannot be established |
| `authoritative-value-invalid` | One or more authoritative values violate the release contract |

Repair copies every required and extension metadata value and every logical page value. It does
not infer missing values, normalize text, skip records, or copy SQLite-local `InternalId` values.
The new notebook receives local internal IDs while preserving stable Page IDs. The successful
report includes record counts, copied authoritative categories, rebuilt derived categories, a
logical fingerprint, output validation, and publication status; it does not expose private page
bodies or metadata values.

The output is first built at a private path in the destination directory. It is published only
after schema, integrity, value, derived-data, logical-equivalence, and close/reopen validation all
succeed. Failure or cancellation removes the private output and its owned sidecars and never
publishes a partial result. The source and any pre-existing destination remain unchanged.

### Concurrency, reports, and exit status

Check and repair wait up to 30 seconds for the read locks needed for a consistent snapshot. If a
writer prevents a safe snapshot, close it and retry; busy is not reported as corruption. Repair
holds one consistent authoritative snapshot across preflight and copy.

The `v0.4.0` maintenance interface provides human-readable reports only. It does not provide JSON
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

### Using a repaired notebook

Repair does not register or select its output. To use it, place the new file directly in the
workspace root, keep the original and an independent backup during review, then select it
explicitly:

```console
mn notebooks check "work-repaired.mnote"
mn notebooks list --long
mn use work-repaired
mn status
```

Do not replace or delete the source until the output has been reviewed and backed up. Source
replacement and automatic workspace registration are not maintenance operations in this release.

## Backup, restore, and data transfer

Archive v1 is a logical backup of every authoritative value in one notebook; it is not a copy of
SQLite file bytes. It is validated before publication and fully validated before a restore target
is created. Checksums detect corruption but do not authenticate the archive's source.

The native `v0.3.0` and `v0.4.0` tuple is archive format `1`, content model `2`, and source notebook
format `2`.
The legacy tuple `(1, 1, "1")` is read only by explicit migration. Mixed and unknown tuples are
rejected rather than interpreted as a nearby version. The normative layout and compatibility
matrix are in the [archive v1 specification](archive/v1/README.md).

Use archive backup and restore when all notebook metadata and page values must round-trip. Use text
export and import for human-readable page bodies or transfer through a flat directory; text
transfer is not a full-fidelity notebook backup.

## zsh completion

Install the generated function in a user-owned directory:

```zsh
mkdir -p "$HOME/.local/share/zsh/site-functions"
mn completion zsh > "$HOME/.local/share/zsh/site-functions/_mn"
```

Add the directory to `fpath` before `compinit` in `~/.zshrc`:

```zsh
fpath=("$HOME/.local/share/zsh/site-functions" $fpath)
autoload -Uz compinit
compinit
```

If `.zshrc` already invokes `compinit`, add only the `fpath` assignment before that invocation.
Start a new shell after changing the configuration. After updating Memoria Note, regenerate `_mn`
with the newly installed executable. To uninstall completion, remove `_mn`; remove the `fpath`
entry as well if the directory is no longer used.

Completion is static and supports zsh only. It completes command metadata, fixed
values, and suitable file or directory arguments, but does not read notebooks or pages. Bash, fish,
PowerShell, and dynamic data completion are not supported.

## Automation and the CLI process contract

This section is the documentation source of truth for the release's process-level
automation contract.

### Exit status

| Status | Classification | Meaning |
| ---: | --- | --- |
| `0` | success | Operation completed, produced no changes, or was declined by the user |
| `1` | unexpected | Unexpected internal failure |
| `2` | validation | Invalid syntax, option, input, data, or unsupported format |
| `3` | not found | Required file, directory, notebook, page, or configuration was not found |
| `4` | conflict | Existing or ambiguous data prevents the requested operation |
| `5` | storage | Filesystem, database, archive, editor, or other external I/O failure |
| `6` | partial | A validated migration or streamed listing/search result is incomplete |
| `130` | canceled | Process cancellation such as `Ctrl+C` |

A non-affirmative answer to the delete prompt and a canceled config setup are user decisions, not
process cancellation; they return `0` and make no change. `Ctrl+C` returns `130` when cancellation
is observed by the command.

### Standard streams

- Help, version, successful human-readable output, summaries, and read-only reports use standard
  output.
- Expected diagnostics use standard error with an `Error:` prefix.
- Cancellation caused by process interruption uses standard error with a `Canceled:` prefix.
- Unexpected failures use standard error with a `Fatal:` prefix. Internal exception details are
  not part of normal terminal output.
- `mn cat` writes only the page body to standard output.
- `notebooks backup` writes only binary archive bytes when standard output is selected.
- `notebooks restore` can read binary archive bytes from standard input.
- `notebooks metadata <text-option> -` reads the value from standard input.
- `mn completion zsh` writes only the completion script to standard output.

Do not merge standard error into a raw output stream that will be stored or piped as page,
archive, or completion content.

### State-reporting output

`mn status` writes the complete state report to standard output even when a missing selection
returns `3`, or invalid/unsupported state returns `2`. It does not duplicate the already reported
detail on standard error. `mn config show` and `mn config validate` may likewise write a status such
as `invalid`, `unsupported`, or `missing` to standard output before returning the classified
failure and diagnostic.

A completed `notebooks check` issue report and a completed `notebooks repair` refusal report also
remain on standard output while returning `2`. Fatal maintenance errors that prevent a complete
report use standard error instead.

Scripts must check the exit status as well as consuming state-reporting output.

### Stability boundary

Exit-status classifications, stream placement, and outputs explicitly documented as raw are the
automation contract. Human-readable help, notifications, diagnostic sentences, tables, labels,
and whitespace are not a versioned machine-readable format and must not be parsed as an external
application API.

The release does not provide general-purpose versioned JSON output, a batch protocol, or a REST API.

### Non-interactive commands

- Use `delete --force` when deletion is intentional and no terminal prompt is available; use
  `--dry-run` first when validation and target reporting are needed.
- `config editor setup` is interactive. Use per-invocation `--editor` options for non-interactive
  execution rather than scripting its prompts.
- Redirect binary backup and restore streams; terminal-connected binary streams are rejected.
- Quote workspace, notebook, page, and path arguments according to the calling shell.

## Migration from `configuration.json`

`config.toml` is the only user configuration read by this release. Memoria Note does not
migrate, read, validate, repair, rename, quarantine, or delete the previous
`configuration.json`. The legacy file remains byte-for-byte unchanged and is ignored even when it
is beside `config.toml`.

After upgrading, run:

```bash
mn config editor setup
```

This change does not alter `.mnote` notebooks, `mn-workspace.toml`, or archive v1 files. Do not
delete the old configuration until rollback to an older release is no longer required.

## Recovery and troubleshooting

### Inspect state without changing it

Start with these commands:

```bash
mn --version
mn status
mn notebooks list --long
mn config path
mn config show
mn config validate
```

`status`, notebook listing, and the config inspection commands do not repair or replace data. Use
an explicit `--workspace` or `--notebook` override to isolate a saved-selection problem.

To inspect a specific current-format notebook more deeply without changing it, provide its file
path explicitly:

```bash
mn notebooks check "work.mnote"
```

If check finds issues, preserve the source and its sidecars before attempting repair. Repair to a
different new path and review the output as described in
[Notebook maintenance](#notebook-maintenance).

### Invalid workspace selection

If `mn-workspace.toml` is invalid or refers to a missing/unsupported notebook, inspect it with
`mn status`. Select a valid notebook with `mn use <notebook>` or clear the selection with
`mn use --root`. An invocation-only `--notebook` can access a valid notebook without rewriting an
invalid workspace configuration.

### Invalid user configuration

Use `mn config path` to locate the file and `mn config validate` to classify it. A missing, invalid,
or unsupported `config.toml` is not silently replaced. Preserve a copy for diagnosis, then remove
or rename it only when intentionally resetting editor configuration; run
`mn config editor setup` to create a valid replacement.

### Restore and rollback

Never restore over an existing notebook. Validate first with `notebooks restore --dry-run`, restore
to a new leaf name, inspect the result, and only then change the saved selection. Keep the source
notebook and input archive until verification is complete.

Application rollback changes only the installed executable path. If a future release changes data
compatibility, use the backup made by the older compatible release rather than opening a newer or
unknown format with an older binary.

### Legacy archives

ZIP files matching a recognized pre-v1 legacy layout are explicitly rejected. Memoria Note does
not reinterpret, convert, or modify them. Preserve the original archive and obtain a compatible
older Memoria Note release for manual recovery. Recover or export the content with that release,
then create data that the current release can import or restore. Do not rename entries or edit the
legacy ZIP in place; work from a copy.

An unrelated, malformed, damaged, or unsupported-version ZIP may receive a different validation
classification. Failure never creates or replaces the requested restore target.

## Files

| File or directory | Purpose |
| --- | --- |
| `<workspace>/*.mnote` | Live SQLite notebooks directly beneath the workspace |
| `<workspace>/mn-workspace.toml` | Versioned saved notebook selection |
| Path printed by `mn config path` | Versioned user editor configuration (`config.toml`) |
| `$HOME/.local/share/zsh/site-functions/_mn` | Recommended user zsh completion file |

Use `mn config path` to discover the exact `config.toml` path on the current platform. Temporary
editor documents, logs, and other runtime files are application implementation details and should
not be used as durable data.

## Environment

| Variable | Behavior |
| --- | --- |
| `EDITOR` | Default editor variable name offered by interactive setup |
| User-selected editor variable | Read when starting the editor if environment selection is configured |
| `MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY` | Overrides the application-data directory for this process |
| `PATH` | Locates `mn` and editor executables when a path is not explicit |
| `HOME` | Used by the documented Linux installation and zsh completion paths |

The application-data override is useful for isolated execution and testing. Pointing it at another
directory changes which `config.toml` the process uses; it does not change the workspace.

## Limitations

- Release support is limited to the `linux-x64` and `win-x64` self-contained archives.
- Notebooks should be kept on a local filesystem; network and synchronized filesystem behavior is
  outside the initial support contract.
- Workspaces are flat. Notebook discovery does not recurse into subdirectories.
- Static zsh completion is the only supported shell completion and does not query persisted data.
- There is no installer, automatic updater, package-manager package, or automatic data migration.
- There is no versioned machine-readable output interface.
- Archive v1 checksums detect damage but are not signatures and do not prove provenance.

For maintainer checks performed before publishing a release, see the
[release artifact verification checklist](release/artifact-verification.md).
