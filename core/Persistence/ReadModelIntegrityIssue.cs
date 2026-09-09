namespace MemoriaNote
{
    /// <summary>
    /// Identifies a kind of read model inconsistency.
    /// </summary>
    public enum ReadModelIntegrityIssueKind
    {
        /// <summary>A Page has no corresponding Contents row.</summary>
        MissingContent,

        /// <summary>A Contents row has no corresponding Page.</summary>
        UnexpectedContent,

        /// <summary>A read model contains a duplicated row identifier.</summary>
        DuplicateRowId,

        /// <summary>A read model contains a duplicated UUID.</summary>
        DuplicateUuid,

        /// <summary>Corresponding rows disagree about their Rowid or UUID.</summary>
        IdentityMismatch,

        /// <summary>A Contents summary value differs from its Page value.</summary>
        ValueMismatch,

        /// <summary>A required Page synchronization trigger is absent.</summary>
        MissingTrigger,

        /// <summary>The FTS5 virtual table is absent.</summary>
        MissingFtsIndex,

        /// <summary>The FTS5 index is inconsistent with itself or Pages.</summary>
        FtsIndexMismatch
    }

    /// <summary>
    /// Describes one read model integrity problem.
    /// </summary>
    public sealed class ReadModelIntegrityIssue
    {
        internal ReadModelIntegrityIssue(
            ReadModelIntegrityIssueKind kind,
            int? rowId = null,
            string uuid = null,
            string propertyName = null,
            string expectedValue = null,
            string actualValue = null)
        {
            Kind = kind;
            RowId = rowId;
            Uuid = uuid;
            PropertyName = propertyName;
            ExpectedValue = expectedValue;
            ActualValue = actualValue;
        }

        /// <summary>Gets the category of the inconsistency.</summary>
        public ReadModelIntegrityIssueKind Kind { get; }

        /// <summary>Gets the affected Page row identifier when available.</summary>
        public int? RowId { get; }

        /// <summary>Gets the affected Page UUID when available.</summary>
        public string Uuid { get; }

        /// <summary>Gets the mismatched property or database object name.</summary>
        public string PropertyName { get; }

        /// <summary>Gets the expected value when available.</summary>
        public string ExpectedValue { get; }

        /// <summary>Gets the actual value when available.</summary>
        public string ActualValue { get; }
    }
}
