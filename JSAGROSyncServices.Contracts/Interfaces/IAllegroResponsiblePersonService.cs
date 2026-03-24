namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroResponsiblePersonService
    {
        public Task SyncResponsiblePersons(CancellationToken ct = default);
    }
}
