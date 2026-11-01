# Archive v1 content model v2 draft examples

These examples belong to the ECLI-1130a draft reference package. They describe the candidate tuple `(1, 2, "2")` but
do not indicate released reader, writer, backup, or restore support.

The `valid` directory represents the root of an extracted candidate archive. Placing its three files at the root of a
ZIP container produces a representative valid archive. The sizes and SHA-256 digests in the manifest match the exact
payload bytes stored in this repository.

The `compatibility` directories are immutable review and implementation fixtures:

- `empty` represents a notebook with no pages;
- `single-page` represents the minimal non-empty streaming and round-trip case;
- `full-field` represents multiple pages and covers every CM2 page property, required and unknown metadata, null and
  empty optional metadata values, Unicode, multiline text, canonical tags, JSON escapes, and signed 64-bit boundary
  values.

A future compatibility test should consume these repository bytes directly rather than copying or regenerating them.

Files directly in the `invalid` directory are individual entry examples for specific validation rules. They are not
intended to be combined with each other into one ZIP container.

| File | Expected rejection reason |
| --- | --- |
| `manifest-checksum-mismatch.json` | Payload SHA-256 mismatch when combined with the valid payloads |
| `manifest-mixed-source-version.json` | Unsupported mixed tuple `(1, 2, "1")` |
| `manifest-unsupported-content-model-version.json` | Unsupported content model version `3` |
| `metadata-duplicate-key.json` | Duplicate `Name` metadata key |
| `page-invalid-timestamp.ndjson` | Invalid Gregorian calendar date, despite matching the lexical pattern |
| `page-missing-text.ndjson` | Missing required `text` property |
| `page-noncanonical-page-id.ndjson` | Page ID contains uppercase hexadecimal text |
| `page-noncanonical-tags.ndjson` | Tags contain optional whitespace and are not in canonical UTF-8 byte order |
| `pages-duplicate-page-id.ndjson` | Duplicate stable Page ID |

When replacing a payload to build a semantic-validation fixture, update the manifest's `recordCount`,
`uncompressedByteLength`, and `sha256` values to match the replacement. The checksum-mismatch example is the sole
intentional exception. Keeping every unrelated manifest value correct prevents an earlier count or length failure
from masking the validation rule that a fixture is intended to exercise.
