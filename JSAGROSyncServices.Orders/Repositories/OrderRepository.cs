using Dapper;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Orders.Configuration;
using System.Data;

namespace JSAGROSyncServices.Orders.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly DapperContext _context;
        private readonly OrderServiceContext _service;

        public OrderRepository(DapperContext context, OrderServiceContext serviceContext)
        {
            _context = context;
            _service = serviceContext;
        }

        public async Task<List<AllegroOrder>> GetOrdersToUpdateExternalInfo(List<string> shippingRates)
        {
            var shippingRateTable = new DataTable();
            shippingRateTable.Columns.Add("ShippingRate", typeof(string));

            foreach (var rate in shippingRates)
                shippingRateTable.Rows.Add(rate);

            return await QueryOrdersAsync(
                "dbo.AllegroOrders_GetToUpdateExternalInfo",
                new
                {
                    IntegrationCompany = _service.Company,
                    Account = _service.Account,
                    NotWithExternalOrderStatus = "Zrealizowane",
                    ShippingRates = shippingRateTable.AsTableValuedParameter("dbo.ShippingRateList")
                });
        }

        public async Task<List<AllegroOrder>> GetOrdersToUpdateInAllegro()
        {
            return await QueryOrdersAsync(
                "dbo.AllegroOrders_GetToUpdateInAllegro",
                new
                {
                    NewStatus = AllegroOrderStatus.NEW,
                    ProcessingStatus = AllegroOrderStatus.PROCESSING,
                    ReadyForShipmentStatus = AllegroOrderStatus.READY_FOR_SHIPMENT,
                    ReadyForPickupStatus = AllegroOrderStatus.READY_FOR_PICKUP,
                    SentStatus = AllegroOrderStatus.SENT,
                    ReadyStatus = AllegroCheckoutFormStatus.READY_FOR_PROCESSING,
                    Account = _service.Account,
                    IntegrationCompany = _service.Company
                });
        }

        public async Task<List<AllegroOrder>> GetPendingOrdersForExternalCompany(int delayMinutes)
        {
            return await QueryOrdersAsync(
                "dbo.AllegroOrders_GetPendingForExternalCompany",
                new
                {
                    ReadyStatus = AllegroCheckoutFormStatus.READY_FOR_PROCESSING,
                    DelayMinutes = delayMinutes,
                    NewStatus = AllegroOrderStatus.NEW,
                    Account = _service.Account,
                    IntegrationCompany = _service.Company
                });
        }

        /// <summary>Wszystkie procedury zamówień zwracają ten sam kształt: zamówienie + jego pozycje.</summary>
        private async Task<List<AllegroOrder>> QueryOrdersAsync(string storedProcedure, object parameters)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            var orders = new Dictionary<int, AllegroOrder>();

            await conn.QueryAsync<AllegroOrder, AllegroOrderItem, AllegroOrder>(
                storedProcedure,
                (order, item) =>
                {
                    if (!orders.TryGetValue(order.Id, out var current))
                    {
                        current = order;
                        current.Items = new List<AllegroOrderItem>();
                        orders.Add(order.Id, current);
                    }

                    if (item != null)
                        current.Items.Add(item);

                    return current;
                },
                parameters,
                splitOn: "Id",
                commandType: CommandType.StoredProcedure);

            return orders.Values.ToList();
        }

        public async Task MarkAsOrderedInExternalCompany(int orderId, int externalOrderId)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            await conn.ExecuteAsync(
                "dbo.AllegroOrders_MarkAsOrderedInExternalCompany",
                new { OrderId = orderId, ExternalOrderId = externalOrderId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task SaveAllegroOrder(AllegroOrder order)
        {
            using var conn = _context.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                var orderParams = new DynamicParameters(new
                {
                    order.AllegroId,
                    order.MessageToSeller,
                    order.Note,
                    order.Status,
                    order.RealizeStatus,
                    order.Amount,
                    order.ClientNickname,
                    order.RecipientFirstName,
                    order.RecipientLastName,
                    order.RecipientStreet,
                    order.RecipientCity,
                    order.RecipientPostalCode,
                    order.RecipientCountry,
                    order.RecipientCompanyName,
                    order.RecipientEmail,
                    order.RecipientPhoneNumber,
                    order.DeliveryMethodId,
                    order.DeliveryMethodName,
                    order.CancellationDate,
                    order.CreatedAt,
                    order.Revision,
                    order.SentToExternalCompany,
                    order.ExternalOrderId,
                    order.PaymentType,
                    order.ExternalOrderStatus,
                    order.ExternalOrderNumber,
                    order.ExternalDeliveryName,
                    order.Account,
                    order.IntegrationCompany
                });

                orderParams.Add("@Id", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("dbo.AllegroOrders_Save", orderParams, transaction, commandType: CommandType.StoredProcedure);

                if (order.Id == 0)
                    order.Id = orderParams.Get<int>("@Id");

                foreach (var item in order.Items)
                {
                    // Brak produktu w bazie nie przerywa zapisu - zamówienie zapisujemy w całości,
                    // a serwis nie złoży go u dostawcy, dopóki wszystkie pozycje nie będą rozpoznane.
                    var product = await conn.QueryFirstOrDefaultAsync<ProductLookup>(
                        "dbo.RolmarProducts_GetByCode",
                        new { Code = item.ExternalId, IntegrationCompany = _service.Company },
                        transaction,
                        commandType: CommandType.StoredProcedure);

                    item.ProductId = product?.ProductId;

                    await conn.ExecuteAsync(
                        "dbo.AllegroOrderItems_Upsert",
                        new
                        {
                            AllegroOrderId = order.Id,
                            item.OrderItemId,
                            item.ProductId,
                            item.OfferId,
                            item.OfferName,
                            ExternalId = product?.Code ?? item.ExternalId,
                            item.PriceGross,
                            item.Currency,
                            item.Quantity,
                            item.ExternalCourier,
                            item.ExternalTrackingNumber,
                            item.ShippingRate,
                            item.BoughtAt
                        },
                        transaction,
                        commandType: CommandType.StoredProcedure);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task SetEmailSent(int orderId)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            await conn.ExecuteAsync(
                "dbo.AllegroOrders_SetEmailSent",
                new { OrderId = orderId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task UpdateOrderExternalInfo(AllegroOrder order)
        {
            using var conn = _context.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                await conn.ExecuteAsync(
                    "dbo.AllegroOrders_UpdateExternalInfo",
                    new
                    {
                        order.Id,
                        order.ExternalOrderStatus,
                        order.ExternalOrderNumber,
                        order.ExternalDeliveryName
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure);

                foreach (var item in order.Items)
                {
                    await conn.ExecuteAsync(
                        "dbo.AllegroOrderItems_UpdateExternalInfo",
                        new
                        {
                            AllegroOrderId = order.Id,
                            item.OrderItemId,
                            item.ProductId,
                            item.ExternalCourier,
                            item.ExternalTrackingNumber
                        },
                        transaction,
                        commandType: CommandType.StoredProcedure);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private sealed class ProductLookup
        {
            public int ProductId { get; set; }

            public string Code { get; set; } = string.Empty;
        }
    }
}
