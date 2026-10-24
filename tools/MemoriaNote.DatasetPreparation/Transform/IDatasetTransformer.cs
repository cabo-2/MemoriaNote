using MemoriaNote.DatasetPreparation.Contracts;

namespace MemoriaNote.DatasetPreparation.Transform;

internal interface IDatasetTransformer
{
    Task<DatasetInventory> InventoryAsync(
        string checkoutRoot,
        DatasetManifest manifest,
        CancellationToken token);
}
