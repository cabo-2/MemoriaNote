# Performance dataset preparation

`tools/MemoriaNote.DatasetPreparation` materializes and verifies the fixed English and Japanese
datasets used by the published performance harness. The Python [checkpoint workflow](README.md) is
the normal operator entry point; the commands below are for maintaining or diagnosing dataset
preparation itself.

Source and prepared content remain under the ignored `performance/.cache/` directory and are never
committed. The first `prepare` command may use the network to shallow-fetch and sparse-checkout the
pinned Git commit. Subsequent preparation reuses the checkout. Verification is always offline.

## English dataset

The `dotnet-docs-en` manifest pins a full `dotnet/docs` commit and records the selected Markdown tree,
transformation, nested scales, licenses, statistics, and prepared tree checksum:

```console
dotnet run --project tools/MemoriaNote.DatasetPreparation -- prepare \
  --manifest performance/datasets/dotnet-docs-en/dataset-manifest.json \
  --cache-root performance/.cache

dotnet run --project tools/MemoriaNote.DatasetPreparation -- verify \
  --manifest performance/datasets/dotnet-docs-en/dataset-manifest.json \
  --cache-root performance/.cache
```

The prepared root contains `catalog.ndjson`, deterministic statistics, and flat UTF-8 text
directories under `scales/pages-100`, `scales/pages-1000`, and `scales/pages-10000`. The preparation
tool refuses to replace a directory unless it contains the ownership marker for the same dataset ID
and version.

## Japanese dataset

The `mdn-translated-content-ja` manifest pins a full `mdn/translated-content` commit and selects
Japanese Markdown below `files/ja`. Conflicting and orphaned pages are excluded. MDN `slug` metadata
provides stable page names and categories, while `title` supplies display titles. Source paths and
content hashes remain in the generated local catalog for attribution and reproducibility.

```console
dotnet run --project tools/MemoriaNote.DatasetPreparation -- prepare \
  --manifest performance/datasets/mdn-translated-content-ja/dataset-manifest.json \
  --cache-root performance/.cache

dotnet run --project tools/MemoriaNote.DatasetPreparation -- verify \
  --manifest performance/datasets/mdn-translated-content-ja/dataset-manifest.json \
  --cache-root performance/.cache
```

The prepared Japanese corpus contains the same nested `pages-100`, `pages-1000`, and `pages-10000`
scales as the English corpus.

## Maintenance commands

Use `inventory` to inspect a pinned source without writing prepared pages. Reserve
`prepare --update-lock` for an intentional dataset revision or transformation-version change, and
review every resulting manifest checksum and statistic before committing it.
