using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies explicit workspace notebook selection.</summary>
[TestFixture]
public sealed class WorkspaceSelectionUseCaseTests
{
    /// <summary>Verifies a notebook is validated before its selection is saved.</summary>
    [Test]
    public async Task SelectAsync_NewSelection_ValidatesAndSaves()
    {
        var store = new RecordingStore();
        var validator = new RecordingValidator();
        var useCase = new WorkspaceSelectionUseCase(validator, store);
        var notebook = NotebookFileName.FromInput("work");
        var workspacePath = Path.GetFullPath("workspace");

        var changed = await useCase.SelectAsync(
            workspacePath,
            notebook,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True);
            Assert.That(
                validator.DatabasePath,
                Is.EqualTo(Path.Combine(workspacePath, "work.mnote")));
            Assert.That(store.Saved?.CurrentNotebook, Is.EqualTo(notebook));
        }
    }

    /// <summary>Verifies reselecting the same notebook validates but does not rewrite.</summary>
    [Test]
    public async Task SelectAsync_CurrentSelection_DoesNotSave()
    {
        var notebook = NotebookFileName.FromInput("work");
        var store = new RecordingStore
        {
            Loaded = new WorkspaceConfigurationLoadResult(
                WorkspaceConfiguration.CreateSelected(notebook),
                WorkspaceConfigurationLoadStatus.Loaded)
        };
        var validator = new RecordingValidator();
        var useCase = new WorkspaceSelectionUseCase(validator, store);

        var changed = await useCase.SelectAsync(
            "/workspace",
            notebook,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.False);
            Assert.That(validator.CallCount, Is.EqualTo(1));
            Assert.That(store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies a validation failure leaves the old selection untouched.</summary>
    [Test]
    public void SelectAsync_InvalidNotebook_DoesNotSave()
    {
        var store = new RecordingStore();
        var validator = new RecordingValidator
        {
            Exception = new InvalidDataException("invalid notebook")
        };
        var useCase = new WorkspaceSelectionUseCase(validator, store);

        Assert.That(
            async () => await useCase.SelectAsync(
                "/workspace",
                NotebookFileName.FromInput("invalid"),
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(store.SaveCount, Is.Zero);
    }

    /// <summary>Verifies root selection does not create a missing configuration.</summary>
    [Test]
    public void SelectRoot_MissingConfiguration_DoesNotSave()
    {
        var store = new RecordingStore();
        var useCase = new WorkspaceSelectionUseCase(new RecordingValidator(), store);

        var changed = useCase.SelectRoot("/workspace");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies root selection clears an existing notebook selection.</summary>
    [Test]
    public void SelectRoot_SelectedConfiguration_SavesRoot()
    {
        var store = new RecordingStore
        {
            Loaded = new WorkspaceConfigurationLoadResult(
                WorkspaceConfiguration.CreateSelected(
                    NotebookFileName.FromInput("work")),
                WorkspaceConfigurationLoadStatus.Loaded)
        };
        var useCase = new WorkspaceSelectionUseCase(new RecordingValidator(), store);

        var changed = useCase.SelectRoot("/workspace");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True);
            Assert.That(store.Saved?.CurrentNotebook, Is.Null);
        }
    }

    sealed class RecordingStore : IWorkspaceConfigurationStore
    {
        internal WorkspaceConfigurationLoadResult Loaded { get; set; } =
            new(
                WorkspaceConfiguration.CreateRoot(),
                WorkspaceConfigurationLoadStatus.Missing);

        internal int SaveCount { get; private set; }

        internal WorkspaceConfiguration? Saved { get; private set; }

        public WorkspaceConfigurationLoadResult Load(string workspacePath)
        {
            return Loaded;
        }

        public void Save(string workspacePath, WorkspaceConfiguration configuration)
        {
            SaveCount++;
            Saved = configuration;
        }
    }

    sealed class RecordingValidator : INotebookFormatValidator
    {
        internal string? DatabasePath { get; private set; }

        internal int CallCount { get; private set; }

        internal Exception? Exception { get; set; }

        public Task<NotebookMetadataResult> ValidateCurrentAsync(
            string databasePath,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DatabasePath = databasePath;
            CallCount++;
            if (Exception != null)
                return Task.FromException<NotebookMetadataResult>(Exception);

            return Task.FromResult<NotebookMetadataResult>(null!);
        }
    }
}
