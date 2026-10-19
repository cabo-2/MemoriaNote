# Memoria Note release artifact verification

This document is the repeatable operator checklist for Memoria Note release
artifacts. It covers automated artifact smoke tests and the manual checks that
must be completed before a draft GitHub Release is published.

The initial release targets only the self-contained `linux-x64` and `win-x64`
archives. macOS, framework-dependent deployment, Native AOT, trimming,
ReadyToRun, installers, and package managers are out of scope.

## Automated verification

The common runner is `.github/scripts/verify-release-artifacts.py`. Its
`smoke` mode requires an explicit executable path and never falls back to a
build output. Its `package` mode consumes the NCLI-650b result contract,
validates and cleanly extracts the archive, and runs the same smoke scenarios
against both the publish directory and the extracted archive.

The smoke scenarios cover:

- version, help, and parser-error process contracts;
- a workspace path containing spaces and Unicode;
- `create`, `use`, `new`, `ls`, and `cat` with an isolated editor helper;
- file-based archive v1 backup and restore;
- zsh completion encoding and representative metadata;
- self-contained host, runtime, and SQLite native dependencies.

Every scenario redirects application data, temporary files, workspace state,
and .NET CLI state beneath a unique temporary root. Successful runs remove the
root. Failed runs preserve it by default and report the command, exit code,
standard output, standard error, and retained path. CI passes
`--cleanup-on-failure` because hosted-runner storage is temporary.

### Ordinary CI trigger

The normal CI workflow runs native package verification for pull requests,
pushes to `main`, and manual `workflow_dispatch` runs. It uses the synthetic
tag `v0.0.0`, does not create or push a Git tag, does not call the GitHub
Release API, and does not upload an Actions artifact.

This verification remains an ordinary CI regression gate. The tag-only release
workflow repeats the native package and artifact-smoke checks with the actual
normalized release version before it uploads either archive.

### Tag-triggered draft Release

Pushing a tag in strict `v<major>.<minor>.<patch>` form runs
`.github/workflows/release.yml`. The workflow rejects a tag whose version does
not match `MemoriaNoteProductVersion`, whose event revision does not resolve to
the tagged commit, or whose commit is not contained in `main`. Lightweight and
annotated tags are both resolved to their commit before these checks.

After native release builds and the full test suite succeed on Ubuntu and
Windows, the workflow creates or reuses a draft GitHub Release. Each native job
packages its RID, verifies the publish directory and clean archive extraction,
and uploads only the verified archive. The final job downloads both archives
from the draft, regenerates `SHA256SUMS` from those downloaded bytes, and
uploads it. No Actions artifact is used for transfer or storage.

A rerun reuses an existing draft and replaces same-named platform assets only
after local verification succeeds. Preparing or finalizing a rerun removes an
old `SHA256SUMS` before assets can change, so a partial failure cannot leave a
stale checksum presented as current. The workflow refuses to modify a
published Release and never publishes a draft automatically.

### Local Linux verification

From the repository root, substitute the intended tag and version when
verifying a release candidate:

```console
python3 .github/scripts/package-release.py \
  --tag v0.2.0 \
  --rid linux-x64

python3 .github/scripts/verify-release-artifacts.py package \
  --package-results artifacts/release/v0.2.0/package-results.json \
  --publish-executable artifacts/release/v0.2.0/publish/linux-x64/mn \
  --rid linux-x64 \
  --expected-version 0.2.0
```

The local Linux run is authoritative only for `linux-x64`. A Linux host may
cross-publish and inspect the layout of `win-x64`, but it cannot replace the
native Windows artifact smoke run.

### Native Windows verification

Run the following from PowerShell at the repository root:

```powershell
python .github/scripts/package-release.py `
  --tag v0.2.0 `
  --rid win-x64

python .github/scripts/verify-release-artifacts.py package `
  --package-results artifacts/release/v0.2.0/package-results.json `
  --publish-executable artifacts/release/v0.2.0/publish/win-x64/mn.exe `
  --rid win-x64 `
  --expected-version 0.2.0
```

GitHub Actions provides the routine native Windows result. Before the initial
release is published, and after a material packaging change, repeat the manual
checks below on a real Windows installation.

## Archive integrity checklist

Complete these checks for both release archives:

- [ ] The tag uses `v<major>.<minor>.<patch>` and has not been moved or reused.
- [ ] The tag version, `mn --version`, archive name, archive root, and draft
      GitHub Release version agree after removing their display prefixes.
- [ ] The asset names are
      `mn-v<version>-linux-x64.tar.gz` and
      `mn-v<version>-win-x64.zip`.
- [ ] `SHA256SUMS` contains exactly one correct entry for each published
      archive.
- [ ] Each archive contains one `mn-v<version>-<rid>` root directory.
- [ ] Each root contains `LICENSE`, the application executable, `mn.dll`,
      `mn.deps.json`, and `mn.runtimeconfig.json`.
- [ ] Linux contains `libhostfxr.so`, `libhostpolicy.so`, and
      `libe_sqlite3.so`; Windows contains `hostfxr.dll`, `hostpolicy.dll`, and
      `e_sqlite3.dll`.
- [ ] The extracted Linux `mn` has an executable bit.
- [ ] The common package verification succeeds on native Linux and native
      Windows for the publish directory and clean archive extraction.
- [ ] No macOS, framework-dependent, Native AOT, installer, or package-manager
      asset is attached.

## Runtime-free target check

On a clean target environment that does not have a separately installed .NET
runtime:

- [ ] Extract the archive into a new directory.
- [ ] Run `mn --version` or `mn.exe --version` and verify the release version.
- [ ] Run `--help` and the representative workspace workflow.
- [ ] Confirm that no dependency is read from the repository checkout or a
      development build directory.

The target may have the .NET SDK installed only when performing the automated
runner procedure. A runtime-free manual target is required to confirm the
self-contained user experience.

## Install, update, and uninstall rehearsal

Keep the complete extracted directory together. Copying only `mn` or `mn.exe`
loses required managed and native dependencies.

### Linux

Use a versioned application directory such as
`$HOME/.local/lib/memoria-note/<version>` and expose the executable with a
symbolic link at `$HOME/.local/bin/mn`.

- [ ] Fresh install: extract into a new versioned directory, create the link,
      start a new shell, and run `mn --version`.
- [ ] Update: install the new version beside the old one, change the link, and
      verify the new version before deleting the old directory.
- [ ] Rollback rehearsal: point the link back to the previous intact version
      without changing workspace or user data.
- [ ] Uninstall: remove the link and installed version directories without
      deleting notebooks, workspace configuration, or user configuration.

### Windows

Use a versioned directory such as
`%LOCALAPPDATA%\Programs\MemoriaNote\<version>` and add that complete directory
to the user `PATH`.

- [ ] Fresh install: expand into a new versioned directory, update the user
      `PATH`, start a new terminal, and run `mn.exe --version`.
- [ ] Update: expand the new version beside the old one, change the `PATH`
      entry, and verify the new version before deleting the old directory.
- [ ] Rollback rehearsal: restore the previous `PATH` entry without changing
      workspace or user data.
- [ ] Uninstall: remove the `PATH` entry and installed version directories
      without deleting notebooks, workspace configuration, or user
      configuration.

## zsh completion rehearsal

Generate completion from the installed `mn` of the same release version:

```console
mkdir -p "$HOME/.local/share/zsh/site-functions"
mn completion zsh > "$HOME/.local/share/zsh/site-functions/_mn"
```

- [ ] Add the directory to `fpath` before `compinit`, then start a new zsh.
- [ ] Complete the root command, a representative subcommand, `--workspace`,
      and one fixed value such as `--on-conflict replace`.
- [ ] Regenerate `_mn` after an update and verify it came from the new binary.
- [ ] Remove `_mn` during uninstall and confirm no stale completion remains.

## Release evidence

Record release-specific results in the draft GitHub Release body or another
release record. The completed instance of this checklist does not need to be
committed to the repository.

```text
Tag and commit:
Draft Release URL:
Archive names:
SHA256SUMS checked by:

Linux distribution and version:
Linux shell and zsh version:
Linux automated smoke result:
Linux runtime-free/manual result:

Windows edition and version:
PowerShell version:
Windows automated smoke result:
Windows runtime-free/manual result:

Install/update/uninstall result:
Completion result:
Known limitations or deviations:
Checked by and date:
```

Do not publish the draft Release while a native job, checksum, runtime-free
check, or required manual item is incomplete. Record an explicit deviation and
create a new patch release when a published tag or asset must be corrected.
