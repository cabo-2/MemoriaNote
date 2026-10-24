# MemoriaNote performance checkpoint workflow

`performance/scripts/checkpoint.py` is the operator entry point for published performance
checkpoints. It prepares the fixed inputs, runs the .NET performance harness, validates the results,
and generates an English Markdown report. Python does not perform timing; the .NET runner remains
the source of raw samples and summaries.

Representative measurements are local-only diagnostics. GitHub Actions runs only the bounded
Python tests, which use small synthetic fixtures and do not download public datasets or release
artifacts.

## Requirements

- Python 3.10 or later.
- The .NET 10 SDK.
- A native Linux or Windows x64 host.
- Network access for `prepare`. The `run` and `report` commands are offline.

The workflow rejects unsupported operating systems, an architecture or RID mismatch, and an
artifact profile for the other operating system. Run all commands from the repository root and use
the profile matching the host:

| Host | Artifact profile |
| --- | --- |
| Linux x64 | `performance/artifacts/v0.2.0-linux-x64.json` |
| Windows x64 | `performance/artifacts/v0.2.0-win-x64.json` |

## Test the workflow

The bounded unit tests cover Linux and Windows workflow branches with synthetic inputs:

```console
python -m unittest discover --start-directory performance/scripts/tests --pattern "test_*.py"
```

## Prepare inputs

Restore solution dependencies, prepare the English and Japanese datasets, and cache the official
release archive:

```console
python performance/scripts/checkpoint.py prepare \
  --artifact-profile performance/artifacts/v0.2.0-linux-x64.json
```

PowerShell accepts the same arguments when the command is entered on one line or written with its
line-continuation syntax. Use the Windows profile on a Windows host.

`prepare` is the only command that may use the network. Valid datasets and artifacts are reused. A
checksum or identity mismatch is an error: the workflow neither replaces the cached input nor
continues into a measurement.

Use `--artifact-input` to supply an already downloaded archive. Its bytes must still match the
profile checksum. Future local publish-directory profiles use the same option; machine-specific
paths are not stored in checked-in profiles.

## Run a checkpoint

Run the generic, English, and Japanese manifests as one checkpoint session:

```console
python performance/scripts/checkpoint.py run \
  --artifact-profile performance/artifacts/v0.2.0-linux-x64.json \
  --roadmap-id ECLI-1010c \
  --description "v0.2.0 initial baseline" \
  --environment-label linux-reference-1
```

`run` is offline. It verifies both datasets and the archive, builds the already restored solution in
`Release`, safely extracts the artifact into a new temporary directory, checks `mn --version`, and
runs and validates every configured manifest.

Raw results and `session.json` are written under a new
`performance/results/ecli-1010c/<session-id>/` directory. Existing sessions are never replaced. Use
`--keep-temporary` only when the isolated workspace is needed for troubleshooting.

## Generate the report

Use the session path printed by `run`:

```console
python performance/scripts/checkpoint.py report \
  --session performance/results/ecli-1010c/<session-id>/session.json
```

The default report path is
`performance/reports/<roadmap-id>/<session-id>.md`. The `performance/reports/` directory contains
local generated output and is not tracked by Git. The English report contains the minimum
environment, artifact, runner, manifest, dataset, and scenario summary fields that can be derived
automatically. It excludes raw samples, command output, host names, user names, and machine-specific
absolute paths. Result and report output paths must be new unless a different new path is selected.

Use `--output <path>` when a different new report path is required. A reviewed report intended to
become canonical documentation can therefore be generated explicitly outside the ignored directory.

## Internal component documentation

The Python workflow is the supported operator path. Maintainers working on its underlying .NET
components can consult the [performance runner](runner.md) and
[dataset preparation](dataset-preparation.md) references.
