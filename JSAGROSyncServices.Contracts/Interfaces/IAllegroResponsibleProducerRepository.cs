using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroResponsibleProducerRepository
    {
        public Task UpsertAllegroResponsibleProducers(IEnumerable<AllegroResponsibleProducer> producers, CancellationToken ct = default);
        public Task<IEnumerable<AllegroResponsibleProducer>> GetAllegroResponsibleProducers(CancellationToken ct = default);
    }
}
