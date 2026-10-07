using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies exclusive external editor configuration states.</summary>
[TestFixture]
public sealed class UserEditorConfigurationTests
{
    /// <summary>Verifies environment selection stores only its permitted values.</summary>
    [Test]
    public void FromEnvironment_CreatesExclusiveSelection()
    {
        var configuration = UserEditorConfiguration.FromEnvironment(
            "EDITOR",
            new[] { "--wait", "{file}" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                configuration.Selection,
                Is.EqualTo(UserEditorSelection.Environment));
            Assert.That(configuration.Variable, Is.EqualTo("EDITOR"));
            Assert.That(configuration.Executable, Is.Null);
            Assert.That(
                configuration.Arguments,
                Is.EqualTo(new[] { "--wait", "{file}" }));
        }
    }

    /// <summary>Verifies program selection stores only its permitted values.</summary>
    [Test]
    public void FromProgram_CreatesExclusiveSelection()
    {
        var configuration = UserEditorConfiguration.FromProgram(
            "code",
            new[] { "--wait" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                configuration.Selection,
                Is.EqualTo(UserEditorSelection.Program));
            Assert.That(configuration.Variable, Is.Null);
            Assert.That(configuration.Executable, Is.EqualTo("code"));
            Assert.That(configuration.Arguments, Is.EqualTo(new[] { "--wait" }));
        }
    }

    /// <summary>Verifies explicit unset is represented as a valid in-file selection.</summary>
    [Test]
    public void Unset_HasNoSelectionValues()
    {
        var configuration = UserEditorConfiguration.Unset;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.Selection, Is.EqualTo(UserEditorSelection.Unset));
            Assert.That(configuration.Variable, Is.Null);
            Assert.That(configuration.Executable, Is.Null);
            Assert.That(configuration.Arguments, Is.Empty);
        }
    }

    /// <summary>Verifies required environment variable values are rejected.</summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("EDIT\0OR")]
    [TestCase("EDITOR=code")]
    public void FromEnvironment_InvalidVariable_Throws(string variable)
    {
        Assert.That(
            () => UserEditorConfiguration.FromEnvironment(variable),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies a null environment variable name is rejected.</summary>
    [Test]
    public void FromEnvironment_NullVariable_Throws()
    {
        Assert.That(
            () => UserEditorConfiguration.FromEnvironment(null!),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies required executable values are rejected.</summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("co\0de")]
    public void FromProgram_InvalidExecutable_Throws(string executable)
    {
        Assert.That(
            () => UserEditorConfiguration.FromProgram(executable),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies a null executable is rejected.</summary>
    [Test]
    public void FromProgram_NullExecutable_Throws()
    {
        Assert.That(
            () => UserEditorConfiguration.FromProgram(null!),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies null and NUL arguments are rejected without rejecting empty arguments.</summary>
    [Test]
    public void EditorArguments_InvalidValue_Throws()
    {
        Assert.That(
            () => UserEditorConfiguration.FromProgram("code", new[] { (string)null! }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => UserEditorConfiguration.FromProgram("code", new[] { "a\0b" }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            UserEditorConfiguration.FromProgram("code", new[] { "" }).Arguments,
            Is.EqualTo(new[] { "" }));
    }

    /// <summary>Verifies caller-owned argument collections cannot mutate configuration.</summary>
    [Test]
    public void EditorArguments_AreCopied()
    {
        var arguments = new[] { "--wait" };
        var configuration = UserEditorConfiguration.FromProgram("code", arguments);

        arguments[0] = "--changed";

        Assert.That(configuration.Arguments, Is.EqualTo(new[] { "--wait" }));
    }
}
