using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroResponsiblePersonRepository
    {
        public Task UpsertAllegroResponsiblePersons(IEnumerable<AllegroResponsiblePerson> responsiblePersons, CancellationToken ct = default);
        public Task<IEnumerable<AllegroResponsiblePerson>> GetAllegroResponsiblePersons(CancellationToken ct = default);
    }
}
