using System.Text;
using McMaster.Extensions.CommandLineUtils;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies zsh completion generation from production command metadata.</summary>
[TestFixture]
public sealed class ZshCompletionGeneratorTests
{
    /// <summary>Verifies command metadata and completion-only hints are represented.</summary>
    [Test]
    public void Generate_ForProductionTree_ContainsCommandsOptionsAndHints()
    {
        var script = GenerateProductionScript();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                script,
                Does.Contain(
                    "'completion:Generate static shell completion scripts'"));
            Assert.That(script, Does.Contain("_mn_notebooks_import()"));
            Assert.That(
                script,
                Does.Contain("'(--workspace)--workspace[Use this directory as " +
                    "the workspace for this invocation]:directory:_directories'"));
            Assert.That(script, Does.Contain("'1:archive:_files'"));
            Assert.That(script, Does.Contain("'1:directory:_directories'"));
            Assert.That(script, Does.Contain(":policy:(fail skip replace)'"));
            Assert.That(script, Does.Contain(":policy:(fail id-suffix)'"));
            Assert.That(script, Does.Contain(":value:(true false)'"));
            Assert.That(
                script,
                Does.Contain("'*--editor-arg[Pass an argument to --editor; " +
                    "may be repeated]:argument:'"));
            Assert.That(
                script,
                Does.Contain("'(-l --long)-l[Show all page metadata columns]'"));
            Assert.That(
                script,
                Does.Contain("'(-l --long)--long[Show all page metadata columns]'"));
            Assert.That(script, Does.Contain(":notebook:'"));
        }
    }

    /// <summary>Verifies obsolete commands and dynamic data access are not generated.</summary>
    [Test]
    public void Generate_ForProductionTree_OmitsLegacyAndDynamicCompletion()
    {
        var script = GenerateProductionScript();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Not.Contain("--completion"));
            Assert.That(script, Does.Not.Contain("mn work list"));
            Assert.That(script, Does.Not.Contain("'work:"));
            Assert.That(script, Does.Not.Contain("'find:"));
            Assert.That(script, Does.Not.Contain("'page:"));
            Assert.That(script, Does.Not.Contain("_call_program"));
        }
    }

    /// <summary>Verifies generated text is deterministic and platform independent.</summary>
    [Test]
    public void Generate_ForSameTree_IsDeterministicLfText()
    {
        var first = GenerateProductionScript();
        var second = GenerateProductionScript();
        var bytes = Encoding.UTF8.GetBytes(first);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.StartWith("#compdef mn\n"));
            Assert.That(first, Does.EndWith("\n"));
            Assert.That(first, Does.Not.Contain("\r"));
            Assert.That(bytes, Is.Not.Empty);
            Assert.That(
                bytes.Take(3),
                Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }));
        }
    }

    /// <summary>Verifies descriptions are normalized and safely single-quoted.</summary>
    [Test]
    public void Escaping_WithWhitespaceQuotesAndMetacharacters_IsSafe()
    {
        const string description = "two  words\nwith 'quote': [value]\\path";

        var escaped = ZshCompletionGenerator.EscapeDescription(description);
        var quoted = ZshCompletionGenerator.QuoteWord(escaped);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                escaped,
                Is.EqualTo("two words with 'quote'\\: \\[value\\]\\\\path"));
            Assert.That(quoted, Does.StartWith("'two words"));
            Assert.That(quoted, Does.Contain("'\\''quote'\\''"));
            Assert.That(quoted, Does.Not.Contain("\n"));
        }
    }

    static string GenerateProductionScript()
    {
        using var output = new StringWriter();
        using var application = CliCommandApplicationFactory.Create(
            PhysicalConsole.Singleton,
            Directory.GetCurrentDirectory(),
            output);
        return ZshCompletionGenerator.Generate(application);
    }
}
