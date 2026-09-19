using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the create-notebook command boundary.</summary>
[TestFixture]
public sealed class CreateNotebookCommandHandlerTests
{
    /// <summary>
    /// Verifies that cancellation is reported without invoking notebook persistence.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_WhenPreCanceled_ReturnsCanceledWithoutCreatingNotebook()
    {
        using var workspace = new TemporaryDirectory();
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        var migrator = new RecordingNotebookMigrator();
        var handler = new CreateNotebookCommandHandler(
            executor,
            new CreateNotebookUseCase(migrator),
            output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await handler.ExecuteAsync(
            workspace.Path,
            "work.mnote",
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(migrator.CreateCallCount, Is.Zero);
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Error: Operation was canceled" + Environment.NewLine));
            Assert.That(
                File.Exists(System.IO.Path.Combine(workspace.Path, "work.mnote")),
                Is.False);
        }
    }

    sealed class RecordingNotebookMigrator : INotebookMigrator
    {
        internal int CreateCallCount { get; private set; }

        public Task<Notebook> CreateAsync(
            string name,
            string title,
            string databasePath,
            CancellationToken token)
        {
            CreateCallCount++;
            throw new InvalidOperationException("Persistence should not be invoked.");
        }

        public Task MigrateAsync(string databasePath, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.CreateCommandTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
