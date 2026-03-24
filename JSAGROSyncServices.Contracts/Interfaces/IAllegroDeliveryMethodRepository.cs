using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroDeliveryMethodRepository
    {
        public Task UpsertAllegroDeliveryMethods(IEnumerable<AllegroDeliveryMethod> deliveryMethods, CancellationToken ct = default);
        public Task<IEnumerable<AllegroDeliveryMethod>> GetAllegroDeliveryMethods(CancellationToken ct = default);
    }
}
