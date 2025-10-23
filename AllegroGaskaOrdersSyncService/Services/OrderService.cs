using AllegroGaskaOrdersSyncService.Data.Enums;
using AllegroGaskaOrdersSyncService.DTOs.AllegroApi;
using AllegroGaskaOrdersSyncService.DTOs.GaskaApi;
using AllegroGaskaOrdersSyncService.Models;
using AllegroGaskaOrdersSyncService.Repositories.Interfaces;
using AllegroGaskaOrdersSyncService.Services.Interfaces;
using AllegroGaskaOrdersSyncService.Settings;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AllegroGaskaOrdersSyncService.Services
{
    public class OrderService : IOrderService
    {
        private readonly ILogger<OrderService> _logger;
        private readonly AppSettings _appSettings;
        private readonly CourierSettings _courierSettings;
        private readonly IOrderRepository _orderRepo;
        private readonly IEmailService _emailService;
        private readonly AllegroApiClient _allegroApiClient;
        private readonly GaskaApiClient _gaskaApiClient;

        public OrderService(ILogger<OrderService> logger, IOptions<AppSettings> appSettings, IOptions<CourierSettings> courierSettings, IOrderRepository orderRepo, IEmailService emailService, AllegroApiClient allegroApiClient, GaskaApiClient gaskaApiClient)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            _courierSettings = courierSettings.Value;
            _orderRepo = orderRepo;
            _emailService = emailService;
            _allegroApiClient = allegroApiClient;
            _gaskaApiClient = gaskaApiClient;
        }

        public async Task SyncOrdersFromAllegro(CancellationToken ct = default)
        {
            const int limit = 100;
            int offset = 0, totalFetched = 0;
            const string minBoughtFrom = "2025-10-24T00:00:00Z";
            var minBoughtDate = DateTime.Parse(minBoughtFrom, null, DateTimeStyles.AdjustToUniversal);
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var boughtDate = sevenDaysAgo < minBoughtDate ? minBoughtDate : sevenDaysAgo;
            string boughtDateIso = boughtDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

            try
            {
                var shippingRates = await _allegroApiClient.GetAsync<ShippingRatesReponse>("/sale/shipping-rates", ct);
                var gaskaShippingRateId = shippingRates.ShippingRates
                    .Where(sr => sr.Name == _appSettings.AllegroDeliveryName)
                    .Select(sr => sr.Id)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(gaskaShippingRateId))
                {
                    _logger.LogError("Failed to find Allegro Shipping Rate ID for delivery method '{DeliveryName}'. Aborting order sync.", _appSettings.AllegroDeliveryName);
                    return;
                }

                while (true)
                {
                    var query = $"order/checkout-forms?limit={limit}&offset={offset}&lineItems.boughtAt.gte={boughtDateIso}";
                    var response = await _allegroApiClient.GetAsync<AllegroGetOrdersResponse>(query, ct);

                    var orders = response?.CheckoutForms ?? Enumerable.Empty<AllegroGetOrdersResponse.CheckoutForm>();
                    if (!orders.Any())
                        break;

                    var semaphore = new SemaphoreSlim(5);

                    foreach (var order in orders)
                    {
                        try
                        {
                            var offerTasks = order.LineItems.Select(async item =>
                            {
                                await semaphore.WaitAsync(ct);
                                try
                                {
                                    return await _allegroApiClient.GetAsync<AllegroOfferDetails>($"/sale/product-offers/{item.Offer.Id}", ct);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }).ToList();

                            var offers = await Task.WhenAll(offerTasks);

                            // Check if all items use the Gąska shipping method
                            bool allItemsUseGaska = offers
                                .Select(o => o.Delivery.ShippingRates.Id)
                                .All(id => id == gaskaShippingRateId);

                            if (!allItemsUseGaska)
                            {
                                _logger.LogInformation("Skipping order {OrderId} - not all items use {ShippingRate} shipping method.", order.Id, _appSettings.AllegroDeliveryName);
                                continue;
                            }

                            // Save the order
                            var model = MapAllegroOrderToModel(order);
                            await _orderRepo.SaveAllegroOrder(model);
                            _logger.LogInformation("Order {OrderId} synced from Allegro.", order.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to sync order {OrderId} from Allegro.", order.Id);
                        }
                    }

                    totalFetched += orders.Count();
                    _logger.LogInformation("Fetched {Count} orders from Allegro so far.", totalFetched);

                    offset += limit;
                    if (totalFetched >= response.TotalCount) break;
                }

                _logger.LogInformation("Finished syncing {Count} orders from Allegro.", totalFetched);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync orders from Allegro.");
            }
        }

        public async Task CreateOrdersInGaska(CancellationToken ct = default!)
        {
            try
            {
                List<AllegroOrder> orders = await _orderRepo.GetPendingOrdersForGaska(_appSettings.OfferProcessingDelayMinutes);

                foreach (var order in orders)
                {
                    try
                    {
                        // Create Delivery Address in Gąska
                        var addressRequest = MapAllegroOrderToGaskaDeliveryRequest(order);
                        var addressResponse = await _gaskaApiClient.PostAsync<GaskaCreateDeliveryAddressResponse>("addDeliveryAddress", addressRequest, ct);
                        if (addressResponse.Result != 0)
                            throw new Exception(addressResponse.Message);

                        // Create Order in Gąska
                        var orderRequest = MapAllegroOrderToGaskaOrderRequest(order, addressResponse.AddressId);
                        var orderResponse = await _gaskaApiClient.PostAsync<GaskaCreateOrderResponse>("order", orderRequest, ct);
                        if (orderResponse.Result != 0)
                            throw new Exception(orderResponse.Message);

                        order.GaskaOrderId = orderResponse.NewOrders.FirstOrDefault();
                        await _orderRepo.MarkAsOrderedInGaska(order.Id, order.GaskaOrderId);

                        // Fetch order data from Gąska to update local record
                        await FetchAndUpdateGaskaOrder(order, ct);
                        _logger.LogInformation("Created Order {GaskaOrderId} in Gąska for Allegro Order {AllegroOrderId}.", order.GaskaOrderNumber, order.AllegroId);

                        // Send success email
                        var body = BuildOrderEmailBody(order);
                        await _emailService.SendEmailAsync(_appSettings.NotificationsEmail, $"Złożono automatyczne zamówienie", body);
                        await _orderRepo.SetEmailSent(order.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create order in Gąska for Allegro Order {AllegroOrderId}.", order.AllegroId);

                        if (order.EmailSent)
                            continue;

                        // Send error email
                        var body = BuildOrderEmailBody(order, ex.Message);
                        await _emailService.SendEmailAsync(_appSettings.NotificationsEmail, $"BŁĄD przy składaniu zamówienia: {order.AllegroId}", body);
                        await _orderRepo.SetEmailSent(order.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create orders in Gąska.");
            }
        }

        public async Task UpdateOrderGaskaInfo(CancellationToken ct = default!)
        {
            try
            {
                var orders = await _orderRepo.GetOrdersToUpdateGaskaInfo();
                foreach (var order in orders)
                {
                    await FetchAndUpdateGaskaOrder(order, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update orders from Gąska API.");
            }
        }

        private async Task FetchAndUpdateGaskaOrder(AllegroOrder order, CancellationToken ct = default!)
        {
            try
            {
                var gaskaOrderResponse = await _gaskaApiClient.GetAsync<GaskaGetOrderResponse>($"order?id={order.GaskaOrderId}&lng=pl", ct);
                if (gaskaOrderResponse.Result != 0)
                {
                    _logger.LogError($"Failed to fetch Order {order.GaskaOrderId} from Gąska. Error: {gaskaOrderResponse.Message}");
                    return;
                }

                if (gaskaOrderResponse.Order == null)
                {
                    _logger.LogError($"No Order data returned from Gąska for Order ID {order.GaskaOrderId}.");
                    return;
                }

                order.GaskaDeliveryName = gaskaOrderResponse.Order.Delivery;
                order.GaskaOrderStatus = gaskaOrderResponse.Order.Items.FirstOrDefault()?.RealizeDeliveryStatus;
                order.GaskaOrderNumber = gaskaOrderResponse.Order.OrderNumber;

                foreach (var item in order.Items)
                {
                    var gaskaItem = gaskaOrderResponse.Order.Items.FirstOrDefault(i => i.Id == item.GaskaItemId);
                    if (gaskaItem != null)
                        item.GaskaTrackingNumber = gaskaItem.RealizeTrackingNumber;
                }

                await _orderRepo.UpdateOrderGaskaInfo(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Order {AllegroOrderId} with Gąska info.", order.AllegroId);
            }
        }

        public async Task UpdateOrdersInAllegro(CancellationToken ct = default)
        {
            try
            {
                var orders = await _orderRepo.GetOrdersToUpdateInAllegro();
                foreach (var order in orders)
                {
                    // --- Update Order Status ---
                    try
                    {
                        var status = MapGaskaStatusToAllegro(order.GaskaOrderStatus);
                        if (status != order.RealizeStatus && status != AllegroOrderStatus.NEW)
                        {
                            var statusRequest = new AllegroSetOrderStatusRequest
                            {
                                Status = status
                            };

                            var response = await _allegroApiClient.SendWithResponseAsync($"/order/checkout-forms/{order.AllegroId}/fulfillment", HttpMethod.Put, statusRequest, ct);
                            if (!response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync(ct);
                                LogAllegroErrors(response, body, "status", order.AllegroId);
                            }
                            else
                            {
                                _logger.LogInformation("Updated Order Status to {Status} for Allegro Order {AllegroOrderId}.", status, order.AllegroId);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No status change for Allegro order {AllegroOrderId}.", order.AllegroId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update Order Status {AllegroOrderId} in Allegro.", order.AllegroId);
                    }

                    // --- Update Tracking Number ---
                    try
                    {
                        var items = order.Items;
                        if (items == null || !items.Any())
                            continue;

                        // Group items by tracking number (ignore those without tracking)
                        var groups = items
                            .Where(i => !string.IsNullOrWhiteSpace(i.GaskaTrackingNumber))
                            .GroupBy(i => i.GaskaTrackingNumber);

                        foreach (var group in groups)
                        {
                            var trackingNumber = group.Key;
                            var lineItems = group.Select(i => new AllegroAddTrackingNumberRequest.LineItem
                            {
                                Id = i.OrderItemId
                            }).ToList();

                            var carrierId = group.First().GaskaCourier;

                            carrierId = carrierId?.ToUpperInvariant() switch
                            {
                                var s when s.Contains("DPD") => "DPD",
                                var s when s.Contains("FEDEX") => "FEDEX",
                                var s when s.Contains("GLS") => "GLS",
                                _ => carrierId
                            };

                            var request = new AllegroAddTrackingNumberRequest
                            {
                                CarrierId = carrierId,
                                Waybill = trackingNumber,
                                LineItems = lineItems
                            };

                            var response = await _allegroApiClient.SendWithResponseAsync($"/order/checkout-forms/{order.AllegroId}/shipments", HttpMethod.Post, request, ct);

                            if (!response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync(ct);
                                LogAllegroErrors(response, body, "tracking numbers", order.AllegroId);
                            }
                            else
                            {
                                _logger.LogInformation("Updated tracking number {TrackingNumber} for Allegro Order {AllegroOrderId} (Items: {Count}).", trackingNumber, order.AllegroId, lineItems.Count);
                            }
                        }

                        if (!groups.Any())
                        {
                            _logger.LogInformation("No tracking numbers found for Allegro order {AllegroOrderId}.", order.AllegroId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update Order Tracking Number {AllegroOrderId} in Allegro.", order.AllegroId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update orders in Allegro.");
            }
        }

        private AllegroOrder MapAllegroOrderToModel(AllegroGetOrdersResponse.CheckoutForm allegroOrder)
        {
            var address = allegroOrder.Delivery.Address;
            var fulfillment = allegroOrder.Fulfillment;

            string street = address.Street?.Trim() ?? "";
            string city = address.City?.Trim() ?? "";

            // Detect and split street if it contains a city and "ul." pattern
            if (!string.IsNullOrEmpty(street))
            {
                // Regex matches: "something (ul\.?|ulica\.?) rest"
                var match = Regex.Match(street, @"^(?<city>.+?)\s+(?:ul\.?|ulica\.?)\s*(?<street>.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    city = match.Groups["city"].Value.Trim();
                    street = match.Groups["street"].Value.Trim();
                }
            }

            return new AllegroOrder
            {
                AllegroId = allegroOrder.Id,
                MessageToSeller = allegroOrder.MessageToSeller,
                Note = allegroOrder.Note?.Text,
                Status = allegroOrder.Status,
                RealizeStatus = fulfillment.Status,
                ClientNickname = allegroOrder.Buyer.Login.Trim(),
                RecipientFirstName = address.FirstName.Trim(),
                RecipientLastName = address.LastName.Trim(),
                RecipientStreet = street,
                RecipientCity = city,
                RecipientPostalCode = address.ZipCode.Trim(),
                RecipientCountry = address.CountryCode.Trim(),
                RecipientCompanyName = address?.CompanyName,
                RecipientEmail = allegroOrder.Buyer?.Email,
                RecipientPhoneNumber = address?.PhoneNumber,
                DeliveryMethodId = allegroOrder.Delivery.Method.Id,
                DeliveryMethodName = allegroOrder.Delivery.Method.Name.ToUpper(),
                CancellationDate = allegroOrder.Delivery?.Cancellation?.Date,
                Amount = decimal.Parse(allegroOrder.Summary.TotalToPay.Amount, CultureInfo.InvariantCulture),
                CreatedAt = allegroOrder.LineItems?.Max(i => (DateTime?)i.BoughtAt) ?? default,
                Revision = allegroOrder.Revision,
                Items = allegroOrder.LineItems?.Select(item => new AllegroOrderItem
                {
                    ExternalId = item.Offer.External.Id,
                    Quantity = item.Quantity,
                    PriceGross = item.Price.Amount,
                    Currency = item.Price.Currency,
                    OfferName = item.Offer.Name,
                    OfferId = item.Offer.Id,
                    OrderItemId = item.Id,
                    BoughtAt = item.BoughtAt,
                }).ToList() ?? new List<AllegroOrderItem>()
            };
        }

        private GaskaCreateDeliveryAddressRequest MapAllegroOrderToGaskaDeliveryRequest(AllegroOrder order) =>
            new()
            {
                Name1 = $"{order.RecipientFirstName} {order.RecipientLastName}",
                Street = order.RecipientStreet,
                City = order.RecipientCity,
                PostalCode = order.RecipientPostalCode,
                Country = order.RecipientCountry,
                Phone = order.RecipientPhoneNumber,
                Email = "jsagro@wp.pl",
                OneUse = true
            };

        private GaskaCreateOrderRequest MapAllegroOrderToGaskaOrderRequest(AllegroOrder order, int addressId)
        {
            // Determine the delivery method
            string deliveryMethod = order.DeliveryMethodName;

            if (order.PaymentType != AllegroPaymentType.CASH_ON_DELIVERY
                && DateTime.Now.DayOfWeek != DayOfWeek.Saturday
                && DateTime.Now.DayOfWeek != DayOfWeek.Sunday)
            {
                var nowHour = DateTime.Now.Hour;

                // Check if we need to switch courier
                deliveryMethod = order.DeliveryMethodName switch
                {
                    var name when name.Contains("DPD", StringComparison.OrdinalIgnoreCase)
                        && nowHour >= _courierSettings.DpdFinalOrderHour
                        => GetNextAvailableCourier("DPD", nowHour),

                    var name when name.Contains("FEDEX", StringComparison.OrdinalIgnoreCase)
                        && nowHour >= _courierSettings.FedexFinalOrderHour
                        => GetNextAvailableCourier("FEDEX", nowHour),

                    var name when name.Contains("GLS", StringComparison.OrdinalIgnoreCase)
                        && nowHour >= _courierSettings.GlsFinalOrderHour
                        => GetNextAvailableCourier("GLS", nowHour),

                    _ => order.DeliveryMethodName
                };
            }

            return new GaskaCreateOrderRequest
            {
                CustomerNumber = $"{order.RecipientFirstName.Trim()} {order.RecipientLastName.Trim()}".Trim(),
                DeliveryAddressId = addressId,
                DeliveryMethod = order.PaymentType == AllegroPaymentType.CASH_ON_DELIVERY ? "FedEx Dropshipping Pobranie" : deliveryMethod,
                DropshippingAmount = order.PaymentType == AllegroPaymentType.CASH_ON_DELIVERY
                    ? order.Amount
                    : null,
                Items = order.Items?.Select(i => new GaskaCreateOrderItemRequest
                {
                    Id = i.GaskaItemId,
                    Qty = i.Quantity.ToString()
                }).ToList() ?? new List<GaskaCreateOrderItemRequest>()
            };
        }

        private AllegroOrderStatus MapGaskaStatusToAllegro(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return AllegroOrderStatus.PROCESSING;

            // Normalize input for case-insensitive matching
            status = status.Trim().ToLowerInvariant();

            return status switch
            {
                "spakowane" or "czeka na kuriera" => AllegroOrderStatus.READY_FOR_SHIPMENT,
                "wysłane" or "w drodze" => AllegroOrderStatus.SENT,
                "dostarczone" => AllegroOrderStatus.PICKED_UP,
                _ => AllegroOrderStatus.PROCESSING
            };
        }

        private void LogAllegroErrors(HttpResponseMessage response, string body, string action, string orderId)
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<AllegroErrorResponse>(body);
                if (errorResponse?.Errors != null)
                {
                    foreach (var err in errorResponse.Errors)
                    {
                        _logger.LogError("Failed to update {Action} for order {Order}: Code={Code}, Message={Message}, UserMessage={UserMessage}, Path={Path}, Details={Details}",
                             action, orderId, err.Code, err.Message, err.UserMessage ?? "N/A", err.Path ?? "N/A", err.Details ?? "N/A");
                    }
                }
                else
                {
                    _logger.LogError($"Offer error {response.StatusCode}: {body}");
                }
            }
            catch (Exception exParse)
            {
                _logger.LogError(exParse, $"Failed to parse Allegro error ({response.StatusCode}) while  offer for. Body={body}");
            }
        }

        private string GetNextAvailableCourier(string currentCourier, int nowHour)
        {
            var couriers = new List<(string Name, int FinalHour)>
            {
                ("DPD", _courierSettings.DpdFinalOrderHour),
                ("FEDEX", _courierSettings.FedexFinalOrderHour),
                ("GLS", _courierSettings.GlsFinalOrderHour)
            };

            // Skip the current courier and pick the first with final hour > now
            return couriers
                .Where(c => c.Name != currentCourier && nowHour <= c.FinalHour)
                .Select(c => c.Name)
                .FirstOrDefault() ?? currentCourier;
        }

        private string BuildOrderEmailBody(AllegroOrder order, string? errorMessage = null)
        {
            var color = string.IsNullOrEmpty(errorMessage) ? "#28a745" : "#dc3545";
            var headerText = string.IsNullOrEmpty(errorMessage)
                ? "Złożono automatyczne zamówienie w Gąsce"
                : "Wystąpił błąd przy składaniu automatycznego zamówienia w Gąsce";

            var html = $@"
                <html>
                <body style='font-family:Arial,sans-serif; background-color:#f9f9f9; padding:20px;'>
                    <div style='max-width:900px; margin:0 auto; background-color:#ffffff; border-radius:10px; box-shadow:0 4px 10px rgba(0,0,0,0.1); padding:20px;'>
                        <h2 style='color:{color}; text-align:center;'>{headerText}</h2>

                        <p><strong>ID zamówienia Allegro:</strong> {order.AllegroId}</p>
                        <p><strong>Klient:</strong> {order.ClientNickname}</p>
                        <p><strong>Imię i Nazwisko:</strong> {order.RecipientFirstName} {order.RecipientLastName}</p>
                        {(string.IsNullOrEmpty(order.RecipientCompanyName) ? "" : $"<p><strong>Firma:</strong> {order.RecipientCompanyName}</p>")}
                        <p><strong>Email:</strong> {order.RecipientEmail}</p>
                        <p><strong>Telefon:</strong> {order.RecipientPhoneNumber ?? "BRAK"}</p>
                        <p><strong>Adres:</strong> {order.RecipientStreet}, {order.RecipientCity}, {order.RecipientPostalCode}, {order.RecipientCountry}</p>
                        <p><strong>Metoda dostawy:</strong> {order.DeliveryMethodName}</p>
                        {(string.IsNullOrEmpty(order.GaskaOrderNumber) ? "" : $"<p><strong>Numer zamówienia Gąski:</strong> {order.GaskaOrderNumber}</p>")}
                        {(string.IsNullOrEmpty(order.GaskaDeliveryName) ? "" : $"<p><strong>Metoda dostawy Gąski:</strong> {order.GaskaDeliveryName}</p>")}
                        {(string.IsNullOrEmpty(errorMessage) ? "" : $"<p style='color:#dc3545; font-weight:bold;'>Błąd: {errorMessage}</p>")}

                        <h3 style='border-bottom:2px solid #eee; padding-bottom:5px;'>Produkty:</h3>
                        <table style='width:100%; border-collapse:collapse; margin-top:10px;'>
                            <thead style='background-color:#f2f2f2;'>
                                <tr>
                                    <th style='padding:10px; text-align:left; border-bottom:1px solid #ddd;'>Kod</th>
                                    <th style='padding:10px; text-align:left; border-bottom:1px solid #ddd;'>Nazwa</th>
                                    <th style='padding:10px; text-align:center; border-bottom:1px solid #ddd;'>Ilość</th>
                                    <th style='padding:10px; text-align:right; border-bottom:1px solid #ddd;'>Cena jedn.</th>
                                    <th style='padding:10px; text-align:right; border-bottom:1px solid #ddd;'>Razem</th>
                                </tr>
                            </thead>
                            <tbody>";

            foreach (var item in order.Items)
            {
                decimal lineTotal = Convert.ToDecimal(item.PriceGross, CultureInfo.InvariantCulture) * item.Quantity;

                html += $@"
                                <tr>
                                    <td style='padding:10px; border-bottom:1px solid #eee;'>{item.ExternalId}</td>
                                    <td style='padding:10px; border-bottom:1px solid #eee;'>{item.OfferName}</td>
                                    <td style='padding:10px; text-align:center; border-bottom:1px solid #eee;'>{item.Quantity}</td>
                                    <td style='padding:10px; text-align:right; border-bottom:1px solid #eee;'>{Convert.ToDecimal(item.PriceGross, CultureInfo.InvariantCulture):C}</td>
                                    <td style='padding:10px; text-align:right; border-bottom:1px solid #eee;'>{lineTotal:C}</td>
                                </tr>";
            }

            html += $@"
                            </tbody>
                        </table>
                        <p style='text-align:right; font-weight:bold; margin-top:10px;'>Suma zamówienia: {order.Amount:C}</p>
                        <p style='text-align:center; color:#888; margin-top:20px; font-size:12px;'>Email wysłany automatycznie przez usługę AllegroGaskaOrdersSync</p>
                    </div>
                </body>
                </html>";

            return html;
        }
    }
}