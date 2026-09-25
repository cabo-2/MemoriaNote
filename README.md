# Memoria Note

Memoria Note is a lightweight, database-backed command-line notepad for Windows and Linux. It
stores pages in SQLite notebook files and opens your preferred external editor when creating or
editing page content.

## Features

- **Flat workspaces:** A directory is treated as a workspace, with live `.mnote` notebooks directly
  beneath it. Subdirectories are not searched or registered.
- **Explicit notebook selection:** `mn use` stores the current notebook in the versioned,
  atomically updated `mn-workspace.toml`. Notebook count never changes the selection implicitly.
- **SQLite storage:** Each notebook is a self-contained SQLite database with validated schema and
  format metadata.
- **Page-oriented commands:** Create, list, read, edit, rename, and delete pages from the command
  line. Page IDs provide an unambiguous alternative to names.
- **Safe external editing:** Page content is committed only after the editor exits successfully and
  validation passes.
- **Script-friendly behavior:** Stable exit codes, diagnostics on standard error, raw page output
  from `cat`, and invocation-only notebook overrides.
- **Cross-platform:** Runs on Windows and Linux with .NET 10.

## Installation

### Get the code

```bash
git clone "https://github.com/cabo-2/MemoriaNote.git"
cd MemoriaNote
```

Building Memoria Note requires the .NET 10 SDK. The `new` and `edit` commands also require an
external editor. With the default settings, defining the `EDITOR` environment variable is the
simplest configuration:

```bash
export EDITOR=nano
```

For PowerShell, for example:

```powershell
$env:EDITOR = "notepad"
```

`--editor <executable>` overrides the configured editor for one `new` or `edit` invocation.

### Build and test

Run the following commands from the repository root:

```bash
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

### Publish for Windows

```powershell
dotnet publish cli/mn.csproj -c Release -r win-x64 --self-contained
Copy-Item -Recurse cli\bin\Release\net10.0\win-x64\publish C:\path\to\dir
```

### Publish for Linux, including WSL

```bash
dotnet publish cli/mn.csproj -c Release -r linux-x64 --self-contained
cp -r cli/bin/Release/net10.0/linux-x64/publish /path/to/dir
```

## Usage

Run `mn --help` to see the public command surface:

```text
A lightweight, cross-platform CLI for workspace notebooks

Usage: mn [command] [options]

Options:
  --help                   Show help information.
  --workspace <directory>  Use this directory as the workspace for this
                           invocation

Commands:
  cat                      Write one page body to standard output
  create                   Create a live notebook without overwriting an
                           existing file
  delete                   Delete one page after confirmation
  edit                     Edit one page with an external editor
  ls                       List pages in stable page-name order
  new                      Create one page with an external editor
  rename                   Rename one page without changing its Page ID
  use                      Select a notebook or return to the workspace root

Run 'mn [command] --help' for more information about a command.
```

`list` remains available as a compatibility alias for `ls`. Run
`mn <command> --help` for command-specific arguments and options.

### Workspace and notebook selection

The current directory is the workspace unless `--workspace <directory>` is supplied. A workspace
contains its live notebooks and optional `mn-workspace.toml` configuration at the top level.

Notebook input is always a leaf name. A lowercase `.mnote` suffix is optional and is added when
omitted. Path separators and suffixes such as `.MNOTE` are rejected.

Selection follows these rules:

1. `--notebook <notebook>` selects a notebook for that invocation only.
2. Otherwise, commands use the notebook saved by `mn use <notebook>`.
3. Without either selection, the workspace remains at its virtual root `/` and notebook commands
   report how to select a target.

Creating a notebook does not select it. Even when the workspace contains exactly one notebook,
Memoria Note never selects it automatically. A missing or invalid saved target is reported without
falling back to another notebook.

Notebook file symbolic links are supported and the workspace alias is saved as the selection.
Local filesystems are recommended because SQLite locking behavior on network or synchronized
storage depends on that filesystem. `mn-workspace.toml` itself must not be a symbolic link.

## Examples

### Start a workspace and manage a page

Create a workspace directory, create a notebook, and select it explicitly:

```bash
mkdir notes
cd notes
mn create work
mn use work
```

`work` is normalized to `work.mnote`. The selection is stored as that leaf name in
`mn-workspace.toml`.

Create a page with the external editor, then list and read it:

```bash
mn new meeting-notes
mn ls
mn cat meeting-notes
```

Rename and edit the same page:

```bash
mn rename meeting-notes project-notes
mn edit project-notes
```

Preview a deletion, then perform it without an interactive prompt:

```bash
mn delete project-notes --dry-run
mn delete project-notes --force
```

Return to the virtual workspace root without deleting notebooks:

```bash
mn use --root
```

If no workspace configuration exists, `mn use --root` succeeds without creating one.

### Work with multiple notebooks

Create and select another notebook:

```bash
mn create personal
mn use personal
mn new shopping-list
```

Temporarily target `work.mnote` without changing the saved `personal.mnote` selection:

```bash
mn ls --notebook work
mn new --notebook work release-notes
mn cat --notebook work release-notes
```

The same workflow can target a different directory without changing the process working directory:

```bash
mn --workspace /path/to/notes use work
mn --workspace /path/to/notes ls
```

### Select pages by ID

`mn ls --long` displays Page IDs. `edit`, `cat`, `rename`, and `delete` accept a complete Page ID or
a unique hexadecimal prefix of at least four characters. This is useful when legacy data contains
duplicate page names:

```bash
mn cat --id 744ffba0-0000-0000-0000-000000000000
mn edit --id 744f
mn rename --id 744f archived-notes
mn delete --id 744f --dry-run
```

An ambiguous name or ID prefix returns a conflict instead of choosing a page implicitly.

### Override the editor

Pass an editor and its arguments without shell-string parsing. `{file}` is replaced with the
temporary document path; if omitted, the path is appended automatically. Use the `=` form when an
editor argument begins with `-`:

```bash
mn edit project-notes --editor code --editor-arg=--wait --editor-arg={file}
```

The page is updated only when the editor exits with code 0, the temporary file remains available,
its contents are valid UTF-8, validation succeeds, and the stored page has not changed since editing
started. Unchanged content reports `No changes.`.

## Exit codes

Commands use stable exit codes so shell scripts can distinguish failures:

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Command-line parsing or unexpected failure |
| `2` | Invalid input or data |
| `3` | File, directory, notebook, or page not found |
| `4` | Conflict with existing data or state |
| `5` | Storage or external I/O failure |
| `130` | Canceled, including `Ctrl+C` |

Expected diagnostics are written to standard error with an `Error:` prefix. Unexpected failures use
`Fatal:`.
