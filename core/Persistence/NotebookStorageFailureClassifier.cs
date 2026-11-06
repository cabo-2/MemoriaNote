using System;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence;

/// <summary>Classifies provider failures without exposing provider types to Application.</summary>
internal static class NotebookStorageFailureClassifier
{
    internal static bool CanClassify(Exception exception) => exception is SqliteException;

    internal static NotebookStorageFailureKind Classify(Exception exception)
    {
        if (exception is not SqliteException sqlite)
            throw new ArgumentException("The exception is not a supported storage failure.", nameof(exception));

        return sqlite.SqliteErrorCode switch
        {
            5 or 6 => NotebookStorageFailureKind.Busy,
            19 => NotebookStorageFailureKind.Constraint,
            3 or 8 or 10 or 13 or 14 or 23 => NotebookStorageFailureKind.Access,
            _ => NotebookStorageFailureKind.Unexpected
        };
    }
}

/// <summary>Identifies a provider-neutral storage failure category.</summary>
internal enum NotebookStorageFailureKind
{
    Busy,
    Constraint,
    Access,
    Unexpected
}
