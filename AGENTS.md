# Development Guidance for Memoria Note

This project uses C# targeting **.NET 6**. The repository hosts two projects:

- `core/` – library providing the data model, database access and services
- `cli/` – console application depending on `core`

## Build Verification

Before committing code changes run the following commands from the repository root:

```bash
cd core && dotnet build && cd ..
cd cli && dotnet build && cd ..
```

If tests are added under a `test/` directory, also run `dotnet test`.

## Spelling

Check source files with [cspell](https://cspell.org/):

```bash
npx cspell --config cspell.json "**/*.cs"
```

Correct any reported typos.

## Commit Messages

Write commit messages with a short summary line (<60 chars), followed by a blank line and an optional detailed description.

## Coding Style

- Use **4 spaces** for indentation.
- CamelCase for method and variable names.
- Public members should use XML documentation comments.

## Pull Request Notes

When opening a pull request, summarize the changes and mention any issues closed. Include build and spell-check status in the PR description.

