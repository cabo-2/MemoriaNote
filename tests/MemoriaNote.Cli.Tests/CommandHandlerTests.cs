using MemoriaNote.Application;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies command handlers through replaceable CLI boundaries.</summary>
[TestFixture]
public sealed class CommandHandlerTests
{
    /// <summary>Verifies that the new-page handler uses the stateless editor flow.</summary>
    [Test]
    public async Task New_UsesExternalEditorAndPersistsConfigurationAfterCreation()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        var configuration = new ConfigurationCli();
        var notebookPath = Path.Combine(
            Path.GetTempPath(),
            $"command-handler-{Guid.NewGuid():N}.db");
        var notebook = new Notebook(notebookPath);
        var workspace = new Workspace("command-handler", new[] { notebook }, notebook);
        var application = new StubApplicationService();
        var session = new ApplicationSession(workspace, application);
        var contextFactory = new StubCommandContextFactory(
            configuration,
            session);
        var externalEditor = new StubExternalEditor(
            ExternalEditorResult.Changed("Roadmap body"));
        var handler = new NewCommandHandler(
            executor,
            contextFactory,
            externalEditor,
            output);

        var result = await handler.ExecuteAsync(
            "Roadmap",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(contextFactory.SaveCount, Is.EqualTo(1));
            Assert.That(application.ValidateCreateAsyncCallCount, Is.EqualTo(2));
            Assert.That(application.CreateAsyncCallCount, Is.EqualTo(1));
            Assert.That(externalEditor.Documents, Has.Count.EqualTo(1));
            Assert.That(externalEditor.Documents[0].FileName, Is.EqualTo("Roadmap"));
            Assert.That(externalEditor.Documents[0].Text, Is.Empty);
            Assert.That(
                standardOutput.ToString(),
                Does.Contain("The text created successfully."));
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    sealed class StubCommandContextFactory : ICliCommandContextFactory
    {
        readonly ConfigurationCli _configuration;
        readonly ApplicationSession _session;

        internal StubCommandContextFactory(
            ConfigurationCli configuration,
            ApplicationSession session)
        {
            _configuration = configuration;
            _session = session;
        }

        internal int SaveCount { get; private set; }

        public ConfigurationCli LoadConfiguration()
        {
            return _configuration;
        }

        public Task<ApplicationSession> CreateSessionAsync(
            ConfigurationCli configuration,
            CancellationToken cancellationToken)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_session);
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            Assert.That(configuration, Is.SameAs(_configuration));
            SaveCount++;
        }
    }

    sealed class StubExternalEditor : IExternalEditor
    {
        readonly ExternalEditorResult _result;

        internal StubExternalEditor(ExternalEditorResult result)
        {
            _result = result;
        }

        internal List<ExternalEditorDocument> Documents { get; } = new();

        public Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Documents.Add(document);
            return Task.FromResult(_result);
        }
    }
}
