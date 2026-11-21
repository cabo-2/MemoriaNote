using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies incremental page-list handler failure behavior.</summary>
[TestFixture]
public sealed class PageListCommandHandlerTests
{
    /// <summary>Verifies cancellation after one batch reports incomplete partial output.</summary>
    [Test]
    public async Task ExecuteAsync_CanceledAfterOutput_ReturnsPartialAndDisposesQuery()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("stream.mnote");
        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(databasePath, "Stream"));
        await created.Value!.DisposeAsync();
        await SchemaV2TestNotebook.InsertPagesAsync(databasePath, 130);
        using var cancellation = new CancellationTokenSource();
        using var standardOutput = new CancelingTextWriter(cancellation);
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var handler = new ListCommandHandler(
            new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance),
            new NotebookTargetSessionResolver(new WorkspaceConfigurationStore()),
            output);

        var exitCode = await handler.ExecuteAsync(
            directory.Path,
            "stream",
            pageNamePattern: null!,
            sort: null!,
            reverse: false,
            limit: null,
            longFormat: false,
            cancellationToken: cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(
                standardOutput.Lines,
                Has.Count.EqualTo(128));
            Assert.That(
                standardError.ToString(),
                Is.EqualTo(
                    "Listing failed after 128 page(s); output is incomplete." +
                    Environment.NewLine));
        }

        File.Move(databasePath, directory.GetPath("moved.mnote"));
    }

    private sealed class CancelingTextWriter : StringWriter
    {
        private readonly CancellationTokenSource _cancellation;

        internal CancelingTextWriter(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        internal IReadOnlyList<string> Lines => ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            _cancellation.Cancel();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.PageListHandler.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
