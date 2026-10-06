using MemoriaNote.Application;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies command handlers through replaceable CLI boundaries.</summary>
[TestFixture]
public sealed class CommandHandlerTests
{
    /// <summary>Verifies that the new-page handler uses separate editor options.</summary>
    [Test]
    public async Task New_UsesExternalEditorOptionsWithoutLegacyConfiguration()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        var editorOptions = new EditorOptions(true, "EDITOR", "configured-editor");
        var notebookPath = Path.Combine(
            Path.GetTempPath(),
            $"command-handler-{Guid.NewGuid():N}.db");
        var notebook = new Notebook(notebookPath);
        var workspace = new Workspace("command-handler", new[] { notebook }, notebook);
        var application = new StubApplicationService();
        var session = new ApplicationSession(workspace, application);
        var editorOptionsProvider = new StubEditorOptionsProvider(editorOptions);
        var externalEditor = new StubExternalEditor(
            ExternalEditorResult.Changed("Roadmap body"));
        var handler = new NewCommandHandler(
            executor,
            editorOptionsProvider,
            new StubNotebookTargetSessionResolver(session),
            externalEditor,
            output);

        var result = await handler.ExecuteAsync(
            "Roadmap",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(editorOptionsProvider.LoadCount, Is.EqualTo(1));
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

    sealed class StubEditorOptionsProvider : IEditorOptionsProvider
    {
        readonly EditorOptions _options;

        internal StubEditorOptionsProvider(EditorOptions options)
        {
            _options = options;
        }

        internal int LoadCount { get; private set; }

        public EditorOptions Load()
        {
            LoadCount++;
            return _options;
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
            EditorOptions options,
            ExternalEditorCommand commandOverride,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Documents.Add(document);
            return Task.FromResult(_result);
        }
    }
}
