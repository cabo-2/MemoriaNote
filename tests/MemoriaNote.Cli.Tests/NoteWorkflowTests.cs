using System.Text.Json;
using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies representative note workflows through the CLI process boundary.
/// </summary>
[TestFixture]
public sealed class NoteWorkflowTests
{
    /// <summary>
    /// Verifies that note creation and selection persist across CLI processes.
    /// </summary>
    [Test]
    public async Task WorkCreateSelectAndList_PersistsStateAcrossProcesses()
    {
        using var harness = new CliProcessHarness();
        const string noteName = "ideas";
        const string noteTitle = "Project Ideas";
        var notePath = Path.GetFullPath(Path.Combine(
            harness.ApplicationDataDirectory,
            noteName + ".db"));

        var createResult = await harness.RunAsync(
            "work",
            "create",
            noteName,
            noteTitle);

        AssertSucceeded(createResult);
        Assert.That(File.Exists(notePath), Is.True);

        var initialListResult = await harness.RunAsync("work", "list");

        AssertSucceeded(initialListResult);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(initialListResult.StandardOutput, Does.Contain("note (Notepad)"));
            Assert.That(
                initialListResult.StandardOutput,
                Does.Contain($"{noteName} ({noteTitle})"));
        }

        var selectResult = await harness.RunAsync("work", "select", noteName);

        AssertSucceeded(selectResult);

        var selectedListResult = await harness.RunAsync("work", "list");

        AssertSucceeded(selectedListResult);
        Assert.That(
            ContainsSelectedNote(selectedListResult.StandardOutput, noteName, noteTitle),
            Is.True,
            "The selected note was not marked in a later CLI process.");

        using var configuration = await ReadConfigurationAsync(harness.ConfigurationPath);
        var root = configuration.RootElement;
        var workgroup = root.GetProperty("Workgroup");
        var dataSources = GetStringValues(root.GetProperty("DataSources"));
        var workgroupDataSources = GetStringValues(
            workgroup.GetProperty("UseDataSources"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                workgroup.GetProperty("SelectedNoteName").GetString(),
                Is.EqualTo(noteName));
            Assert.That(dataSources, Does.Contain(notePath));
            Assert.That(workgroupDataSources, Does.Contain(notePath));
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    static void AssertSucceeded(CliProcessResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    static bool ContainsSelectedNote(string output, string name, string title)
    {
        var expectedNote = $"{name} ({title})";
        return output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line =>
                line.StartsWith("* ", StringComparison.Ordinal) &&
                line.Contains(expectedNote, StringComparison.Ordinal));
    }

    static async Task<JsonDocument> ReadConfigurationAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream);
    }

    static string?[] GetStringValues(JsonElement values)
    {
        return values.EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
    }
}
