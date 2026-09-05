# Development Guidance for Memoria Note

This project uses C# targeting **.NET 6**. The repository currently hosts two production projects:

- `core/` – library providing the data model, database access and services
- `cli/` – console application depending on `core`

The project is expected to move to **.NET 10** after the baseline functional test suite is in place. Do not combine that framework upgrade with unrelated refactoring or feature work.

## Development Workflow

- Keep `main` in a buildable and testable state.
- Make repository changes on a short-lived branch and merge them through a pull request. Do not commit directly to `main`.
- Keep each pull request focused on one logical, independently reversible change.
- Use branch names such as `test/core-000-integration-harness`, `fix/core-010-note-ownership`, or `docs/update-development-guidance`.
- Reference the applicable roadmap ID in the branch name or pull request description. A separate GitHub issue is optional unless additional discussion or tracking is useful.
- Prefer draft pull requests for work that benefits from early visibility, but only mark them ready when the intended scope and verification are complete.
- Required reviewer approval is not expected for solo development. Automated checks must pass when CI is available.
- Merge pull requests with GitHub's **Squash and merge** option so that each pull request becomes one logical commit on `main`, then delete the merged branch. Do not use **Create a merge commit** or **Rebase and merge** unless a specific change requires it.

## Build Verification

Before committing code changes or marking a pull request ready, run the following commands from the repository root:

```bash
cd core && dotnet build && cd ..
cd cli && dotnet build && cd ..
```

If tests are present under `tests/`, also run `dotnet test`.

When a root solution is added, prefer building and testing the solution from the repository root so that all projects are verified together.

## Spelling

Check source files with [cspell](https://cspell.org/):

```bash
npx cspell --config cspell.json "**/*.cs"
```

Correct any reported typos.

## Commit Messages

Write commit messages with a short summary line (<60 chars), followed by a blank line and an optional detailed description.

Work-in-progress commits may be concise because pull requests are squash merged. Ensure the final squash commit message follows this rule.

## Coding Style

- Use **4 spaces** for indentation.
- CamelCase for method and variable names.
- Public members should use XML documentation comments.

## Pull Request Notes

Each pull request should include:

- A concise summary of the change and its motivation.
- The roadmap ID or any issue it addresses.
- Build, test, and spell-check results, including anything not run and the reason.
- Compatibility or migration notes when the database schema, backup format, configuration format, target framework, or dependencies change.

Keep framework and package upgrades in dedicated pull requests. For the planned .NET 10 migration, first retarget the production and test projects while preserving the existing test suite, then update the test platform and other tooling separately when practical.
