using AllegroGaskaOrdersSyncService.Models;

namespace AllegroGaskaOrdersSyncService.Repositories.Interfaces
{
    public interface IOrderRepository
    {
        public Task SaveAllegroOrder(AllegroOrder order);

        public Task MarkAsOrderedInGaska(int orderId, int gaskaOrderId);

        public Task<List<AllegroOrder>> GetOrdersToUpdateGaskaInfo();

        public Task<List<AllegroOrder>> GetPendingOrdersForGaska(int delayMinutes);

        public Task UpdateOrderGaskaInfo(AllegroOrder order);

        public Task<List<AllegroOrder>> GetOrdersToUpdateInAllegro();

        public Task SetEmailSent(int orderId);
    }
}