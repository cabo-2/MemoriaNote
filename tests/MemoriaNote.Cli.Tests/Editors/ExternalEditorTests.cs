using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies orchestration of external editor boundaries.</summary>
[TestFixture]
public sealed class ExternalEditorTests
{
    /// <summary>Verifies that the resolved executable edits the exchange file.</summary>
    [Test]
    public async Task EditAsync_ConnectsResolverExchangeAndProcessRunner()
    {
        var configuration = new ConfigurationCli
        {
            Terminal = new ConfigurationCli.TerminalSetting
            {
                EditorEnv = true,
                EditorPath = "configured-editor"
            }
        };
        var exchange = new StubEditorFileExchange("/tmp/Page Draft \"one\".txt");
        var processRunner = new StubExternalEditorProcessRunner();
        var editor = new ExternalEditor(
            new EditorExecutableResolver(
                new StubEnvironmentVariableSource("environment-editor")),
            exchange,
            processRunner);
        var document = new ExternalEditorDocument("Page Draft.txt", "before");

        var result = await editor.EditAsync(
            configuration,
            null,
            document,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exchange.Document, Is.SameAs(document));
            Assert.That(processRunner.ExecutablePath, Is.EqualTo("environment-editor"));
            Assert.That(processRunner.DocumentPath, Is.EqualTo(exchange.ExchangePath));
            Assert.That(result.IsChanged, Is.True);
            Assert.That(result.Text, Is.EqualTo("after"));
        }
    }

    sealed class StubEnvironmentVariableSource : IEnvironmentVariableSource
    {
        readonly string _value;

        internal StubEnvironmentVariableSource(string value)
        {
            _value = value;
        }

        public string Get(string name)
        {
            return _value;
        }
    }

    sealed class StubEditorFileExchange : IEditorFileExchange
    {
        internal StubEditorFileExchange(string exchangePath)
        {
            ExchangePath = exchangePath;
        }

        internal string ExchangePath { get; }

        internal ExternalEditorDocument? Document { get; private set; }

        public async Task<ExternalEditorResult> EditAsync(
            ExternalEditorDocument document,
            Func<string, CancellationToken, Task> editFile,
            CancellationToken cancellationToken)
        {
            Document = document;
            await editFile(ExchangePath, cancellationToken);
            return ExternalEditorResult.Changed("after");
        }
    }

    sealed class StubExternalEditorProcessRunner : IExternalEditorProcessRunner
    {
        internal string? ExecutablePath { get; private set; }

        internal string? DocumentPath { get; private set; }

        public Task RunAsync(
            ExternalEditorCommand command,
            string documentPath,
            CancellationToken cancellationToken)
        {
            ExecutablePath = command.ExecutablePath;
            DocumentPath = documentPath;
            return Task.CompletedTask;
        }
    }
}
