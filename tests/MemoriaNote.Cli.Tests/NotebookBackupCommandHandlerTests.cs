using MemoriaNote.Archive;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies safety checks at the notebook backup command boundary.</summary>
[TestFixture]
public sealed class NotebookBackupCommandHandlerTests
{
    /// <summary>
    /// Verifies that an omitted archive never writes binary data to an attached terminal.
    /// </summary>
    [Test]
    public async Task Execute_StandardOutputTerminal_RejectsBeforeResolvingNotebook()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"MemoriaNote.BackupHandler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var temporaryFiles = new TemporaryFileStore(
                Path.Combine(root, "temporary"));
            using var binaryOutput = new MemoryStream();
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();
            var output = new ConsoleCommandOutput(standardOutput, standardError);
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            var notebook = new Notebook(Path.Combine(root, "source.mnote"));
            var resolver = new StubNotebookTargetSessionResolver(
                new ApplicationSession(
                    new Workspace("test", new[] { notebook }, notebook),
                    new StubApplicationService()));
            var databaseFactory = new SqliteNotebookDbContextFactory(
                NullLoggerFactory.Instance);
            var service = new ArchiveV1BackupService(
                databaseFactory,
                temporaryFiles,
                SystemClock.Instance,
                new ArchiveV1Creator("MemoriaNote.Tests", "1.0.0"));
            var handler = new NotebookBackupCommandHandler(
                executor,
                resolver,
                service,
                new StubBinaryStandardOutput(binaryOutput, isTerminal: true),
                output);

            var result = await handler.ExecuteAsync(
                null,
                null,
                null,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
                Assert.That(resolver.ResolveCount, Is.Zero);
                Assert.That(binaryOutput.Length, Is.Zero);
                Assert.That(standardOutput.ToString(), Is.Empty);
                Assert.That(
                    standardError.ToString(),
                    Does.Contain("Refusing to write a binary archive to the terminal"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    sealed class StubBinaryStandardOutput : IBinaryStandardOutput
    {
        internal StubBinaryStandardOutput(Stream stream, bool isTerminal)
        {
            Stream = stream;
            IsTerminal = isTerminal;
        }

        public bool IsTerminal { get; }

        public Stream Stream { get; }
    }
}
