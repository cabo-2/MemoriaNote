#nullable enable

using System;

namespace MemoriaNote.Persistence.Repositories;

/// <summary>Reports a candidate-specific body-search validation failure.</summary>
internal sealed class NotebookBodySearchValidationException : ArgumentException
{
    internal NotebookBodySearchValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>Reports an unsupported body-search case comparison.</summary>
internal sealed class NotebookBodySearchCaseSensitivityException : ArgumentException
{
    internal NotebookBodySearchCaseSensitivityException()
        : base("The selected body-search candidate does not support case-sensitive matching.")
    {
    }
}

/// <summary>Reports that a selected derived body-search index cannot be used.</summary>
internal sealed class NotebookBodySearchIndexException : Exception
{
    internal NotebookBodySearchIndexException(Exception innerException)
        : base("The selected body-search index is missing or invalid.", innerException)
    {
    }
}
