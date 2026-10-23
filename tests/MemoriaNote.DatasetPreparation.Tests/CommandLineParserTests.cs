using MemoriaNote.DatasetPreparation.CommandLine;

namespace MemoriaNote.DatasetPreparation.Tests;

/// <summary>Verifies the dataset preparation command-line contract.</summary>
[TestFixture]
public sealed class CommandLineParserTests
{
    /// <summary>Verifies prepare accepts explicit local roots and lock updates.</summary>
    [Test]
    public void Parse_Prepare_ReturnsOptions()
    {
        var command = (PrepareCommand)CommandLineParser.Parse(
        [
            "prepare",
            "--manifest", "dataset.json",
            "--cache-root", "cache",
            "--source-root", "source",
            "--dataset-root", "prepared",
            "--update-lock"
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(command.Options.SourceRoot, Is.EqualTo("source"));
            Assert.That(command.Options.DatasetRoot, Is.EqualTo("prepared"));
            Assert.That(command.Options.UpdateLock, Is.True);
        }
    }

    /// <summary>Verifies verify cannot modify a committed lock.</summary>
    [Test]
    public void Parse_VerifyWithUpdateLock_IsRejected()
    {
        Assert.That(
            () => CommandLineParser.Parse(
                ["verify", "--manifest", "dataset.json", "--cache-root", "cache", "--update-lock"]),
            Throws.TypeOf<CommandLineException>());
    }
}
