using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies metadata command checks that precede notebook resolution.</summary>
[TestFixture]
public sealed class NotebookMetadataCommandHandlerTests
{
    /// <summary>Verifies multiple updates are rejected before resolving a notebook.</summary>
    [Test]
    public async Task Execute_MultipleUpdateOptions_ReturnsValidationBeforeResolution()
    {
        var fixture = CreateFixture(isInteractive: false);

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "title",
            "description",
            null,
            null,
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Input.ReadToEndCount, Is.Zero);
            Assert.That(fixture.StandardOutput.ToString(), Is.Empty);
            Assert.That(
                fixture.StandardError.ToString(),
                Does.Contain("only one metadata update option"));
        }
    }

    /// <summary>Verifies redirected input is required before resolving the target.</summary>
    [Test]
    public async Task Execute_StandardInputMarkerOnTerminal_ReturnsValidationBeforeResolution()
    {
        var fixture = CreateFixture(isInteractive: true);

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            "-",
            null,
            null,
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Input.ReadToEndCount, Is.Zero);
            Assert.That(fixture.StandardOutput.ToString(), Is.Empty);
            Assert.That(fixture.StandardError.ToString(), Does.Contain("Redirect or pipe"));
        }
    }

    /// <summary>Verifies invalid read-only input is rejected before target resolution.</summary>
    [TestCase("")]
    [TestCase("True")]
    [TestCase("writable")]
    [TestCase("-")]
    public async Task Execute_InvalidReadOnly_ReturnsValidationBeforeResolution(string value)
    {
        var fixture = CreateFixture(isInteractive: false);

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            value,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Input.ReadToEndCount, Is.Zero);
            Assert.That(fixture.StandardError.ToString(), Does.Contain("true or false"));
        }
    }

    static Fixture CreateFixture(bool isInteractive)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"MemoriaNote.MetadataHandler-{Guid.NewGuid():N}");
        var notebook = new Notebook(Path.Combine(root, "test.mnote"));
        var resolver = new StubNotebookTargetSessionResolver(
            new ApplicationSession(
                new Workspace("test", new[] { notebook }, notebook),
                new StubApplicationService()));
        var input = new RecordingInput(isInteractive);
        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        var handler = new NotebookMetadataCommandHandler(
            executor,
            resolver,
            input,
            output);
        return new Fixture(
            handler,
            resolver,
            input,
            standardOutput,
            standardError);
    }

    sealed class RecordingInput : ICommandInput
    {
        internal RecordingInput(bool isInteractive)
        {
            IsInteractive = isInteractive;
        }

        public bool IsInteractive { get; }

        internal int ReadToEndCount { get; private set; }

        public string ReadLine() => throw new InvalidOperationException();

        public Task<string> ReadLineAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            ReadToEndCount++;
            return Task.FromResult(string.Empty);
        }
    }

    sealed record Fixture(
        NotebookMetadataCommandHandler Handler,
        StubNotebookTargetSessionResolver Resolver,
        RecordingInput Input,
        StringWriter StandardOutput,
        StringWriter StandardError);
}
