# Archive v1 examples

The `valid` directory represents the root of an extracted archive v1. Placing its three files at the root of a ZIP
container produces a representative valid archive. The sizes and SHA-256 digests in the manifest match the payload
bytes stored in this repository.

Files in the `invalid` directory are individual entry examples for specific validation rules. They are not intended
to be combined into one ZIP container.

| File | Expected rejection reason |
| --- | --- |
| `manifest-unsupported-archive-version.json` | Unsupported archive format version |
| `metadata-duplicate-key.json` | Duplicate `Name` metadata key |
| `page-missing-text.ndjson` | Missing required `text` property |
| `pages-duplicate-uuid.ndjson` | Duplicate page UUID |

When replacing a payload to build a semantic-validation fixture, update the manifest's `recordCount`,
`uncompressedByteLength`, and `sha256` values to match the replacement. Doing so prevents a checksum mismatch from
masking the validation rule that the fixture is intended to exercise.
