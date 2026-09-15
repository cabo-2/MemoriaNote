using MemoriaNote.Core.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies notebook metadata validation without presentation-specific messages.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NotebookMetadataValidationPolicyTests
{
    /// <summary>Verifies empty and whitespace names are rejected first.</summary>
    /// <param name="name">The invalid notebook name.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Validate_EmptyName_ReturnsNameRequired(string? name)
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("current", "Current Title");
        var workspace = new Workspace(null, new[] { notebook }, notebook);
        var update = CreateUpdate(name!, "Updated Title");

        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookId.FromDatabasePath(notebook.DatabasePath),
            update,
            workspace);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.NameRequired
        }));
    }

    /// <summary>Verifies an empty title is rejected after a valid name.</summary>
    /// <param name="title">The invalid notebook title.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Validate_EmptyTitle_ReturnsTitleRequired(string? title)
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("current", "Current Title");
        var workspace = new Workspace(null, new[] { notebook }, notebook);
        var update = CreateUpdate("updated", title!);

        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookId.FromDatabasePath(notebook.DatabasePath),
            update,
            workspace);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.TitleRequired
        }));
    }

    /// <summary>Verifies a name already used by another notebook is rejected.</summary>
    [Test]
    public void Validate_DuplicateName_ReturnsDuplicateName()
    {
        using var currentDatabase = new TemporaryNotebookDatabase();
        using var otherDatabase = new TemporaryNotebookDatabase();
        var current = currentDatabase.CreateNotebook("current", "Current Title");
        var other = otherDatabase.CreateNotebook("duplicate", "Other Title");
        var workspace = new Workspace(null, new[] { current, other }, current);
        var update = CreateUpdate("duplicate", "Updated Title");

        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookId.FromDatabasePath(current.DatabasePath),
            update,
            workspace);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.DuplicateName
        }));
    }

    /// <summary>
    /// Verifies another object for the current notebook is excluded by normalized identity.
    /// </summary>
    [Test]
    public void Validate_SameNotebookIdInDifferentObject_HasNoErrors()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("current", "Current Title");
        var sameNotebook = new Notebook(database.DatabasePath);
        var workspace = new Workspace(
            null,
            new[] { notebook, sameNotebook },
            notebook);
        var update = CreateUpdate("current", "Updated Title");

        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookId.FromDatabasePath(notebook.DatabasePath),
            update,
            workspace);

        Assert.That(errors, Is.Empty);
    }

    /// <summary>Verifies name comparison remains ordinal and case-sensitive.</summary>
    [Test]
    public void Validate_NameWithDifferentCase_HasNoErrors()
    {
        using var currentDatabase = new TemporaryNotebookDatabase();
        using var otherDatabase = new TemporaryNotebookDatabase();
        var current = currentDatabase.CreateNotebook("current", "Current Title");
        var other = otherDatabase.CreateNotebook("Notebook", "Other Title");
        var workspace = new Workspace(null, new[] { current, other }, current);
        var update = CreateUpdate("notebook", "Updated Title");

        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookId.FromDatabasePath(current.DatabasePath),
            update,
            workspace);

        Assert.That(errors, Is.Empty);
    }

    static NotebookMetadataUpdate CreateUpdate(string name, string title)
    {
        return new NotebookMetadataUpdate(
            name,
            title,
            "version",
            null!,
            null!,
            false,
            null!,
            default);
    }
}
