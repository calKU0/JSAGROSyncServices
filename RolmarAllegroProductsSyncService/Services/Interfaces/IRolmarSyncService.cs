namespace RolmarAllegroProductsSyncService.Services.Interfaces
{
    public interface IRolmarSyncService
    {
        public Task SyncProductsAsync(CancellationToken ct = default);

        public Task SyncStockAsync(CancellationToken ct = default);

        public Task SyncImagesAsync(CancellationToken ct = default);
    }
}