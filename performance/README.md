# MemoriaNote published performance harness

The harness measures a published `mn` executable as a black-box process. Representative performance
runs are local-only diagnostics; GitHub Actions runs only bounded functional tests for the harness.

## Build and test

From the repository root:

```console
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

The checkpoint workflow has bounded Python unit tests that do not download public datasets or
release artifacts and do not run representative measurements:

```console
python -m unittest discover --start-directory performance/scripts/tests --pattern "test_*.py"
```

## Checkpoint workflow

`performance/scripts/checkpoint.py` prepares the fixed inputs, runs the existing .NET performance
runner, validates its results, and generates an English Markdown report. Python performs no timing;
the .NET runner remains the source of raw samples and summaries.

The workflow supports native Linux and Windows hosts. It rejects any other host OS, an architecture
or RID mismatch, and an artifact profile for the other OS before changing cached inputs. Use the
matching checked-in profile:

| Host | Artifact profile |
| --- | --- |
| Linux x64 | `performance/artifacts/v0.2.0-linux-x64.json` |
| Windows x64 | `performance/artifacts/v0.2.0-win-x64.json` |

First prepare both representative datasets and the official release archive. This is the only
workflow command that may use the network:

```console
python performance/scripts/checkpoint.py prepare \
  --artifact-profile performance/artifacts/v0.2.0-linux-x64.json
```

PowerShell uses the same arguments with its line-continuation syntax, or the command can be entered
on one line with the Windows profile. Valid prepared datasets and a valid cached artifact are reused.
A checksum or identity mismatch is an input error: the workflow does not overwrite the cache or
continue into a measurement.

Run the generic, English, and Japanese manifests as one checkpoint session:

```console
python performance/scripts/checkpoint.py run \
  --artifact-profile performance/artifacts/v0.2.0-linux-x64.json \
  --roadmap-id ECLI-1010c \
  --description "v0.2.0 initial baseline" \
  --environment-label linux-reference-1
```

`run` is offline. It verifies both datasets and the archive, builds the solution in `Release`,
extracts the artifact into a new temporary directory, checks `mn --version`, and invokes `describe`,
`run`, and `validate-result` for each manifest. Raw results and `session.json` are written below a
new `performance/results/ecli-1010c/<session-id>/` directory. Existing sessions are never replaced.

Generate a report after the session is valid, using the path printed by `run`:

```console
python performance/scripts/checkpoint.py report \
  --session performance/results/ecli-1010c/<session-id>/session.json
```

The default report path is below `docs/performance/`. The report contains the minimum environment,
artifact, runner, manifest, dataset, and scenario summary fields that can be derived automatically.
It does not include raw samples, command output, hostnames, user names, or machine-specific absolute
paths. Both result and report output paths must be new unless a different explicit path is selected.

Use `--artifact-input` with `prepare` and `run` to supply an already downloaded archive. The bytes
must still match the profile checksum. Future local publish-directory profiles also use this option;
machine-specific paths are never stored in a checked-in profile.

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

## Prepare the English dataset

The `dotnet-docs-en` manifest pins a full `dotnet/docs` commit and records the selected Markdown tree,
transformation, nested scales, licenses, statistics, and prepared tree checksum. Source and prepared
content are stored under the ignored `performance/.cache/` directory.

The first command may access the network to shallow-fetch and sparse-checkout the pinned Git commit.
Subsequent preparation reuses that checkout. Verification is always offline:

```console
dotnet run --project tools/MemoriaNote.DatasetPreparation -- prepare \
  --manifest performance/datasets/dotnet-docs-en/dataset-manifest.json \
  --cache-root performance/.cache

dotnet run --project tools/MemoriaNote.DatasetPreparation -- verify \
  --manifest performance/datasets/dotnet-docs-en/dataset-manifest.json \
  --cache-root performance/.cache
```

Use `inventory` to inspect a pinned source without writing prepared pages. `prepare --update-lock` is
reserved for an intentional dataset revision or transform-version change; review every resulting
manifest checksum and statistic before committing it.

The prepared root contains `catalog.ndjson`, deterministic statistics, and flat UTF-8 text directories
under `scales/pages-100`, `scales/pages-1000`, and `scales/pages-10000`. Pass the selected scale
directory to the published CLI's `notebooks import` command. The preparation tool refuses to replace a
directory unless it contains the ownership marker for the same dataset ID and version.

## Prepare the Japanese dataset

The `mdn-translated-content-ja` manifest pins a full `mdn/translated-content` commit and selects
Japanese Markdown below `files/ja`. Conflicting and orphaned pages are excluded. MDN `slug` metadata
provides stable page names and categories, while `title` supplies display titles. Source paths and
content hashes remain in the generated local catalog for attribution and reproducibility.

The first command may access the network to shallow-fetch and sparse-checkout the pinned Git commit.
Source articles, code examples, and prepared page text remain under the ignored
`performance/.cache/` directory and are never committed:

```console
dotnet run --project tools/MemoriaNote.DatasetPreparation -- prepare \
  --manifest performance/datasets/mdn-translated-content-ja/dataset-manifest.json \
  --cache-root performance/.cache

dotnet run --project tools/MemoriaNote.DatasetPreparation -- verify \
  --manifest performance/datasets/mdn-translated-content-ja/dataset-manifest.json \
  --cache-root performance/.cache
```

As with the English dataset, use `inventory` to inspect the pinned source and reserve
`prepare --update-lock` for intentional source or transform changes. The prepared Japanese corpus
contains nested `pages-100`, `pages-1000`, and `pages-10000` scales.

## Run the 10,000-page listing scenarios

The ECLI-1010c manifests measure `ls --limit 10000` against notebooks populated from the largest
locked English and Japanese scales. The `v0.2.0` baseline creates schema v1 notebooks; later
checkpoints create the current format owned by the measured artifact:

| Dataset | Scenario manifest |
| --- | --- |
| `dotnet-docs-en` | `performance/scenarios/scenario-manifest-v1.dotnet-docs-en-pages-10000.extracted-archive.json` |
| `mdn-translated-content-ja` | `performance/scenarios/scenario-manifest-v1.mdn-translated-content-ja-pages-10000.extracted-archive.json` |

Run each manifest separately with its matching `--dataset-manifest` and full prepared directory as
`--dataset-root`. The runner verifies the complete prepared tree before arranging the scenario; the
manifest resolves the measured scale below that root as `{dataset}/scales/pages-10000`.

Each scenario creates a notebook and imports the 10,000 text files once outside the measured interval.
The same closed notebook is then reopened by a new CLI process for three unmeasured warm-ups and 30
measured `ls` invocations. The existing scenario contract applies one 600,000 ms timeout to both
arrange and measured commands. One invalid warm-up or sample invalidates the scenario.

Use `describe` before `run` and inspect the manifest to confirm the dataset ID and scale,
`warm-reopen` cache mode, `reuse-state` reset policy, repetitions, and timeout. Representative dataset
runs remain local-only; CI validates only the manifest and locked dataset contracts with bounded tests.

## Create retained v0.2.0 schema v1 notebooks

The performance runner deletes its isolated workspace after a successful run. Create the notebooks
that will be retained for SchemaLab separately with the official `v0.2.0` executable. The prepared
corpora must first pass the `prepare` and offline `verify` commands in the English and Japanese
sections above.

From the repository root, set the executable and a new staging directory. Keep both locations under
ignored local storage and replace the placeholders before running the commands:

```bash
readonly ECLI_V020_MN="/path/to/mn-v0.2.0-linux-x64/mn"
readonly ECLI_V1_STAGE="performance/.cache/ecli-1010c/notebook-build/<session-id>"
readonly ECLI_V1_RETAIN="performance/.cache/notebooks/schema-v1/v0.2.0"

mkdir -p "$ECLI_V1_STAGE/dotnet" "$ECLI_V1_STAGE/mdn" "$ECLI_V1_RETAIN"
```

Create and import the English notebook:

```console
"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/dotnet" \
  create dotnet-docs-en-pages-10000

"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/dotnet" \
  notebooks import \
  performance/.cache/datasets/dotnet-docs-en/2026-10-22.1/prepared/scales/pages-10000 \
  --notebook dotnet-docs-en-pages-10000
```

Create and import the Japanese notebook:

```console
"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/mdn" \
  create mdn-translated-content-ja-pages-10000

"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/mdn" \
  notebooks import \
  performance/.cache/datasets/mdn-translated-content-ja/2026-10-24.1/prepared/scales/pages-10000 \
  --notebook mdn-translated-content-ja-pages-10000
```

Confirm that both files are recognized as ready file-based format `1` notebooks:

```console
"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/dotnet" notebooks list --long
"$ECLI_V020_MN" --workspace "$ECLI_V1_STAGE/mdn" notebooks list --long
```

After every CLI process has exited, confirm that no `*.mnote-wal` or `*.mnote-shm` file remains. Then
copy without replacing an existing retained input and record each checksum:

```console
test ! -e "$ECLI_V1_RETAIN/dotnet-docs-en-pages-10000.mnote"
test ! -e "$ECLI_V1_RETAIN/mdn-translated-content-ja-pages-10000.mnote"

cp "$ECLI_V1_STAGE/dotnet/dotnet-docs-en-pages-10000.mnote" \
  "$ECLI_V1_RETAIN/dotnet-docs-en-pages-10000.mnote"
cp "$ECLI_V1_STAGE/mdn/mdn-translated-content-ja-pages-10000.mnote" \
  "$ECLI_V1_RETAIN/mdn-translated-content-ja-pages-10000.mnote"

sha256sum "$ECLI_V1_RETAIN"/*.mnote
```

These notebooks contain upstream documentation and code examples. Keep them under
`performance/.cache/`; never commit the `.mnote` files or their extracted content.
