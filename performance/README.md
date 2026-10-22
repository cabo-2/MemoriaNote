# MemoriaNote published performance harness

The harness measures a published `mn` executable as a black-box process. Representative performance
runs are local-only diagnostics; GitHub Actions runs only bounded functional tests for the harness.

## Build and test

From the repository root:

```console
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

## Describe a run

Publish or extract a supported artifact first, then inspect the selected scenarios without measuring
them:

```console
dotnet run --project tools/MemoriaNote.Performance -- describe \
  --scenario-manifest performance/scenarios/scenario-manifest-v1.json \
  --executable /path/to/publish/mn \
  --artifact-id v0.2.0-linux-x64-self-contained \
  --product-version 0.2.0 \
  --source-commit 3ddede8 \
  --rid linux-x64 \
  --deployment-kind self-contained \
  --artifact-input-kind publish-directory \
  --environment-label linux-reference-1
```

## Run and validate

Use the same options with `run`, add an output path, and validate the generated evidence:

```console
dotnet run --project tools/MemoriaNote.Performance -- run \
  --scenario-manifest performance/scenarios/scenario-manifest-v1.json \
  --executable /path/to/publish/mn \
  --artifact-id v0.2.0-linux-x64-self-contained \
  --product-version 0.2.0 \
  --source-commit 3ddede8 \
  --rid linux-x64 \
  --deployment-kind self-contained \
  --artifact-input-kind publish-directory \
  --environment-label linux-reference-1 \
  --output /path/to/results/v0.2.0-linux-x64.json

dotnet run --project tools/MemoriaNote.Performance -- validate-result \
  --result /path/to/results/v0.2.0-linux-x64.json
```

Use repeated `--scenario <id>` options for a subset. Dataset-backed scenarios additionally require
`--dataset-manifest`; `--dataset-root` may override the manifest-relative prepared directory. The
runner never downloads a missing dataset.

Prepared directories use `tree-sha256-v1`: enumerate files recursively, normalize relative path
separators to `/`, sort paths with ordinal comparison, and append the UTF-8 bytes of
`<relative-path>`, one NUL byte, the lowercase SHA-256 hex of the file, and one LF byte for each
entry. The manifest's `prepared.sha256` is the lowercase SHA-256 of that complete byte sequence;
`prepared.totalBytes` is the sum of all file sizes.

For an extracted archive, set `--artifact-input-kind extracted-archive` and pass the original archive
with `--archive` so its SHA-256 is recorded. Use
`performance/scenarios/scenario-manifest-v1.extracted-archive.json`; each manifest explicitly declares
the measured artifact input kind.

## Measurement contract

- Setup, dataset verification, validation, and cleanup are outside the measured interval.
- Timing starts immediately before process creation and stops after process exit and stdout/stderr
  drain.
- Every sample starts a new process.
- One timeout, unexpected exit, or output mismatch invalidates the scenario; failed samples are not
  removed as outliers.
- Valid results retain all raw samples and nearest-rank p50/p95 summaries.
- Machine-specific absolute paths, command output, hostnames, and user names are not stored in the
  result.

The checked-in `synthetic-small` dataset is for harness correctness only. Representative English and
Japanese datasets and initial baselines belong to `ECLI-1010`.
