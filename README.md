# Memoria Note

Memoria Note is a lightweight, database-backed command-line notepad. It stores pages in SQLite
notebook files inside a workspace and uses your preferred external editor to create and edit page
content.

The initial release provides self-contained archives for 64-bit Linux and Windows. A separate .NET
runtime is not required.

## Install

Download the archive for your platform and `SHA256SUMS` from the
[latest GitHub Release](https://github.com/cabo-2/MemoriaNote/releases/latest):

- Linux x64: `mn-v<version>-linux-x64.tar.gz`
- Windows x64: `mn-v<version>-win-x64.zip`

Verify the archive against `SHA256SUMS` before extracting it. Keep the complete extracted directory
together; `mn` depends on the managed and native libraries beside it.

### Linux

Extract the archive into a versioned application directory and link the executable from a directory
on `PATH`. For release `v0.2.1`:

```bash
tar -xzf mn-v0.2.1-linux-x64.tar.gz
mkdir -p "$HOME/.local/lib/memoria-note" "$HOME/.local/bin"
mv mn-v0.2.1-linux-x64 "$HOME/.local/lib/memoria-note/0.2.1"
ln -s "$HOME/.local/lib/memoria-note/0.2.1/mn" "$HOME/.local/bin/mn"
mn --version
```

Ensure `$HOME/.local/bin` is on `PATH` before running `mn`.

### Windows

Extract `mn-v0.2.1-win-x64.zip` into a versioned directory such as
`%LOCALAPPDATA%\Programs\MemoriaNote\0.2.1`, add that complete directory to the user `PATH`, open a
new terminal, and run:

```powershell
mn.exe --version
```

See the [user guide](reference/user-guide.md#installation-update-and-uninstall) for checksum,
update, rollback, and uninstall instructions.

## Quick start

Create a workspace and notebook, select the notebook, and configure an editor:

```bash
mkdir notes
cd notes
mn create work
mn use work
mn config editor setup
```

Create, list, read, and edit a page:

```bash
mn new meeting-notes
mn ls
mn cat meeting-notes
mn edit meeting-notes
```

`mn create work` creates `work.mnote`; it does not select it automatically. The selection made by
`mn use work` is stored in the workspace's `mn-workspace.toml`. Run `mn status` at any time to see
the current workspace and notebook state.

## zsh completion

Generate completion from the installed `mn`:

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

Restart zsh after changing the file. Regenerate `_mn` whenever `mn` is updated.

## Documentation

- [User guide](reference/user-guide.md): concepts, every command, operations, migration, recovery,
  troubleshooting, and the CLI process contract
- [Archive v1 format](reference/archive/v1/format.md): normative backup and restore format
- [Release artifact verification](reference/release/artifact-verification.md): maintainer release
  checklist

Memoria Note has stable exit-status and stream conventions for shell automation. Human-readable
diagnostic text is not a versioned machine-readable interface; see
[Automation and the CLI process contract](reference/user-guide.md#automation-and-the-cli-process-contract).

## Build from source

Building requires the .NET 10 SDK. From the repository root:

```bash
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

Repository builds are development builds and display a version such as
`0.2.1-dev+1a2b3c4`. Release archives display the corresponding release version without the
development suffix.

## Initial-release limits

- Supported release artifacts are `linux-x64` and `win-x64` self-contained archives.
- macOS, Arm, framework-dependent, single-file, Native AOT, trimmed, and ReadyToRun distributions
  are not provided.
- Installers and package-manager packages are not provided.
- Static completion is provided for zsh only; bash, fish, PowerShell, and dynamic data completion
  are not supported.
- Notebook files should be kept on a local filesystem. SQLite behavior on network or synchronized
  storage depends on that filesystem.

Memoria Note is licensed under the [MIT License](LICENSE).
