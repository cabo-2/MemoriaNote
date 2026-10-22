using System.Text;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies stable process exit classification for the harness entry point.</summary>
[TestFixture]
public sealed class PerformanceApplicationTests
{
    /// <summary>Verifies a structurally invalid result uses the invalid-result exit code.</summary>
    [Test]
    public async Task ValidateResult_InvalidSchema_ReturnsInvalidResult()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "invalid.json");
        await File.WriteAllTextAsync(path, "{}", Encoding.UTF8);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new PerformanceApplication(output, error).RunAsync(
            ["validate-result", "--result", path]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(PerformanceApplication.InvalidResultExitCode));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("JSON Schema"));
        }
    }

    /// <summary>Verifies missing run arguments use the usage exit code.</summary>
    [Test]
    public async Task Run_MissingOptions_ReturnsUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await new PerformanceApplication(output, error).RunAsync(["run"]);

        Assert.That(exitCode, Is.EqualTo(PerformanceApplication.UsageExitCode));
    }
}
