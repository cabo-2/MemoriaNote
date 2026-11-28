namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Defines fixed SQLite behavior for released maintenance operations.</summary>
internal static class SchemaV2MaintenanceDefaults
{
    /// <summary>Gets the production wait limit for a busy maintenance source.</summary>
    internal const int BusyTimeoutSeconds = 30;
}
