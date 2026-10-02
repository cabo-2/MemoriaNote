using MemoriaNote.Archive;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies restore command behavior at injectable standard-input boundaries.</summary>
[TestFixture]
public sealed class NotebookRestoreCommandHandlerTests
{
    /// <summary>Verifies terminal input is rejected before archive bytes are read.</summary>
    [Test]
    public async Task StandardInput_WhenTerminal_IsRejectedBeforeReading()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"MemoriaNote-restore-handler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var temporaryFiles = new TemporaryFileStore(
                Path.Combine(root, "temporary"));
            using var standardInput = new ThrowingReadStream();
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();
            var output = new ConsoleCommandOutput(standardOutput, standardError);
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            var service = new ArchiveV1RestoreService(
                new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance),
                temporaryFiles);
            var handler = new NotebookRestoreCommandHandler(
                executor,
                service,
                new StubBinaryStandardInput(standardInput, isTerminal: true),
                output);

            var result = await handler.ExecuteAsync(
                root,
                "restored",
                null,
                dryRun: false,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
                Assert.That(standardInput.ReadAttempted, Is.False);
                Assert.That(File.Exists(Path.Combine(root, "restored.mnote")), Is.False);
                Assert.That(standardOutput.ToString(), Is.Empty);
                Assert.That(
                    standardError.ToString(),
                    Does.Contain("Refusing to read a binary archive from the terminal"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    sealed class StubBinaryStandardInput : IBinaryStandardInput
    {
        internal StubBinaryStandardInput(Stream stream, bool isTerminal)
        {
            Stream = stream;
            IsTerminal = isTerminal;
        }

        public bool IsTerminal { get; }

        public Stream Stream { get; }
    }

    sealed class ThrowingReadStream : MemoryStream
    {
        internal bool ReadAttempted { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttempted = true;
            throw new IOException("The stream must not be read.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadAttempted = true;
            throw new IOException("The stream must not be read.");
        }
    }
}
