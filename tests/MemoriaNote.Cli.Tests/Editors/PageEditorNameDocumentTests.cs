using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies the compatibility text used for page name editing.</summary>
[TestFixture]
public sealed class PageEditorNameDocumentTests
{
    /// <summary>Verifies the existing instruction text for each name operation.</summary>
    [TestCase(EditorMode.Create, "#### Enter a name to be created ####")]
    [TestCase(EditorMode.Rename, "#### Enter the name to be renamed ####")]
    [TestCase(EditorMode.Delete, "#### Enter the name to be deleted ####")]
    public void Create_UsesExistingInstruction(EditorMode mode, string instruction)
    {
        var result = PageEditorNameDocument.Create("Page name", mode);

        Assert.That(
            result,
            Is.EqualTo(
                "Page name" + Environment.NewLine +
                Environment.NewLine +
                instruction + Environment.NewLine));
    }

    /// <summary>Verifies that the first non-empty non-comment line is returned.</summary>
    [Test]
    public void ReadName_ReturnsFirstEnteredName()
    {
        var document = Environment.NewLine +
            "Renamed page" + Environment.NewLine +
            Environment.NewLine +
            "#### instruction ####";

        var result = PageEditorNameDocument.ReadName(document);

        Assert.That(result, Is.EqualTo("Renamed page"));
    }

    /// <summary>Verifies that leaving only the instruction produces an empty name.</summary>
    [Test]
    public void ReadName_CommentFirst_ReturnsEmptyName()
    {
        var result = PageEditorNameDocument.ReadName(
            Environment.NewLine + "#### instruction ####");

        Assert.That(result, Is.Empty);
    }
}
