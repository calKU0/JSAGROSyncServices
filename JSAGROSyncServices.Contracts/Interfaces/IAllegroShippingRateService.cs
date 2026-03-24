namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroShippingRateService
    {
        public Task SyncShippingRates(CancellationToken ct = default);
    }
}
