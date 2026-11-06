#nullable enable

using System;

namespace MemoriaNote.Application;

/// <summary>Classifies a predictable failure from the notebook application surface.</summary>
public enum NotebookFailureCategory
{
    /// <summary>The request or one of its logical values is invalid.</summary>
    Validation,

    /// <summary>The requested notebook or page was not found.</summary>
    NotFound,

    /// <summary>The operation conflicts with existing or concurrently changed data.</summary>
    Conflict,

    /// <summary>The notebook format or its validated content cannot be used.</summary>
    Format,

    /// <summary>The operation could not access the notebook or it was busy.</summary>
    AccessOrBusy,

    /// <summary>An unclassified storage failure prevented the operation.</summary>
    UnexpectedStorage
}

/// <summary>Describes a predictable application-surface failure without provider details.</summary>
public sealed class NotebookFailure
{
    internal NotebookFailure(
        NotebookFailureCategory category,
        string code,
        string message)
    {
        Category = category;
        Code = code;
        Message = message;
    }

    /// <summary>Gets the broad failure category.</summary>
    public NotebookFailureCategory Category { get; }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }

    /// <summary>Gets a safe diagnostic message that contains no SQL or connection string.</summary>
    public string Message { get; }
}

/// <summary>Contains either a successful operation value or a classified failure.</summary>
/// <typeparam name="T">The successful value type.</typeparam>
public sealed class NotebookOperationResult<T>
{
    private NotebookOperationResult(T? value, NotebookFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>Gets whether the operation completed successfully.</summary>
    public bool IsSuccess => Failure is null;

    /// <summary>Gets the successful value, or the default value of <typeparamref name="T"/> after failure.</summary>
    public T? Value { get; }

    /// <summary>Gets the classified failure, or <see langword="null"/> after success.</summary>
    public NotebookFailure? Failure { get; }

    internal static NotebookOperationResult<T> Succeeded(T value) => new(value, null);

    internal static NotebookOperationResult<T> Failed(NotebookFailure failure) =>
        new(default, failure ?? throw new ArgumentNullException(nameof(failure)));
}
