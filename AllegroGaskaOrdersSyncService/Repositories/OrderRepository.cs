using AllegroGaskaOrdersSyncService.Data;
using AllegroGaskaOrdersSyncService.Data.Enums;
using AllegroGaskaOrdersSyncService.Models;
using AllegroGaskaOrdersSyncService.Repositories.Interfaces;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly DapperContext _context;

        public OrderRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<List<AllegroOrder>> GetOrdersToUpdateGaskaInfo()
        {
            using var conn = _context.CreateConnection();

            var sql = @"
                SELECT
                    o.*,
                    i.Id, i.AllegroOrderId, i.GaskaItemId, i.OrderItemId, i.OfferId, i.OfferName, i.ExternalId,
                    i.PriceGross, i.Currency, i.Quantity, i.GaskaCourier, i.GaskaTrackingNumber, i.BoughtAt
                FROM AllegroOrders o
                LEFT JOIN AllegroOrderItems i ON o.Id = i.AllegroOrderId
                WHERE
                    o.SentToGaska = 1
                    AND isnull(o.GaskaOrderStatus,'') <> 'Zrealizowane'
                    AND o.GaskaOrderId IS NOT NULL;
                ";

            var orderDict = new Dictionary<int, AllegroOrder>();

            var orders = await conn.QueryAsync<AllegroOrder, AllegroOrderItem, AllegroOrder>(
                sql,
                (order, item) =>
                {
                    if (!orderDict.TryGetValue(order.Id, out var currentOrder))
                    {
                        currentOrder = order;
                        currentOrder.Items = new List<AllegroOrderItem>();
                        orderDict.Add(order.Id, currentOrder);
                    }

                    if (item != null)
                        currentOrder.Items.Add(item);

                    return currentOrder;
                },
                splitOn: "Id"
            );

            return orderDict.Values.ToList();
        }

        public async Task<List<AllegroOrder>> GetOrdersToUpdateInAllegro()
        {
            using var conn = _context.CreateConnection();

            var sql = @"
                SELECT
                    o.*,
                    i.Id, i.AllegroOrderId, i.GaskaItemId, i.OrderItemId, i.OfferId, i.OfferName, i.ExternalId,
                    i.PriceGross, i.Currency, i.Quantity, i.GaskaCourier, i.GaskaTrackingNumber, i.BoughtAt
                FROM AllegroOrders o
                LEFT JOIN AllegroOrderItems i ON o.Id = i.AllegroOrderId
                WHERE
                    o.SentToGaska = 1
                    AND o.GaskaOrderId IS NOT NULL
                    AND o.RealizeStatus IN (
                        @NewStatus,
                        @ProcessingStatus,
                        @ReadyForShipmentStatus,
                        @ReadyForPickupStatus,
                        @SentStatus
                    );
                ";

            var orderDict = new Dictionary<int, AllegroOrder>();

            var orders = await conn.QueryAsync<AllegroOrder, AllegroOrderItem, AllegroOrder>(
                sql,
                (order, item) =>
                {
                    if (!orderDict.TryGetValue(order.Id, out var currentOrder))
                    {
                        currentOrder = order;
                        currentOrder.Items = new List<AllegroOrderItem>();
                        orderDict.Add(order.Id, currentOrder);
                    }

                    if (item != null)
                        currentOrder.Items.Add(item);

                    return currentOrder;
                },
                param: new
                {
                    NewStatus = AllegroOrderStatus.NEW,
                    ProcessingStatus = AllegroOrderStatus.PROCESSING,
                    ReadyForShipmentStatus = AllegroOrderStatus.READY_FOR_SHIPMENT,
                    ReadyForPickupStatus = AllegroOrderStatus.READY_FOR_PICKUP,
                    SentStatus = AllegroOrderStatus.SENT
                },
                splitOn: "Id"
            );

            return orderDict.Values.ToList();
        }

        public async Task<List<AllegroOrder>> GetPendingOrdersForGaska(int delayMinutes)
        {
            using var conn = _context.CreateConnection();

            var sql = @"
                SELECT
                    o.*,
                    i.Id, i.AllegroOrderId, i.GaskaItemId, i.OrderItemId, i.OfferId, i.OfferName, i.ExternalId,
                    i.PriceGross, i.Currency, i.Quantity, i.GaskaCourier, i.GaskaTrackingNumber, i.BoughtAt
                FROM AllegroOrders o
                LEFT JOIN AllegroOrderItems i ON o.Id = i.AllegroOrderId
                WHERE
                    o.SentToGaska = 0
                    AND o.Status = @ReadyStatus
                    AND o.RealizeStatus = @NewStatus
                    AND DATEDIFF(MINUTE, o.CreatedAt, GETDATE()) >= @DelayMinutes;
                ";

            var orderDict = new Dictionary<int, AllegroOrder>();

            var orders = await conn.QueryAsync<AllegroOrder, AllegroOrderItem, AllegroOrder>(
                sql,
                (order, item) =>
                {
                    if (!orderDict.TryGetValue(order.Id, out var currentOrder))
                    {
                        currentOrder = order;
                        currentOrder.Items = new List<AllegroOrderItem>();
                        orderDict.Add(order.Id, currentOrder);
                    }

                    if (item != null)
                        currentOrder.Items.Add(item);

                    return currentOrder;
                },
                param: new { ReadyStatus = AllegroCheckoutFormStatus.READY_FOR_PROCESSING, DelayMinutes = delayMinutes, NewStatus = AllegroOrderStatus.NEW },
                splitOn: "Id"
            );

            return orderDict.Values.ToList();
        }

        public async Task MarkAsOrderedInGaska(int orderId, int gaskaOrderId)
        {
            using var conn = _context.CreateConnection();
            var sql = @"
                UPDATE AllegroOrders
                SET
                    SentToGaska = 1,
                    GaskaOrderId = @GaskaOrderId
                WHERE Id = @OrderId;
                ";

            await conn.ExecuteAsync(sql, new { OrderId = orderId, GaskaOrderId = gaskaOrderId });
        }

        public async Task SaveAllegroOrder(AllegroOrder order)
        {
            using var conn = _context.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                // Insert or update the order
                var orderSql = @"
                    IF EXISTS (SELECT 1 FROM AllegroOrders WHERE AllegroId = @AllegroId)
                    BEGIN
                        UPDATE AllegroOrders
                        SET
                            MessageToSeller = @MessageToSeller,
                            Note = @Note,
                            Status = @Status,
                            RealizeStatus = @RealizeStatus,
                            Amount = @Amount,
                            ClientNickname = @ClientNickname,
                            RecipientFirstName = @RecipientFirstName,
                            RecipientLastName = @RecipientLastName,
                            RecipientStreet = @RecipientStreet,
                            RecipientCity = @RecipientCity,
                            RecipientPostalCode = @RecipientPostalCode,
                            RecipientCountry = @RecipientCountry,
                            RecipientCompanyName = @RecipientCompanyName,
                            RecipientEmail = @RecipientEmail,
                            RecipientPhoneNumber = @RecipientPhoneNumber,
                            DeliveryMethodId = @DeliveryMethodId,
                            DeliveryMethodName = @DeliveryMethodName,
                            CancellationDate = @CancellationDate,
                            CreatedAt = @CreatedAt,
                            Revision = @Revision,
                            PaymentType = @PaymentType
                        WHERE AllegroId = @AllegroId;

                        SELECT @Id = Id FROM AllegroOrders WHERE AllegroId = @AllegroId;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO AllegroOrders (
                            AllegroId, MessageToSeller, Note, Status, RealizeStatus, Amount, ClientNickname,
                            RecipientFirstName, RecipientLastName, RecipientStreet, RecipientCity, RecipientPostalCode, RecipientCountry,
                            RecipientCompanyName, RecipientEmail, RecipientPhoneNumber,
                            DeliveryMethodId, DeliveryMethodName, CancellationDate, CreatedAt, Revision,
                            SentToGaska, GaskaOrderId, PaymentType, GaskaOrderStatus, GaskaOrderNumber, GaskaDeliveryName
                        )
                        VALUES (
                            @AllegroId, @MessageToSeller, @Note, @Status, @RealizeStatus, @Amount, @ClientNickname,
                            @RecipientFirstName, @RecipientLastName, @RecipientStreet, @RecipientCity, @RecipientPostalCode, @RecipientCountry,
                            @RecipientCompanyName, @RecipientEmail, @RecipientPhoneNumber,
                            @DeliveryMethodId, @DeliveryMethodName, @CancellationDate, @CreatedAt, @Revision,
                            @SentToGaska, @GaskaOrderId, @PaymentType, @GaskaOrderStatus, @GaskaOrderNumber, @GaskaDeliveryName
                        );

                        SET @Id = SCOPE_IDENTITY();
                    END";

                var orderParams = new DynamicParameters(order);
                orderParams.Add("@Id", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

                await conn.ExecuteAsync(orderSql, orderParams, transaction);

                if (order.Id == 0)
                    order.Id = orderParams.Get<int>("@Id");

                foreach (var item in order.Items)
                {
                    var product = await conn.QueryFirstOrDefaultAsync<(int GaskaItemId, string CodeGaska)>(
                        "SELECT Id AS GaskaItemId, CodeGaska FROM Products WHERE CodeGaska = @CodeGaska",
                        new { CodeGaska = item.ExternalId }, transaction
                    );

                    if (product == default)
                        throw new Exception($"Product with Id {item.GaskaItemId} not found.");

                    var itemParams = new
                    {
                        AllegroOrderId = order.Id,
                        OrderItemId = item.OrderItemId,
                        GaskaItemId = product.GaskaItemId,
                        OfferId = item.OfferId,
                        OfferName = item.OfferName,
                        ExternalId = product.CodeGaska,
                        PriceGross = item.PriceGross,
                        Currency = item.Currency,
                        Quantity = item.Quantity,
                        GaskaCourier = item.GaskaCourier,
                        GaskaTrackingNumber = item.GaskaTrackingNumber,
                        BoughtAt = item.BoughtAt
                    };

                    var itemSql = @"
                    MERGE INTO AllegroOrderItems AS target
                    USING (SELECT
                              @AllegroOrderId AS AllegroOrderId,
                              @OrderItemId AS OrderItemId
                          ) AS source
                    ON target.AllegroOrderId = source.AllegroOrderId AND target.OrderItemId = source.OrderItemId
                    WHEN MATCHED THEN
                        UPDATE SET
                            GaskaItemId = @GaskaItemId,
                            OfferId = @OfferId,
                            OfferName = @OfferName,
                            ExternalId = @ExternalId,
                            PriceGross = @PriceGross,
                            Currency = @Currency,
                            Quantity = @Quantity,
                            BoughtAt = @BoughtAt
                    WHEN NOT MATCHED THEN
                        INSERT (
                            AllegroOrderId, GaskaItemId, OrderItemId, OfferId, OfferName, ExternalId, PriceGross, Currency, Quantity, BoughtAt
                        )
                        VALUES (
                            @AllegroOrderId, @GaskaItemId, @OrderItemId, @OfferId, @OfferName, @ExternalId, @PriceGross, @Currency, @Quantity, @BoughtAt
                        );";

                    await conn.ExecuteAsync(itemSql, itemParams, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public Task SetEmailSent(int orderId)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            var sql = @"
                UPDATE AllegroOrders
                SET EmailSent = 1
                WHERE Id = @OrderId;
                ";

            return conn.ExecuteAsync(sql, new { OrderId = orderId });
        }

        public async Task UpdateOrderGaskaInfo(AllegroOrder order)
        {
            using var conn = _context.CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                // Update only Gaska fields in the order
                var orderSql = @"
                    UPDATE AllegroOrders
                    SET
                        GaskaOrderStatus = @GaskaOrderStatus,
                        GaskaOrderNumber = @GaskaOrderNumber,
                        GaskaDeliveryName = @GaskaDeliveryName
                    WHERE Id = @Id;
                    ";

                await conn.ExecuteAsync(orderSql, new
                {
                    order.Id,
                    order.GaskaOrderId,
                    order.GaskaOrderStatus,
                    order.GaskaOrderNumber,
                    order.GaskaDeliveryName
                }, transaction);

                // Update Gaska fields in items
                var itemSql = @"
                    UPDATE AllegroOrderItems
                    SET
                        GaskaItemId = @GaskaItemId,
                        GaskaCourier = @GaskaCourier,
                        GaskaTrackingNumber = @GaskaTrackingNumber
                    WHERE AllegroOrderId = @AllegroOrderId AND OrderItemId = @OrderItemId;
                    ";

                foreach (var item in order.Items)
                {
                    await conn.ExecuteAsync(itemSql, new
                    {
                        AllegroOrderId = order.Id,
                        item.OrderItemId,
                        item.GaskaItemId,
                        item.GaskaCourier,
                        item.GaskaTrackingNumber
                    }, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}