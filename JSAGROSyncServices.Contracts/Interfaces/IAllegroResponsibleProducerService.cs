namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroResponsibleProducerService
    {
        public Task SyncResponsibleProducers(CancellationToken ct = default);
    }
}
