using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the shared mapping from CLI failures to stable exit codes.</summary>
[TestFixture]
public sealed class CliErrorMappingTests
{
    /// <summary>Verifies the exit code assigned to each explicit error category.</summary>
    [TestCaseSource(nameof(ErrorKinds))]
    public void Failure_MapsErrorKindToStableExitCode(
        object errorKindValue,
        int expectedExitCode)
    {
        var errorKind = (CliErrorKind)errorKindValue;
        var result = CliCommandResult.Failure(errorKind, "failure");

        Assert.That((int)result.ExitCode, Is.EqualTo(expectedExitCode));
    }

    /// <summary>Verifies storage exception classification.</summary>
    [TestCaseSource(nameof(StorageExceptions))]
    public void Map_StorageExceptionReturnsStorageFailure(Exception exception)
    {
        var result = new CliErrorMapper().Map(exception, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Storage));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Storage));
            Assert.That(result.Exception, Is.SameAs(exception));
        }
    }

    /// <summary>Verifies known user-facing exception classification.</summary>
    [TestCaseSource(nameof(KnownFailures))]
    public void Map_KnownFailureReturnsExpectedExitCode(
        Exception exception,
        int expectedExitCode)
    {
        var result = new CliErrorMapper().Map(exception, false);

        Assert.That((int)result.ExitCode, Is.EqualTo(expectedExitCode));
    }

    /// <summary>Verifies that requested cancellation has its conventional exit code.</summary>
    [Test]
    public void Map_RequestedCancellationReturnsCanceledFailure()
    {
        var exception = new OperationCanceledException();

        var result = new CliErrorMapper().Map(exception, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Canceled));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Canceled));
        }
    }

    /// <summary>Verifies that programming failures retain the fatal classification.</summary>
    [Test]
    public void Map_UnexpectedExceptionReturnsUnexpectedFailure()
    {
        var result = new CliErrorMapper().Map(
            new InvalidOperationException("broken"),
            false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Unexpected));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Unexpected));
        }
    }

    private static IEnumerable<Exception> StorageExceptions()
    {
        yield return new IOException("disk unavailable");
        yield return new UnauthorizedAccessException("access denied");
        yield return new StubDbException("database unavailable");
        yield return new DbUpdateException(
            "update failed",
            new IOException("disk unavailable"));
        yield return new AggregateException(
            new IOException("disk unavailable"),
            new StubDbException("database unavailable"));
    }

    private static IEnumerable<TestCaseData> ErrorKinds()
    {
        yield return ErrorKind(CliErrorKind.Validation, CliExitCode.Validation);
        yield return ErrorKind(CliErrorKind.NotFound, CliExitCode.NotFound);
        yield return ErrorKind(CliErrorKind.Conflict, CliExitCode.Conflict);
        yield return ErrorKind(CliErrorKind.Storage, CliExitCode.Storage);
        yield return ErrorKind(CliErrorKind.Canceled, CliExitCode.Canceled);
        yield return ErrorKind(CliErrorKind.Unexpected, CliExitCode.Unexpected);
    }

    private static TestCaseData ErrorKind(
        CliErrorKind errorKind,
        CliExitCode exitCode)
    {
        return new TestCaseData(errorKind, (int)exitCode)
            .SetName($"Failure_{errorKind}_Returns{(int)exitCode}");
    }

    private static IEnumerable<object[]> KnownFailures()
    {
        yield return new object[]
        {
            new FileNotFoundException("missing file"),
            (int)CliExitCode.NotFound
        };
        yield return new object[]
        {
            new DirectoryNotFoundException("missing directory"),
            (int)CliExitCode.NotFound
        };
        yield return new object[]
        {
            new InvalidDataException("invalid data"),
            (int)CliExitCode.Validation
        };
        yield return new object[]
        {
            new ConfigurationFormatException("invalid configuration"),
            (int)CliExitCode.Validation
        };
    }

    private sealed class StubDbException : DbException
    {
        internal StubDbException(string message)
            : base(message)
        {
        }
    }
}
