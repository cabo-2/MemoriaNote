using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies page operation classification at the CLI boundary.</summary>
[TestFixture]
public sealed class PageCommandResultMapperTests
{
    /// <summary>Verifies that an empty name remains a validation failure.</summary>
    [Test]
    public void ToCliResult_NameRequired_ReturnsValidation()
    {
        var operation = PageOperationResult.ValidationFailed(
            new[] { PageErrorCode.NameRequired });

        var result = PageCommandResultMapper.ToCliResult(
            PageOperationKind.Create,
            operation);

        Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Validation));
    }

    /// <summary>Verifies that a duplicate name is reported as a conflict.</summary>
    [Test]
    public void ToCliResult_DuplicateName_ReturnsConflict()
    {
        var operation = PageOperationResult.ValidationFailed(
            new[] { PageErrorCode.DuplicateName });

        var result = PageCommandResultMapper.ToCliResult(
            PageOperationKind.Create,
            operation);

        Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Conflict));
    }

    /// <summary>Verifies that conflict classification does not depend on error order.</summary>
    [Test]
    public void ToCliResult_MultipleErrorsContainingDuplicate_ReturnsConflict()
    {
        var operation = PageOperationResult.ValidationFailed(
            new[]
            {
                PageErrorCode.NameRequired,
                PageErrorCode.DuplicateName
            });

        var result = PageCommandResultMapper.ToCliResult(
            PageOperationKind.Create,
            operation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Conflict));
            Assert.That(
                result.Message,
                Does.StartWith(
                    "The text name have not been entered." + Environment.NewLine));
            Assert.That(result.Message, Does.EndWith("The text name is already in use."));
        }
    }

    /// <summary>Verifies that a read-only notebook is reported as a conflict.</summary>
    [Test]
    public void ToCliResult_ReadOnly_ReturnsConflict()
    {
        var operation = PageOperationResult.Failed(
            PageOperationStatus.ReadOnly,
            PageErrorCode.ReadOnly);

        var result = PageCommandResultMapper.ToCliResult(
            PageOperationKind.Edit,
            operation);

        Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Conflict));
    }
}
