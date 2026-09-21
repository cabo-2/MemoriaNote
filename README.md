# Memoria Note

Memoria Note is a lightweight, database-backed command-line notepad for Windows and Linux. It organizes pages into SQLite notebooks and uses your preferred external editor for writing and editing content.

## Features

- **Database-Backed Storage:** Pages and notebook metadata are stored in SQLite databases.
- **Cross-Platform:** Runs on both Windows and Linux.
- **One-shot editing:** `new` and `edit` open an external editor once and update a page only after successful validation.
- **Notebook Workspaces:** Create, add, select, edit, back up, and restore notebooks from the command line.
- **Import and Export:** Move pages between a notebook and text files.
- **Shell Integration:** Supports zsh autocomplete for quickly finding and managing your notes.

## Installation

### Get the code

```bash
git clone "https://github.com/cabo-2/MemoriaNote.git"
cd MemoriaNote
```

Building Memoria Note requires the .NET 10 SDK. An external editor is also required for commands that modify pages or notebook metadata. Set the `EDITOR` environment variable to select one; otherwise Memoria Note uses its configured editor path.

### Build and test

Run the following commands from the repository root:

```bash
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

### Publish for Windows

```pwsh
dotnet publish cli/mn.csproj -c Release -r win-x64 --self-contained
```

```pwsh
Copy-Item -Recurse cli\bin\Release\net10.0\win-x64\publish C:\path\to\dir
```

### Publish for Linux (including WSL)

```bash
dotnet publish cli/mn.csproj -c Release -r linux-x64 --self-contained
```

```bash
cp -r cli/bin/Release/net10.0/linux-x64/publish /path/to/dir
```

#### Supports zsh autocomplete

**Requires administrator privileges**

```bash
sudo cp misc/zsh-completion/_mn /usr/share/zsh/site-functions/
```

## Usage

After installing Memoria Note, you can perform various operations through the command line:

```bash
mn --help
```

This command displays a help message outlining all available commands and options:

```
A lightweight, cross-platform CLI for creating, organizing, and editing workspace-based notes

Usage: mn [command] [options]

Options:
  --help                   Show help information.
  --workspace <directory>  Use this directory as the workspace for this invocation

Commands:
  cat     Write one page body to standard output
  create  Create a live notebook without overwriting an existing file
  edit    Edit text with an external editor
  ls      List pages in stable page-name order
  new     Create text command

Run 'mn [command] --help' for more information about a command.
```

`list` remains available as a compatibility alias for `ls`. Dynamic page-name completion is
temporarily disabled while a dedicated completion command is designed.

### Exit codes

Commands use stable exit codes so shell scripts can distinguish failures:

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Command-line parsing or unexpected failure |
| `2` | Invalid input or data |
| `3` | File, directory, note, or page not found |
| `4` | Conflict with existing data or state |
| `5` | Storage or external I/O failure |
| `130` | Canceled, including `Ctrl+C` |

Expected diagnostics are written to standard error with an `Error:` prefix.
Unexpected failures use `Fatal:`.

### Examples

Create a live notebook in the current directory, or in an explicitly selected
workspace. Relative notebook paths are resolved from the workspace, and existing
files are never overwritten:

```bash
mn create work.mnote
mn --workspace /path/to/notes create work.mnote
```

The `.mnote` extension identifies live notebook candidates. A created notebook is
also reopened and checked for the current SQLite schema and format metadata before
the command reports success. `create` does not create or update user configuration
or `mn-workspace.toml`.

Create a page in the new notebook by targeting it explicitly. The command opens
the configured external editor and saves the page when the editor exits
successfully with changed content:

```bash
mn new --notebook work.mnote meeting-notes
```

List page names in stable name order, optionally limiting the result or showing metadata:

```bash
mn ls --notebook work.mnote
mn ls --notebook work.mnote --limit 50
mn ls --notebook work.mnote --long
```

Write exactly one page body to standard output by its exact name, complete Page ID,
or a unique Page ID prefix of at least four hexadecimal characters:

```bash
mn cat --notebook work.mnote meeting-notes
mn cat --notebook work.mnote --id 744ffba0-0000-0000-0000-000000000000
mn cat --notebook work.mnote --id 744f
```

If a page name or Page ID prefix matches more than one page, `cat` reports a
conflict and requires a more specific `--id` value. It does not add a trailing
newline to the stored page body.

Edit an existing page:

```bash
mn edit meeting-notes
```
