namespace MemoriaNote.Persistence;

/// <summary>Identifies the recognized notebook format generation.</summary>
internal enum NotebookFormatClassification
{
    Missing,
    Legacy,
    Current,
    Unsupported,
    Corrupt
}

/// <summary>Explains the evidence used for a format classification.</summary>
internal enum NotebookFormatRecognitionReason
{
    MissingPath,
    SchemaV1Identity,
    SchemaV2Identity,
    NewerSchemaV1Version,
    NewerSchemaV2Version,
    ConflictingIdentity,
    UnrecognizedSqlite,
    UnreadableIdentity
}

/// <summary>Describes the next action explicitly recommended by recognition.</summary>
internal enum NotebookFormatRecommendedAction
{
    None,
    Migrate
}

/// <summary>Reports a read-only notebook format recognition result.</summary>
internal sealed class NotebookFormatRecognition
{
    internal NotebookFormatRecognition(
        NotebookFormatClassification classification,
        NotebookFormatRecognitionReason reason,
        NotebookFormatRecommendedAction recommendedAction,
        string detectedVersion = null)
    {
        Classification = classification;
        Reason = reason;
        RecommendedAction = recommendedAction;
        DetectedVersion = detectedVersion;
    }

    /// <summary>Gets the recognized format classification.</summary>
    internal NotebookFormatClassification Classification { get; }

    /// <summary>Gets the evidence category behind the classification.</summary>
    internal NotebookFormatRecognitionReason Reason { get; }

    /// <summary>Gets an explicit next action, when one is safe to recommend.</summary>
    internal NotebookFormatRecommendedAction RecommendedAction { get; }

    /// <summary>Gets the positively identified persisted version, when available.</summary>
    internal string DetectedVersion { get; }
}
