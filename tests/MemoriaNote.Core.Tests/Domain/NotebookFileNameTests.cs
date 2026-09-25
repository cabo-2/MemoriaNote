using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies normalization and validation of workspace notebook leaf names.</summary>
[TestFixture]
public sealed class NotebookFileNameTests
{
    /// <summary>Verifies a missing suffix is completed and an exact suffix is retained.</summary>
    [TestCase("work", "work.mnote")]
    [TestCase("work.mnote", "work.mnote")]
    [TestCase("root", "root.mnote")]
    public void FromInput_ValidLeafName_ReturnsNormalizedName(
        string input,
        string expected)
    {
        var fileName = NotebookFileName.FromInput(input);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fileName.Value, Is.EqualTo(expected));
            Assert.That(fileName.ToString(), Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies normalized names use ordinal value semantics.</summary>
    [Test]
    public void Equality_NormalizedNames_UsesOrdinalComparison()
    {
        var first = NotebookFileName.FromInput("work");
        var second = NotebookFileName.FromInput("work.mnote");
        var differentCase = NotebookFileName.FromInput("WORK");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first == second, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(differentCase));
        }
    }

    /// <summary>Verifies missing, path-like, reserved, and invalid suffix inputs are rejected.</summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("nested/work")]
    [TestCase("nested\\work")]
    [TestCase("work.MNOTE")]
    [TestCase("work.db")]
    [TestCase("--root")]
    [TestCase("--root.mnote")]
    public void FromInput_InvalidLeafName_Throws(string? input)
    {
        Assert.That(
            () => NotebookFileName.FromInput(input!),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies a null input is rejected with its specific argument exception.</summary>
    [Test]
    public void FromInput_Null_ThrowsArgumentNullException()
    {
        Assert.That(
            () => NotebookFileName.FromInput(null!),
            Throws.TypeOf<ArgumentNullException>());
    }
}
