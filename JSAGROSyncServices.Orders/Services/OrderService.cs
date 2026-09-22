using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.DTOs.Allegro.GaskaApi;
using JSAGROSyncServices.Contracts.DTOs.GaskaApi;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Orders.Configuration;
using JSAGROSyncServices.Orders.Helpers;
using JSAGROSyncServices.Orders.Settings;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Orders.Services
{
    /// <summary>
    /// Przebieg zamówień wspólny dla wszystkich kont. Różnice między kontami
    /// (dropshipping vs wysyłka do magazynu) opisuje <see cref="OrderPipeline"/>.
    /// </summary>
    public class OrderService : IOrderService
    {
        private const int OrderPageSize = 100;
        private const int OfferDetailsParallelism = 5;

        /// <summary>Najstarsze zamówienia, jakie serwis ma w ogóle brać pod uwagę.</summary>
        private static readonly DateTime EarliestOrderDate = new(2026, 02, 11, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Odstęp między utworzeniem adresu a złożeniem zamówienia - Gąska nie lubi zbyt szybkich wywołań.</summary>
        private static readonly TimeSpan GaskaCallSpacing = TimeSpan.FromSeconds(5);

        private readonly ILogger<OrderService> _logger;
        private readonly AppSettings _appSettings;
        private readonly CourierSettings _courierSettings;
        private readonly OrderServiceContext _service;
        private readonly OrderPipeline _pipeline;
        private readonly IOrderRepository _orderRepo;
        private readonly IEmailService _emailService;
        private readonly AllegroApiClient _allegroApiClient;
        private readonly GaskaOrdersApiClient _gaskaApiClient;

        public OrderService(
            ILogger<OrderService> logger,
            IOptions<AppSettings> appSettings,
            IOptions<CourierSettings> courierSettings,
            OrderServiceContext serviceContext,
            OrderPipeline pipeline,
            IOrderRepository orderRepo,
            IEmailService emailService,
            AllegroApiClient allegroApiClient,
            GaskaOrdersApiClient gaskaApiClient)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            _courierSettings = courierSettings.Value;
            _service = serviceContext;
            _pipeline = pipeline;
            _orderRepo = orderRepo;
            _emailService = emailService;
            _allegroApiClient = allegroApiClient;
            _gaskaApiClient = gaskaApiClient;
        }

        private List<string> ManagedDeliveryNames => _appSettings.AllegroDeliveryNames;

        // ------------------------------------------------------------------ Allegro -> baza

        public async Task SyncOrdersFromAllegro(CancellationToken ct = default)
        {
            var deliveryNames = ManagedDeliveryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (deliveryNames.Count == 0)
            {
                _logger.LogError("No Allegro delivery price lists configured. Aborting order sync.");
                return;
            }

            int synced = 0, skipped = 0, failed = 0;

            try
            {
                var shippingRates = await _allegroApiClient.GetAsync<AllegroShippingRatesResponse>("/sale/shipping-rates", ct);

                if (shippingRates?.ShippingRates == null)
                {
                    _logger.LogError("Allegro returned no shipping rates. Aborting order sync.");
                    return;
                }

                // Cenniki dostawy zmieniają się rzadko - mapę budujemy raz na cykl, nie przy każdym zamówieniu.
                var managedRateNamesById = shippingRates.ShippingRates
                    .Where(rate => rate.Id != null && rate.Name != null && deliveryNames.Contains(rate.Name))
                    .ToDictionary(rate => rate.Id!, rate => rate.Name!, StringComparer.OrdinalIgnoreCase);

                if (managedRateNamesById.Count == 0)
                {
                    _logger.LogError("None of the configured delivery price lists exist in Allegro: {DeliveryNames}. Aborting order sync.",
                        string.Join(", ", ManagedDeliveryNames));
                    return;
                }

                var boughtFrom = MaxDate(DateTime.UtcNow.AddDays(-7), EarliestOrderDate)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ");

                // Szczegóły oferty są potrzebne tylko po to, żeby poznać jej cennik dostawy.
                // Ta sama oferta wraca w wielu zamówieniach, więc pytamy o nią raz na cykl.
                var shippingRateIdByOffer = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                for (var offset = 0; ; offset += OrderPageSize)
                {
                    var query = $"order/checkout-forms?limit={OrderPageSize}&offset={offset}&lineItems.boughtAt.gte={boughtFrom}";
                    var response = await _allegroApiClient.GetAsync<AllegroGetOrdersResponse>(query, ct);

                    var orders = response?.CheckoutForms;

                    if (orders == null || orders.Count == 0)
                        break;

                    await LoadMissingOfferShippingRatesAsync(orders, shippingRateIdByOffer, ct);

                    foreach (var order in orders)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var offerShippingRates = BuildOfferShippingRates(order, shippingRateIdByOffer, managedRateNamesById);

                            // Zamówienia z cenników, których nie obsługujemy, pomijamy - na koncie
                            // są też oferty wystawione ręcznie i nie wolno ich ruszać.
                            if (order.LineItems == null || order.LineItems.Count == 0 || offerShippingRates.Values.Any(name => name == null))
                            {
                                skipped++;
                                continue;
                            }

                            var rates = offerShippingRates.ToDictionary(x => x.Key, x => x.Value!, StringComparer.OrdinalIgnoreCase);
                            await _orderRepo.SaveAllegroOrder(MapAllegroOrderToModel(order, rates));
                            synced++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            _logger.LogError(ex, "Failed to sync order {AllegroOrderId} from Allegro.", order.Id);
                        }
                    }

                    if (offset + orders.Count >= response!.TotalCount)
                        break;
                }

                _logger.LogInformation("Orders synced: {Synced}, skipped (other price lists): {Skipped}, failed: {Failed}.",
                    synced, skipped, failed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync orders from Allegro.");
            }
        }

        private async Task LoadMissingOfferShippingRatesAsync(
            IEnumerable<AllegroGetOrdersResponse.CheckoutForm> orders,
            Dictionary<string, string?> shippingRateIdByOffer,
            CancellationToken ct)
        {
            var missing = orders
                .SelectMany(o => o.LineItems ?? new List<AllegroGetOrdersResponse.LineItem>())
                .Select(item => item.Offer?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id) && !shippingRateIdByOffer.ContainsKey(id!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count == 0)
                return;

            using var gate = new SemaphoreSlim(OfferDetailsParallelism);

            var lookups = missing.Select(async offerId =>
            {
                await gate.WaitAsync(ct);

                try
                {
                    var offer = await _allegroApiClient.GetAsync<AllegroMinimalOfferDetails>($"/sale/product-offers/{offerId}", ct);
                    return (OfferId: offerId!, RateId: offer?.Delivery?.ShippingRates?.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read delivery price list of offer {OfferId}.", offerId);
                    return (OfferId: offerId!, RateId: null);
                }
                finally
                {
                    gate.Release();
                }
            });

            foreach (var (offerId, rateId) in await Task.WhenAll(lookups))
                shippingRateIdByOffer[offerId] = rateId;
        }

        private static Dictionary<string, string?> BuildOfferShippingRates(
            AllegroGetOrdersResponse.CheckoutForm order,
            IReadOnlyDictionary<string, string?> shippingRateIdByOffer,
            IReadOnlyDictionary<string, string> managedRateNamesById)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in order.LineItems ?? new List<AllegroGetOrdersResponse.LineItem>())
            {
                var offerId = item.Offer?.Id;

                if (string.IsNullOrWhiteSpace(offerId) || result.ContainsKey(offerId))
                    continue;

                string? name = null;

                if (shippingRateIdByOffer.TryGetValue(offerId, out var rateId)
                    && !string.IsNullOrWhiteSpace(rateId)
                    && managedRateNamesById.TryGetValue(rateId!, out var rateName))
                {
                    name = rateName;
                }

                result[offerId] = name;
            }

            return result;
        }

        // ------------------------------------------------------------------ baza -> Gąska

        public async Task CreateOrdersInGaska(CancellationToken ct = default)
        {
            int created = 0, skipped = 0, failed = 0;

            try
            {
                var orders = await _orderRepo.GetPendingOrdersForExternalCompany(_appSettings.OfferProcessingDelayMinutes);

                foreach (var order in orders)
                {
                    ct.ThrowIfCancellationRequested();

                    var unknownProducts = order.Items
                        .Where(item => item.ProductId is null or 0)
                        .Select(item => item.ExternalId)
                        .ToList();

                    if (unknownProducts.Count > 0)
                    {
                        skipped++;

                        _logger.LogWarning(
                            "Order {AllegroOrderId} not sent to Gąska - products missing in the database: {Codes}.",
                            order.AllegroId, string.Join(", ", unknownProducts));

                        await NotifyOnceAsync(order,
                            $"Zamówienie nie zostało złożone - brak produktów w bazie: {string.Join(", ", unknownProducts)}",
                            $"BŁĄD przy składaniu zamówienia: {order.AllegroId}", ct);

                        continue;
                    }

                    if (await TryCreateOrderInGaskaAsync(order, ct))
                        created++;
                    else
                        failed++;
                }

                _logger.LogInformation("Orders created in Gąska: {Created}, skipped: {Skipped}, failed: {Failed}.",
                    created, skipped, failed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create orders in Gąska.");
            }
        }

        private async Task<bool> TryCreateOrderInGaskaAsync(AllegroOrder order, CancellationToken ct)
        {
            var isDropshipping = _pipeline.IsDropshipping(order.Items.Select(i => i.ShippingRate));

            int addressId;
            string? defaultAddressPhone;

            // Etap 1: adres dostawy. Nieudany etap 1 oznacza, że zamówienie na pewno nie poszło
            // do Gąski - można je spokojnie ponowić w następnym cyklu.
            try
            {
                (addressId, defaultAddressPhone) = await ResolveDeliveryAddressAsync(order, isDropshipping, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare delivery address in Gąska for Allegro order {AllegroOrderId}.", order.AllegroId);
                await NotifyOnceAsync(order, ex.Message, $"BŁĄD przy składaniu zamówienia: {order.AllegroId}", ct);
                return false;
            }

            if (!isDropshipping && string.IsNullOrWhiteSpace(order.RecipientPhoneNumber))
            {
                // Gąska wymaga numeru telefonu, a przy wysyłce do magazynu i tak jedzie na nasz adres.
                order.RecipientPhoneNumber = defaultAddressPhone;
            }

            await Task.Delay(GaskaCallSpacing, ct);

            // Etap 2: samo zamówienie. Tu przerwane połączenie NIE znaczy, że zamówienie nie powstało -
            // żądanie mogło dojść do Gąski. Dlatego oznaczamy je jako złożone i zgłaszamy do weryfikacji.
            try
            {
                var orderRequest = BuildGaskaOrderRequest(order, addressId, isDropshipping);
                var orderResponse = await _gaskaApiClient.PostAsync<GaskaCreateOrderResponse>("order", orderRequest, ct);

                if (orderResponse.Result != 0)
                    throw new InvalidOperationException(orderResponse.Message);

                order.ExternalOrderId = orderResponse.NewOrders.FirstOrDefault();
                await _orderRepo.MarkAsOrderedInExternalCompany(order.Id, order.ExternalOrderId);

                await FetchAndUpdateGaskaOrder(order, ct);
                await SetProcessingStatusInAllegro(order, ct);

                _logger.LogInformation("Created order {GaskaOrderNumber} in Gąska for Allegro order {AllegroOrderId}.",
                    order.ExternalOrderNumber, order.AllegroId);

                await NotifyOnceAsync(order, null, $"Złożono automatyczne zamówienie {order.ExternalOrderNumber}", ct);
                return true;
            }
            catch (Exception ex) when (IsTimeout(ex) && !ct.IsCancellationRequested)
            {
                // Zamówienie mogło zostać przyjęte mimo braku odpowiedzi - powtórzenie
                // złożyłoby je drugi raz, więc blokujemy ponowienie i prosimy o sprawdzenie ręczne.
                _logger.LogWarning(ex,
                    "Timed out while creating order in Gąska for Allegro order {AllegroOrderId}. Marked as ordered - verify manually.",
                    order.AllegroId);

                await _orderRepo.MarkAsOrderedInExternalCompany(order.Id, 0);

                await NotifyOnceAsync(order,
                    "Przekroczono czas oczekiwania na odpowiedź Gąski przy składaniu zamówienia. " +
                    "Zamówienie mogło zostać przyjęte - sprawdź ręcznie w panelu Gąski.",
                    $"TIMEOUT przy składaniu zamówienia: {order.AllegroId}", ct);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order in Gąska for Allegro order {AllegroOrderId}.", order.AllegroId);
                await NotifyOnceAsync(order, ex.Message, $"BŁĄD przy składaniu zamówienia: {order.AllegroId}", ct);
                return false;
            }
        }

        /// <summary>
        /// Przy dropshippingu zakładamy jednorazowy adres klienta, przy wysyłce do magazynu
        /// korzystamy z domyślnego adresu skonfigurowanego w Gąsce.
        /// </summary>
        private async Task<(int AddressId, string? Phone)> ResolveDeliveryAddressAsync(AllegroOrder order, bool isDropshipping, CancellationToken ct)
        {
            if (isDropshipping)
            {
                var addressRequest = BuildGaskaDeliveryAddressRequest(order);
                var addressResponse = await _gaskaApiClient.PostAsync<GaskaCreateDeliveryAddressResponse>("addDeliveryAddress", addressRequest, ct);

                if (addressResponse.Result != 0)
                    throw new InvalidOperationException(addressResponse.Message);

                if (addressResponse.AddressId == 0)
                    throw new InvalidOperationException("Gąska nie zwróciła identyfikatora adresu dostawy.");

                return (addressResponse.AddressId, order.RecipientPhoneNumber);
            }

            var addresses = await _gaskaApiClient.GetAsync<GaskaGetDeliveryAddressesResponse>("deliveryAddresses", ct);

            if (addresses.Result != 0)
                throw new InvalidOperationException(addresses.Message);

            var defaultAddress = addresses.AdressDetails?.FirstOrDefault(a => a.Default);

            if (defaultAddress == null || defaultAddress.Id == 0)
                throw new InvalidOperationException("Nie znaleziono domyślnego adresu dostawy w Gąsce.");

            return (defaultAddress.Id, defaultAddress.Phone);
        }

        public async Task UpdateOrderGaskaInfo(CancellationToken ct = default)
        {
            try
            {
                var orders = await _orderRepo.GetOrdersToUpdateExternalInfo(ManagedDeliveryNames);

                foreach (var order in orders)
                {
                    ct.ThrowIfCancellationRequested();
                    await FetchAndUpdateGaskaOrder(order, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update orders from Gąska.");
            }
        }

        private async Task FetchAndUpdateGaskaOrder(AllegroOrder order, CancellationToken ct = default)
        {
            if (order.ExternalOrderId == 0)
            {
                // Zamówienie oznaczone jako złożone po timeoucie - nie znamy jego numeru w Gąsce.
                _logger.LogWarning("Order {AllegroOrderId} has no Gąska order id. Skipping update.", order.AllegroId);
                return;
            }

            try
            {
                var response = await _gaskaApiClient.GetAsync<GaskaGetOrderResponse>($"order?id={order.ExternalOrderId}&lng=pl", ct);

                if (response.Result != 0)
                {
                    _logger.LogError("Failed to fetch order {GaskaOrderId} from Gąska: {Message}", order.ExternalOrderId, response.Message);
                    return;
                }

                if (response.Order == null)
                {
                    _logger.LogError("Gąska returned no data for order {GaskaOrderId}.", order.ExternalOrderId);
                    return;
                }

                var gaskaItems = response.Order.Items ?? new List<GaskaGetOrderResponse.Item>();

                order.ExternalDeliveryName = response.Order.Delivery;
                order.ExternalOrderStatus = gaskaItems.FirstOrDefault()?.RealizeDeliveryStatus;
                order.ExternalOrderNumber = response.Order.OrderNumber;

                foreach (var item in order.Items)
                {
                    var gaskaItem = gaskaItems.FirstOrDefault(i => i.Id == item.ProductId);

                    if (gaskaItem == null)
                        continue;

                    item.ExternalTrackingNumber = gaskaItem.RealizeTrackingNumber;
                    item.ExternalCourier = gaskaItem.RealizeDelivery;
                }

                await _orderRepo.UpdateOrderExternalInfo(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order {AllegroOrderId} with Gąska data.", order.AllegroId);
            }
        }

        // ------------------------------------------------------------------ baza -> Allegro

        private async Task SetProcessingStatusInAllegro(AllegroOrder order, CancellationToken ct)
        {
            await SendStatusToAllegro(order, AllegroOrderStatus.PROCESSING, ct);
        }

        public async Task UpdateOrdersInAllegro(CancellationToken ct = default)
        {
            int statuses = 0, shipments = 0;

            try
            {
                var orders = await _orderRepo.GetOrdersToUpdateInAllegro();
                var skippedWarehouse = 0;

                foreach (var order in orders)
                {
                    ct.ThrowIfCancellationRequested();

                    // Statusy i numery przesyłek ma tylko dropshipping - tam paczkę nadaje dostawca
                    // i to jego dane są prawdziwe. Towar idący przez nasz magazyn wysyłamy sami,
                    // więc po złożeniu zamówienia u dostawcy zostaje przy statusie PROCESSING,
                    // a resztę uzupełniamy ręcznie.
                    if (!_pipeline.IsDropshipping(order.Items.Select(i => i.ShippingRate)))
                    {
                        skippedWarehouse++;
                        continue;
                    }

                    var status = MapGaskaStatusToAllegro(order.ExternalOrderStatus);

                    if (status != order.RealizeStatus && status != AllegroOrderStatus.NEW)
                    {
                        if (await SendStatusToAllegro(order, status, ct))
                            statuses++;
                    }

                    shipments += await SendTrackingNumbersToAllegro(order, ct);
                }

                _logger.LogInformation(
                    "Allegro orders updated - statuses: {Statuses}, tracking numbers: {Shipments}, skipped (own warehouse): {Skipped}.",
                    statuses, shipments, skippedWarehouse);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update orders in Allegro.");
            }
        }

        private async Task<bool> SendStatusToAllegro(AllegroOrder order, AllegroOrderStatus status, CancellationToken ct)
        {
            try
            {
                var request = new AllegroSetOrderStatusRequest { Status = status };
                var response = await _allegroApiClient.SendWithResponseAsync(
                    $"/order/checkout-forms/{order.AllegroId}/fulfillment", HttpMethod.Put, request, ct);

                if (response.IsSuccessStatusCode)
                {
                    // Zapisujemy stan, ktory Allegro wlasnie przyjelo. Bez tego kolejny cykl
                    // porownuje nowy status ze starym i wysyla dokladnie to samo jeszcze raz.
                    order.RealizeStatus = status;
                    await _orderRepo.UpdateRealizeStatus(order.Id, status);

                    _logger.LogInformation("Order {AllegroOrderId} status set to {Status}.", order.AllegroId, status);
                    return true;
                }

                LogAllegroErrors(response, await response.Content.ReadAsStringAsync(ct), "status", order.AllegroId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set status of Allegro order {AllegroOrderId}.", order.AllegroId);
            }

            return false;
        }

        private async Task<int> SendTrackingNumbersToAllegro(AllegroOrder order, CancellationToken ct)
        {
            int sent = 0;

            try
            {
                // Dostawca potrafi zwrócić kilka numerów przesyłki w jednym polu, rozdzielonych
                // przecinkiem. Allegro nie przyjmuje takiego zlepka - każdy numer idzie osobno.
                var shipments = order.Items
                    .SelectMany(item => SplitTrackingNumbers(item.ExternalTrackingNumber)
                        .Select(waybill => new
                        {
                            Waybill = waybill,
                            Carrier = NormalizeCarrierId(item.ExternalCourier) ?? string.Empty,
                            item.OrderItemId
                        }))
                    .GroupBy(x => new { x.Waybill, x.Carrier })
                    .ToList();

                if (shipments.Count == 0)
                    return 0;

                var known = await GetKnownShipments(order, ct);

                foreach (var shipment in shipments)
                {
                    // Ten numer Allegro juz zna - ponowne wyslanie tylko zakladaloby duplikat przesylki.
                    if (known.Contains(ShipmentKey(shipment.Key.Carrier, shipment.Key.Waybill)))
                        continue;

                    var request = new AllegroAddTrackingNumberRequest
                    {
                        CarrierId = shipment.Key.Carrier,
                        Waybill = shipment.Key.Waybill,
                        LineItems = shipment
                            .Select(x => x.OrderItemId)
                            .Distinct(StringComparer.Ordinal)
                            .Select(id => new AllegroAddTrackingNumberRequest.LineItem { Id = id })
                            .ToList()
                    };

                    var response = await _allegroApiClient.SendWithResponseAsync(
                        $"/order/checkout-forms/{order.AllegroId}/shipments", HttpMethod.Post, request, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        sent++;
                        known.Add(ShipmentKey(shipment.Key.Carrier, shipment.Key.Waybill));
                        await _orderRepo.AddSentShipment(order.Id, shipment.Key.Carrier, shipment.Key.Waybill);

                        _logger.LogInformation("Tracking number {TrackingNumber} ({Carrier}) sent for Allegro order {AllegroOrderId}.",
                            shipment.Key.Waybill, shipment.Key.Carrier, order.AllegroId);
                    }
                    else
                    {
                        LogAllegroErrors(response, await response.Content.ReadAsStringAsync(ct), "tracking numbers", order.AllegroId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send tracking numbers of Allegro order {AllegroOrderId}.", order.AllegroId);
            }

            return sent;
        }

        private static string ShipmentKey(string carrierId, string waybill) => $"{carrierId}|{waybill}".ToUpperInvariant();

        /// <summary>
        /// Numery przesyłek, które Allegro już ma. Przy pierwszym przetwarzaniu zamówienia
        /// pytamy o nie Allegro - zamówienie mogło dostać przesyłki wcześniej (także z tego
        /// serwisu, zanim pamiętał, co wysłał). Potem wystarczy zapis w bazie.
        /// </summary>
        private async Task<HashSet<string>> GetKnownShipments(AllegroOrder order, CancellationToken ct)
        {
            var known = (await _orderRepo.GetSentShipments(order.Id))
                .Select(s => ShipmentKey(s.CarrierId, s.Waybill))
                .ToHashSet(StringComparer.Ordinal);

            if (order.ShipmentsCheckedAt != null)
                return known;

            try
            {
                var response = await _allegroApiClient.GetAsync<AllegroShipmentsResponse>(
                    $"/order/checkout-forms/{order.AllegroId}/shipments", ct);

                foreach (var shipment in response?.Shipments ?? new List<AllegroShipmentsResponse.Shipment>())
                {
                    if (string.IsNullOrWhiteSpace(shipment.Waybill))
                        continue;

                    var carrier = shipment.CarrierId ?? string.Empty;

                    known.Add(ShipmentKey(carrier, shipment.Waybill));
                    await _orderRepo.AddSentShipment(order.Id, carrier, shipment.Waybill);
                }

                await _orderRepo.MarkShipmentsChecked(order.Id);
                order.ShipmentsCheckedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Bez tej listy wyslemy numer, ktory moze juz tam byc - gorsze byloby pominiecie wysylki.
                _logger.LogWarning(ex, "Reading shipments of Allegro order {AllegroOrderId} failed.", order.AllegroId);
            }

            return known;
        }

        private static readonly char[] TrackingNumberSeparators = [',', ';'];

        /// <summary>
        /// Rozbija pole numeru przesyłki na pojedyncze numery. Dostawca zwraca je czasem
        /// jako "30648325292, 30648325293, 30648325296" - Allegro takiego zapisu nie przyjmuje.
        /// </summary>
        private static IEnumerable<string> SplitTrackingNumbers(string? trackingNumbers)
        {
            if (string.IsNullOrWhiteSpace(trackingNumbers))
                return Array.Empty<string>();

            return trackingNumbers
                .Split(TrackingNumberSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string? NormalizeCarrierId(string? carrier) => carrier?.ToUpperInvariant() switch
        {
            null => null,
            var s when s.Contains("DPD") => "DPD",
            var s when s.Contains("FEDEX") => "FEDEX",
            var s when s.Contains("GLS") => "GLS",
            _ => carrier
        };

        // ------------------------------------------------------------------ mapowanie

        private AllegroOrder MapAllegroOrderToModel(AllegroGetOrdersResponse.CheckoutForm allegroOrder, Dictionary<string, string> offerShippingRates)
        {
            var address = allegroOrder.Delivery?.Address;
            var buyer = allegroOrder.Buyer;

            var (street, city) = SplitStreetAndCity(
                address?.Street ?? buyer?.Address?.Street,
                address?.City ?? buyer?.Address?.City);

            return new AllegroOrder
            {
                AllegroId = allegroOrder.Id ?? string.Empty,
                MessageToSeller = allegroOrder.MessageToSeller,
                Note = allegroOrder.Note?.Text,
                Status = allegroOrder.Status,
                RealizeStatus = allegroOrder.Fulfillment?.Status ?? AllegroOrderStatus.NEW,
                ClientNickname = buyer?.Login?.Trim() ?? string.Empty,
                RecipientFirstName = FirstNotEmpty(address?.FirstName, buyer?.Address?.FirstName, buyer?.FirstName),
                RecipientLastName = FirstNotEmpty(address?.LastName, buyer?.Address?.LastName, buyer?.LastName),
                RecipientStreet = street,
                RecipientCity = city,
                RecipientPostalCode = FirstNotEmpty(address?.ZipCode, address?.PostCode, buyer?.Address?.ZipCode, buyer?.Address?.PostCode),
                RecipientCountry = FirstNotEmpty(address?.CountryCode, buyer?.Address?.CountryCode),
                RecipientCompanyName = address?.CompanyName ?? buyer?.CompanyName,
                RecipientEmail = buyer?.Email,
                RecipientPhoneNumber = NullIfEmpty(FirstNotEmpty(address?.PhoneNumber, buyer?.Address?.PhoneNumber, buyer?.PhoneNumber)),
                DeliveryMethodId = allegroOrder.Delivery?.Method?.Id ?? string.Empty,
                DeliveryMethodName = allegroOrder.Delivery?.Method?.Name?.ToUpperInvariant() ?? string.Empty,
                IntegrationCompany = _service.Company,
                Account = _service.Account,
                CancellationDate = allegroOrder.Delivery?.Cancellation?.Date,
                Amount = ParseAmount(allegroOrder.Summary?.TotalToPay?.Amount),
                CreatedAt = allegroOrder.LineItems?.Max(i => (DateTime?)i.BoughtAt) ?? default,
                Revision = allegroOrder.Revision ?? string.Empty,
                PaymentType = allegroOrder.Payment?.Type ?? AllegroPaymentType.ONLINE,
                Items = (allegroOrder.LineItems ?? new List<AllegroGetOrdersResponse.LineItem>())
                    .Select(item => new AllegroOrderItem
                    {
                        ExternalId = item.Offer?.External?.Id ?? string.Empty,
                        // Zestaw produktów - ilość pozycji mnożymy przez ilość sztuk w zestawie.
                        Quantity = item.Quantity * (item.Offer?.ProductSet?.Products?.FirstOrDefault()?.Quantity ?? 1),
                        PriceGross = item.Price?.Amount ?? string.Empty,
                        Currency = item.Price?.Currency ?? string.Empty,
                        OfferName = item.Offer?.Name ?? string.Empty,
                        OfferId = item.Offer?.Id ?? string.Empty,
                        OrderItemId = item.Id ?? string.Empty,
                        ShippingRate = item.Offer?.Id != null && offerShippingRates.TryGetValue(item.Offer.Id, out var rate) ? rate : null,
                        BoughtAt = item.BoughtAt
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Klienci wpisują czasem miasto razem z ulicą ("Kowary ul. Główna 5").
        /// Gąska potrzebuje ich osobno.
        /// </summary>
        private static (string Street, string City) SplitStreetAndCity(string? rawStreet, string? rawCity)
        {
            var street = rawStreet?.Trim() ?? string.Empty;
            var city = rawCity?.Trim() ?? string.Empty;

            if (street.Length == 0)
                return (street, city);

            var match = Regex.Match(street, @"^(?<city>.+?)\s+(?:ul\.?|ulica\.?)\s*(?<street>.+)$", RegexOptions.IgnoreCase);

            return match.Success
                ? (match.Groups["street"].Value.Trim(), match.Groups["city"].Value.Trim())
                : (street, city);
        }

        private GaskaCreateDeliveryAddressRequest BuildGaskaDeliveryAddressRequest(AllegroOrder order) => new()
        {
            Name1 = $"{order.RecipientFirstName} {order.RecipientLastName}".Trim(),
            Street = order.RecipientStreet,
            City = order.RecipientCity,
            PostalCode = order.RecipientPostalCode,
            Country = order.RecipientCountry,
            Phone = order.RecipientPhoneNumber,
            Email = _appSettings.DeliveryAddressEmail,
            OneUse = true
        };

        private GaskaCreateOrderRequest BuildGaskaOrderRequest(AllegroOrder order, int addressId, bool isDropshipping)
        {
            // Do magazynu jedzie zawsze naszym kurierem; do klienta - tym, który wybrał na Allegro.
            var courier = NormalizeCourierName(isDropshipping ? order.DeliveryMethodName : _pipeline.WarehouseCourier);

            var isCashOnDelivery = order.PaymentType == AllegroPaymentType.CASH_ON_DELIVERY && isDropshipping;

            if (!isCashOnDelivery && !IsWeekend(DateTime.Now))
                courier = SwitchCourierAfterCutoff(courier, DateTime.Now.Hour);

            return new GaskaCreateOrderRequest
            {
                CustomerNumber = $"{order.RecipientFirstName.Trim()} {order.RecipientLastName.Trim()}".Trim(),
                DeliveryAddressId = addressId,
                DeliveryMethod = isCashOnDelivery ? CashOnDeliveryMethod : courier,
                DropshippingAmount = isCashOnDelivery ? order.Amount : null,
                Items = order.Items
                    .Select(i => new GaskaCreateOrderItemRequest
                    {
                        Id = i.ProductId ?? 0,
                        Qty = i.Quantity.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList()
            };
        }

        private const string CashOnDeliveryMethod = "FedEx Dropshipping Pobranie";

        private static bool IsWeekend(DateTime moment) =>
            moment.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        /// <summary>Po godzinie granicznej kurier już nie odbierze - szukamy takiego, który jeszcze zdąży.</summary>
        private string SwitchCourierAfterCutoff(string courier, int hour)
        {
            var cutoff = CutoffHour(courier);

            if (cutoff == null || hour < cutoff)
                return courier;

            var replacement = CourierCutoffs()
                .Where(c => !c.Name.Equals(courier, StringComparison.OrdinalIgnoreCase) && hour < c.Hour)
                .Select(c => c.Name)
                .FirstOrDefault();

            return replacement == null ? courier : NormalizeCourierName(replacement);
        }

        private int? CutoffHour(string courier) => CourierCutoffs()
            .Where(c => c.Name.Equals(courier, StringComparison.OrdinalIgnoreCase))
            .Select(c => (int?)c.Hour)
            .FirstOrDefault();

        private (string Name, int Hour)[] CourierCutoffs() =>
        [
            ("DPD", _courierSettings.DpdFinalOrderHour),
            ("FedEx", _courierSettings.FedexFinalOrderHour),
            ("GLS", _courierSettings.GlsFinalOrderHour)
        ];

        private static string NormalizeCourierName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            if (name.Contains("DPD", StringComparison.OrdinalIgnoreCase)) return "DPD";
            if (name.Contains("FEDEX", StringComparison.OrdinalIgnoreCase)) return "FedEx";
            if (name.Contains("GLS", StringComparison.OrdinalIgnoreCase)) return "GLS";

            return name;
        }

        private static AllegroOrderStatus MapGaskaStatusToAllegro(string? status) => status?.Trim().ToLowerInvariant() switch
        {
            "spakowane" or "czeka na kuriera" or "zrealizowane" => AllegroOrderStatus.READY_FOR_SHIPMENT,
            "wysłane" or "w drodze" => AllegroOrderStatus.SENT,
            "dostarczone" => AllegroOrderStatus.PICKED_UP,
            _ => AllegroOrderStatus.PROCESSING
        };

        // ------------------------------------------------------------------ powiadomienia i pomocnicze

        /// <summary>Jedno powiadomienie na zamówienie - inaczej ten sam błąd szedłby mailem co cykl.</summary>
        private async Task NotifyOnceAsync(AllegroOrder order, string? errorMessage, string subject, CancellationToken ct)
        {
            if (order.EmailSent)
                return;

            try
            {
                var body = OrderEmailBuilder.Build(order, _service.Account, errorMessage);
                await _emailService.SendEmailAsync(_service.MailSender, _appSettings.NotificationsEmail, subject, body);
                await _orderRepo.SetEmailSent(order.Id);
                order.EmailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification for Allegro order {AllegroOrderId}.", order.AllegroId);
            }
        }

        private void LogAllegroErrors(HttpResponseMessage response, string body, string action, string orderId)
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<AllegroErrorResponse>(body);

                if (errorResponse?.Errors is { Count: > 0 })
                {
                    foreach (var error in errorResponse.Errors)
                    {
                        _logger.LogError("Failed to update {Action} of order {AllegroOrderId}: {Code} - {Message}",
                            action, orderId, error.Code, error.UserMessage ?? error.Message);
                    }

                    return;
                }
            }
            catch (JsonException)
            {
                // Odpowiedź nie jest błędem Allegro - logujemy surową treść poniżej.
            }

            _logger.LogError("Failed to update {Action} of order {AllegroOrderId}: {Status} {Body}",
                action, orderId, response.StatusCode, body);
        }

        private static bool IsTimeout(Exception ex) =>
            ex is TaskCanceledException or TimeoutException
            || (ex is HttpRequestException http && http.InnerException is TimeoutException);

        private static DateTime MaxDate(DateTime left, DateTime right) => left > right ? left : right;

        private static decimal ParseAmount(string? amount) =>
            decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;

        private static string FirstNotEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
