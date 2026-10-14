# Development Guidance for Memoria Note

This project uses C# targeting **.NET 10**.

## Project Structure

- `core/` – production library providing the data model, database access, and services
- `cli/` – production console application depending on `core`
- `tests/` – automated test projects for `core` and `cli`
- `docs/` – local design explorations and work notes; excluded from Git and not a home for canonical documentation
- `reference/` – version-controlled specifications and external-facing documentation

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

Before committing code changes or marking a pull request ready, run the following verification commands outside the
sandbox from the repository root. MSBuild and VSTest use local sockets for interprocess communication, which can be
blocked by the sandbox and cause verification to fail independently of the source or tests:

```bash
dotnet build MemoriaNote.sln
dotnet test MemoriaNote.sln --no-build
```

The required test command runs the full suite. GitHub Actions runs the same full suite on native Ubuntu and Windows
runners.

### Test Categories

Test categories describe test responsibilities, but they do not partition the required verification or select an
operating system:

- `Process` identifies tests that start `dotnet mn.dll` to verify process-boundary contracts such as raw standard I/O,
  inherited environment and current directory, external processes, and clean process state.
- `CliIntegration` identifies tests that invoke the production CLI entry point in the test process to verify the parser,
  composition, SQLite, file system, and text I/O workflow.

Category filters may be used for focused diagnosis or optional measurement:

```bash
dotnet test MemoriaNote.sln --no-build --filter "TestCategory=Process"
dotnet test MemoriaNote.sln --no-build --filter "TestCategory=CliIntegration"
```

Filtered runs do not replace the required full-suite verification. Process tests remain non-parallel, and in-process CLI
integration fixtures that replace process-global state use `[NonParallelizable]` and the shared invocation gate.

## Spelling

[cspell](https://cspell.org/) is not part of the required pre-commit checks. Do not run it routinely before committing; run it only when specifically requested.

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
- Build and test results, including anything not run and the reason.
- Compatibility or migration notes when the database schema, backup format, configuration format, target framework, or dependencies change.

Keep framework and package upgrades in dedicated pull requests. Update production and test projects together when changing the target framework, while preserving the existing test suite; update the test platform and other tooling separately when practical.
