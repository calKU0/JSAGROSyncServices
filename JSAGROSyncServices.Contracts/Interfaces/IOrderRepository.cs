using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IOrderRepository
    {
        public Task SaveAllegroOrder(AllegroOrder order);

        public Task MarkAsOrderedInExternalCompany(int orderId, int externalOrderId);

        public Task<List<AllegroOrder>> GetOrdersToUpdateExternalInfo();

        public Task<List<AllegroOrder>> GetPendingOrdersForExternalCompany(int delayMinutes);

        public Task UpdateOrderExternalInfo(AllegroOrder order);

        public Task<List<AllegroOrder>> GetOrdersToUpdateInAllegro();

        public Task SetEmailSent(int orderId);
    }
}