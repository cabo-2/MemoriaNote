using System;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Selects the fixed Schema V2 derived indexes to validate or rebuild.</summary>
[Flags]
internal enum SchemaV2DerivedIndexSelection
{
    None = 0,
    PageTags = 1,
    FtsUnicode61 = 2,
    FtsTrigram = 4,
    All = PageTags | FtsUnicode61 | FtsTrigram
}
