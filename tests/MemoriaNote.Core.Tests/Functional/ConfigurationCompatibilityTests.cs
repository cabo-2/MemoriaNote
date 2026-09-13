using System.Text.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies compatibility of serialized application configuration.
/// </summary>
[TestFixture]
[Category("Functional")]
public sealed class ConfigurationCompatibilityTests
{
    /// <summary>
    /// Verifies that legacy workspace keys load into renamed properties and remain
    /// unchanged when serialized again.
    /// </summary>
    [Test]
    public void LegacyWorkspaceProperties_RoundTripWithOriginalJsonNames()
    {
        const string legacyJson =
            "{" +
            "\"DataSources\":[\"catalog.db\"]," +
            "\"Workgroup\":{" +
            "\"Name\":\"Research\"," +
            "\"SelectedNoteName\":\"primary\"," +
            "\"UseDataSources\":[\"primary.db\",\"archive.db\"]" +
            "}," +
            "\"DefaultWorkgroupName\":\"My Legacy Notes\"," +
            "\"DefaultNoteName\":\"legacy-note\"," +
            "\"DefaultNoteTitle\":\"Legacy Note\"" +
            "}";

        var configuration = JsonConvert.DeserializeObject<Configuration>(legacyJson);

        Assert.That(configuration, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration!.Workspace.Name, Is.EqualTo("Research"));
            Assert.That(
                configuration.Workspace.SelectedNotebookName,
                Is.EqualTo("primary"));
            Assert.That(
                configuration.Workspace.NotebookDatabasePaths,
                Is.EqualTo(new[] { "primary.db", "archive.db" }));
            Assert.That(
                configuration.DefaultWorkspaceName,
                Is.EqualTo("My Legacy Notes"));
            Assert.That(configuration.DefaultNotebookName, Is.EqualTo("legacy-note"));
            Assert.That(configuration.DefaultNotebookTitle, Is.EqualTo("Legacy Note"));
        }

        using var document = JsonDocument.Parse(
            JsonConvert.SerializeObject(configuration));
        var root = document.RootElement;
        var workspace = root.GetProperty("Workgroup");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.TryGetProperty("Workspace", out _), Is.False);
            Assert.That(
                root.GetProperty("DefaultWorkgroupName").GetString(),
                Is.EqualTo("My Legacy Notes"));
            Assert.That(
                root.TryGetProperty("DefaultWorkspaceName", out _),
                Is.False);
            Assert.That(
                root.GetProperty("DefaultNoteName").GetString(),
                Is.EqualTo("legacy-note"));
            Assert.That(
                root.TryGetProperty("DefaultNotebookName", out _),
                Is.False);
            Assert.That(
                root.GetProperty("DefaultNoteTitle").GetString(),
                Is.EqualTo("Legacy Note"));
            Assert.That(
                root.TryGetProperty("DefaultNotebookTitle", out _),
                Is.False);
            Assert.That(
                workspace.GetProperty("SelectedNoteName").GetString(),
                Is.EqualTo("primary"));
            Assert.That(
                workspace.TryGetProperty("SelectedNotebookName", out _),
                Is.False);
            Assert.That(
                GetStringValues(workspace.GetProperty("UseDataSources")),
                Is.EqualTo(new[] { "primary.db", "archive.db" }));
            Assert.That(
                workspace.TryGetProperty("NotebookDatabasePaths", out _),
                Is.False);
        }
    }

    static string?[] GetStringValues(JsonElement values)
    {
        return values.EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
    }
}
