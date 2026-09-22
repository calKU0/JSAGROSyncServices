using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IOrderRepository
    {
        public Task SaveAllegroOrder(AllegroOrder order);

        public Task MarkAsOrderedInExternalCompany(int orderId, int externalOrderId);

        public Task<List<AllegroOrder>> GetOrdersToUpdateExternalInfo(List<string> shippingRates);

        public Task<List<AllegroOrder>> GetPendingOrdersForExternalCompany(int delayMinutes);

        public Task UpdateOrderExternalInfo(AllegroOrder order);

        public Task<List<AllegroOrder>> GetOrdersToUpdateInAllegro();

        public Task SetEmailSent(int orderId);

        /// <summary>Numery przesyłek, o których wiemy, że są już w Allegro.</summary>
        public Task<List<AllegroShipment>> GetSentShipments(int orderId);

        public Task AddSentShipment(int orderId, string carrierId, string waybill);

        public Task MarkShipmentsChecked(int orderId);

        /// <summary>Zapisuje status, który Allegro właśnie przyjęło.</summary>
        public Task UpdateRealizeStatus(int orderId, AllegroOrderStatus status);
    }
}