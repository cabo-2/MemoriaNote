# Published performance runner

`tools/MemoriaNote.Performance` measures a published `mn` executable as a black-box process. The
Python [checkpoint workflow](README.md) is the normal operator entry point; the commands below are
for developing or diagnosing the runner itself.

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

Use repeated `--scenario <id>` options to select a subset. Dataset-backed scenarios additionally
require `--dataset-manifest`; `--dataset-root` may override the manifest-relative prepared directory.
The runner never downloads a missing dataset.

For an extracted archive, set `--artifact-input-kind extracted-archive` and pass the original archive
with `--archive` so its SHA-256 is recorded. Use
`performance/scenarios/scenario-manifest-v1.extracted-archive.json`; each manifest explicitly
declares the measured artifact input kind.

## Measurement contract

- Setup, dataset verification, validation, and cleanup are outside the measured interval.
- Timing starts immediately before process creation and stops after process exit and stdout/stderr
  drain.
- Every sample starts a new process.
- One timeout, unexpected exit, or output mismatch invalidates the scenario; failed samples are not
  removed as outliers.
- Valid results retain all raw samples and nearest-rank p50/p95 summaries.
- Machine-specific absolute paths, command output, host names, and user names are not stored in the
  result.

The checked-in `synthetic-small` dataset is for harness correctness only. Representative English and
Japanese datasets and initial baselines belong to `ECLI-1010`.

## Prepared dataset identity

Prepared directories use `tree-sha256-v1`: enumerate files recursively, normalize relative path
separators to `/`, sort paths with ordinal comparison, and append the UTF-8 bytes of
`<relative-path>`, one NUL byte, the lowercase SHA-256 hex of the file, and one LF byte for each
entry. The manifest's `prepared.sha256` is the lowercase SHA-256 of that complete byte sequence;
`prepared.totalBytes` is the sum of all file sizes.

## 10,000-page listing scenarios

The ECLI-1010c manifests measure `ls --limit 10000` against notebooks populated from the largest
locked English and Japanese scales. The `v0.2.0` baseline creates schema v1 notebooks; later
checkpoints create the current format owned by the measured artifact:

| Dataset | Scenario manifest |
| --- | --- |
| `dotnet-docs-en` | `performance/scenarios/scenario-manifest-v1.dotnet-docs-en-pages-10000.extracted-archive.json` |
| `mdn-translated-content-ja` | `performance/scenarios/scenario-manifest-v1.mdn-translated-content-ja-pages-10000.extracted-archive.json` |

Run each manifest with its matching `--dataset-manifest` and full prepared directory as
`--dataset-root`. The runner verifies the complete prepared tree before arranging the scenario; the
manifest resolves the measured scale below that root as `{dataset}/scales/pages-10000`.

Each scenario creates a notebook and imports the 10,000 text files once outside the measured
interval. The same closed notebook is reopened by a new CLI process for three unmeasured warm-ups and
30 measured `ls` invocations. The scenario contract applies one 600,000 ms timeout to both arrange
and measured commands. One invalid warm-up or sample invalidates the scenario.

Use `describe` before `run` and inspect the manifest to confirm the dataset ID and scale,
`warm-reopen` cache mode, `reuse-state` reset policy, repetitions, and timeout. Representative runs
remain local-only; CI validates only the manifest and locked dataset contracts with bounded tests.
