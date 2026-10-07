using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies the user configuration TOML schema and canonical encoding.</summary>
[TestFixture]
public sealed class UserConfigurationCodecTests
{
    /// <summary>Verifies all editor selections serialize to canonical version 1 TOML.</summary>
    [Test]
    public void Serialize_EditorSelections_UsesCanonicalToml()
    {
        var environment = UserConfigurationCodec.Serialize(
            new UserConfigurationModel(
                UserEditorConfiguration.FromEnvironment(
                    "EDITOR",
                    new[] { "--wait", "{file}" })));
        var program = UserConfigurationCodec.Serialize(
            new UserConfigurationModel(
                UserEditorConfiguration.FromProgram(
                    "code",
                    new[] { "--wait", "{file}" })));
        var unset = UserConfigurationCodec.Serialize(
            new UserConfigurationModel(UserEditorConfiguration.Unset));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                environment,
                Is.EqualTo(
                    "format_version = 1\n\n" +
                    "[editor]\n" +
                    "selection = \"environment\"\n" +
                    "variable = \"EDITOR\"\n" +
                    "arguments = [\"--wait\", \"{file}\"]\n"));
            Assert.That(
                program,
                Is.EqualTo(
                    "format_version = 1\n\n" +
                    "[editor]\n" +
                    "selection = \"program\"\n" +
                    "executable = \"code\"\n" +
                    "arguments = [\"--wait\", \"{file}\"]\n"));
            Assert.That(
                unset,
                Is.EqualTo(
                    "format_version = 1\n\n" +
                    "[editor]\n" +
                    "selection = \"unset\"\n"));
        }
    }

    /// <summary>Verifies all editor selections deserialize without implicit fallback.</summary>
    [Test]
    public void Deserialize_EditorSelections_CreatesExclusiveModels()
    {
        var environment = UserConfigurationCodec.Deserialize(
            "format_version = 1\n[editor]\nselection = \"environment\"\n" +
            "variable = \"VISUAL\"\narguments = [\"--wait\"]\n");
        var program = UserConfigurationCodec.Deserialize(
            "format_version = 1\n[editor]\nselection = \"program\"\n" +
            "executable = \"code\"\n");
        var unset = UserConfigurationCodec.Deserialize(
            "format_version = 1\n[editor]\nselection = \"unset\"\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(environment.FormatVersion, Is.EqualTo(1));
            Assert.That(
                environment.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Environment));
            Assert.That(environment.Editor.Variable, Is.EqualTo("VISUAL"));
            Assert.That(environment.Editor.Executable, Is.Null);
            Assert.That(environment.Editor.Arguments, Is.EqualTo(new[] { "--wait" }));

            Assert.That(program.Editor.Selection, Is.EqualTo(UserEditorSelection.Program));
            Assert.That(program.Editor.Variable, Is.Null);
            Assert.That(program.Editor.Executable, Is.EqualTo("code"));
            Assert.That(program.Editor.Arguments, Is.Empty);

            Assert.That(unset.Editor.Selection, Is.EqualTo(UserEditorSelection.Unset));
            Assert.That(unset.Editor.Variable, Is.Null);
            Assert.That(unset.Editor.Executable, Is.Null);
            Assert.That(unset.Editor.Arguments, Is.Empty);
        }
    }

    /// <summary>Verifies unknown data is tolerated but not retained by canonical serialization.</summary>
    [Test]
    public void Deserialize_UnknownData_IgnoresIt()
    {
        var configuration = UserConfigurationCodec.Deserialize(
            "# retained only until save\n" +
            "format_version = 1\n" +
            "future_root = true\n" +
            "[editor]\n" +
            "selection = \"program\"\n" +
            "executable = \"code\"\n" +
            "future_editor = 17\n" +
            "[future]\n" +
            "value = \"ignored\"\n");

        var serialized = UserConfigurationCodec.Serialize(configuration);

        Assert.That(
            serialized,
            Is.EqualTo(
                "format_version = 1\n\n" +
                "[editor]\n" +
                "selection = \"program\"\n" +
                "executable = \"code\"\n" +
                "arguments = []\n"));
    }

    /// <summary>Verifies malformed and contradictory version 1 documents are rejected.</summary>
    [TestCase("")]
    [TestCase("format_version =\n")]
    [TestCase("format_version = \"1\"\n[editor]\nselection = \"unset\"\n")]
    [TestCase("format_version = 0\n[editor]\nselection = \"unset\"\n")]
    [TestCase("format_version = 1\n")]
    [TestCase("format_version = 1\neditor = \"unset\"\n")]
    [TestCase("format_version = 1\n[editor]\n")]
    [TestCase("format_version = 1\n[editor]\nselection = 1\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"default\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\nvariable = 1\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\nvariable = \" \"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\nvariable = \"EDITOR=code\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\nvariable = \"EDIT\\u0000OR\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"environment\"\nvariable = \"EDITOR\"\nexecutable = \"code\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = 1\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \" \"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \"co\\u0000de\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \"code\"\nvariable = \"EDITOR\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \"code\"\narguments = \"--wait\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \"code\"\narguments = [1]\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"program\"\nexecutable = \"code\"\narguments = [\"a\\u0000b\"]\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"unset\"\nvariable = \"EDITOR\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"unset\"\nexecutable = \"code\"\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"unset\"\narguments = []\n")]
    public void Deserialize_InvalidConfiguration_Throws(string content)
    {
        Assert.That(
            () => UserConfigurationCodec.Deserialize(content),
            Throws.TypeOf<UserConfigurationFormatException>());
    }

    /// <summary>Verifies future versions have a distinct failure type.</summary>
    [Test]
    public void Deserialize_FutureVersion_ThrowsUnsupportedVersion()
    {
        var exception = Assert.Throws<UnsupportedUserConfigurationVersionException>(() =>
            UserConfigurationCodec.Deserialize(
                "format_version = 2\n[editor]\nselection = \"unset\"\n"));

        Assert.That(exception!.FormatVersion, Is.EqualTo(2));
    }
}
