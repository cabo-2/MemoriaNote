using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies the compatibility document exchanged with the notebook metadata editor.
/// </summary>
[TestFixture]
public sealed class NotebookMetadataEditDocumentTests
{
    /// <summary>Verifies the legacy JSON property names remain available.</summary>
    [Test]
    public void Serialize_EditDocument_PreservesLegacyProperties()
    {
        var document = new NotebookMetadataEditDocument
        {
            Name = "notebook",
            Title = "Notebook Title",
            Version = "1.0",
            Description = "Description",
            Author = "Author",
            ReadOnly = true,
            Tag = "tag",
            CreateTime = new DateTime(2026, 4, 5, 6, 7, 8),
            DataSource = "/notebooks/notebook.db"
        };

        var serialized = JsonConvert.SerializeObject(document);
        var properties = JObject.Parse(serialized)
            .Properties()
            .Select(property => property.Name);

        Assert.That(properties, Is.EqualTo(new[]
        {
            "Name",
            "Title",
            "Version",
            "Description",
            "Author",
            "ReadOnly",
            "Tag",
            "CreateTime",
            "DataSource"
        }));
    }

    /// <summary>
    /// Verifies the compatibility data-source field cannot change a metadata update target.
    /// </summary>
    [Test]
    public void ToUpdate_ChangedDataSource_MapsOnlyMetadataValues()
    {
        const string serialized =
            "{\"Name\":\"updated-name\"," +
            "\"Title\":\"Updated Title\"," +
            "\"Version\":\"2.0\"," +
            "\"Description\":\"Updated description\"," +
            "\"Author\":\"Updated author\"," +
            "\"ReadOnly\":true," +
            "\"Tag\":\"updated-tag\"," +
            "\"CreateTime\":\"2026-04-05T06:07:08\"," +
            "\"DataSource\":\"/different/notebook.db\"}";
        var document = JsonConvert
            .DeserializeObject<NotebookMetadataEditDocument>(serialized)!;

        var update = document.ToUpdate();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.Name, Is.EqualTo("updated-name"));
            Assert.That(update.Title, Is.EqualTo("Updated Title"));
            Assert.That(update.Version, Is.EqualTo("2.0"));
            Assert.That(update.Description, Is.EqualTo("Updated description"));
            Assert.That(update.Author, Is.EqualTo("Updated author"));
            Assert.That(update.ReadOnly, Is.True);
            Assert.That(update.Tag, Is.EqualTo("updated-tag"));
            Assert.That(update.CreateTime, Is.EqualTo(new DateTime(2026, 4, 5, 6, 7, 8)));
            Assert.That(
                typeof(NotebookMetadataUpdate).GetProperty("DataSource"),
                Is.Null);
            Assert.That(
                typeof(NotebookMetadataUpdate).GetProperty("DatabasePath"),
                Is.Null);
        }
    }
}
