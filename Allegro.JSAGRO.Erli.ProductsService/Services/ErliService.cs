using Allegro.JSAGRO.Erli.ProductsService.DTOs;
using Allegro.JSAGRO.Erli.ProductsService.Enums;
using Allegro.JSAGRO.Erli.ProductsService.Mappers;
using Allegro.JSAGRO.Erli.ProductsService.Repositories;
using Allegro.JSAGRO.Erli.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Allegro.JSAGRO.Erli.ProductsService.Services
{
    public class ErliService
    {
        private const int SearchPageSize = 100;
        private const int MaxSearchPages = 10_000;

        private readonly ILogger<ErliService> _logger;
        private readonly AppSettings _appSettings;
        private readonly ErliClient _erliClient;
        private readonly OfferRepository _offerRepository;
        private readonly IAllegroResponsiblePersonRepository _personRepo;
        private readonly IAllegroResponsibleProducerRepository _producerRepo;
        private readonly IAllegroDeliveryMethodRepository _deliveryRepo;

        public ErliService(
            ILogger<ErliService> logger,
            IOptions<AppSettings> appSettings,
            OfferRepository offerRepository,
            ErliClient erliClient,
            IAllegroResponsiblePersonRepository personRepo,
            IAllegroResponsibleProducerRepository producerRepo,
            IAllegroDeliveryMethodRepository deliveryRepo)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            _offerRepository = offerRepository;
            _erliClient = erliClient;
            _personRepo = personRepo;
            _producerRepo = producerRepo;
            _deliveryRepo = deliveryRepo;
        }

        private int UploadParallelism => Math.Clamp(_appSettings.UploadParallelism, 1, 32);

        // ------------------------------------------------------------------ słowniki

        public async Task SyncResponsibleProducersWithErli(CancellationToken ct = default)
        {
            var producers = await _producerRepo.GetAllegroResponsibleProducers();

            await SyncDictionaryAsync(
                "dictionaries/responsibleProducers",
                "responsible producers",
                producers.Select(p => new ErliDictionaryEntry
                {
                    Name = p.TradeName,
                    IdempotenceKey = p.AllegroId,
                    ProperName = p.Name,
                    Country = p.CountryCode,
                    Address = p.Street,
                    PostalCode = p.PostalCode,
                    City = p.City,
                    Phone = p.Phone,
                    Email = p.Email
                }),
                ct);
        }

        public async Task SyncResponsiblePersonsWithErli(CancellationToken ct = default)
        {
            var persons = await _personRepo.GetAllegroResponsiblePersons();

            await SyncDictionaryAsync(
                "dictionaries/responsiblePersons",
                "responsible persons",
                persons.Select(p => new ErliDictionaryEntry
                {
                    Name = p.PersonName,
                    IdempotenceKey = p.AllegroId,
                    ProperName = p.Name,
                    Country = p.CountryCode,
                    Address = p.Street,
                    PostalCode = p.PostalCode,
                    City = p.City,
                    Phone = p.Phone,
                    Email = p.Email
                }),
                ct);
        }

        /// <summary>
        /// Osoby i producenci odpowiedzialni różnią się tylko adresem zasobu,
        /// więc obie synchronizacje idą tą samą ścieżką.
        /// </summary>
        private async Task SyncDictionaryAsync(string endpoint, string label, IEnumerable<ErliDictionaryEntry> entries, CancellationToken ct)
        {
            var existing = await _erliClient.GetAsync<List<ErliDictionaryEntryResponse>>(endpoint, ct);

            var existingByKey = (existing ?? new List<ErliDictionaryEntryResponse>())
                .Where(e => !string.IsNullOrWhiteSpace(e.IdempotenceKey))
                .GroupBy(e => e.IdempotenceKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int created = 0, updated = 0, failed = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (entry.IdempotenceKey != null && existingByKey.TryGetValue(entry.IdempotenceKey, out var match))
                    {
                        await _erliClient.PatchAsync($"{endpoint}/{match.Id}", entry, ct);
                        updated++;
                    }
                    else
                    {
                        await _erliClient.PostAsync(endpoint, entry, ct);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    LogErliError(ex, $"sync {label} entry {entry.IdempotenceKey}");
                }
            }

            _logger.LogInformation("Erli {Label} - created: {Created}, updated: {Updated}, failed: {Failed}.",
                label, created, updated, failed);
        }

        // ------------------------------------------------------------------ cenniki dostaw

        public async Task SyncDeliveriesWithErli(CancellationToken ct = default)
        {
            var deliveryMethods = await _deliveryRepo.GetAllegroDeliveryMethods();
            var erliPriceLists = await _erliClient.GetAsync<List<ErliPriceListResponse>>("delivery/priceListsDetails", ct);

            int created = 0, updated = 0, skipped = 0, failed = 0;

            foreach (var deliveryMethod in deliveryMethods)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var existingPriceList = erliPriceLists?.FirstOrDefault(priceList =>
                        string.Equals(priceList.Name?.Trim(), deliveryMethod.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

                    var prices = BuildPrices(deliveryMethod, existingPriceList);

                    if (prices.Count == 0)
                    {
                        skipped++;
                        _logger.LogWarning("Skipping delivery price list '{DeliveryMethodName}' - no mappable delivery methods.", deliveryMethod.Name);
                        continue;
                    }

                    if (existingPriceList == null)
                    {
                        await _erliClient.PostAsync("delivery/priceList", new ErliPriceListCreate
                        {
                            Name = deliveryMethod.Name,
                            Prices = prices,
                            ErliProEnabled = false,
                            NextDayDeliveryEnabled = false
                        }, ct);

                        created++;
                        continue;
                    }

                    await _erliClient.PatchAsync($"delivery/priceList/{existingPriceList.Id}", new ErliPriceListPatch
                    {
                        Prices = prices,
                        ErliProEnabled = existingPriceList.ErliProEnabled,
                        NextDayDeliveryEnabled = existingPriceList.NextDayDeliveryEnabled
                    }, ct);

                    updated++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogErliError(ex, $"sync delivery price list '{deliveryMethod.Name}'");
                }
            }

            _logger.LogInformation("Erli delivery price lists - created: {Created}, updated: {Updated}, skipped: {Skipped}, failed: {Failed}.",
                created, updated, skipped, failed);
        }

        private List<Prices> BuildPrices(AllegroDeliveryMethod deliveryMethod, ErliPriceListResponse? existingPriceList)
        {
            var pricesByMethod = new Dictionary<ErliDeliveryMethod, Prices>();

            foreach (var detail in deliveryMethod.AllegroDeliveryMethodDetails ?? Enumerable.Empty<AllegroDeliveryMethodDetails>())
            {
                if (!TryMapErliDeliveryMethod(detail, out var erliDeliveryMethod))
                {
                    _logger.LogDebug("Unknown Erli delivery method for '{DeliveryName}'.", detail.Name);
                    continue;
                }

                var existingPrice = existingPriceList?.Prices?.FirstOrDefault(p => p.DeliveryMethod?.Id == erliDeliveryMethod);

                var mappedPrice = new Prices
                {
                    DeliveryMethod = new DeliveryMethod
                    {
                        Id = erliDeliveryMethod,
                        // Erli wymaga czasu dostawy przy kazdym cenniku - przy nowej metodzie
                        // nie mamy go z czego przepisac, wiec podstawiamy wartosc domyslna.
                        DeliveryTime = existingPrice?.DeliveryMethod?.DeliveryTime ?? DefaultDeliveryTime()
                    },
                    BasePrice = ToGrosze(detail.FirstItemAmount),
                    NextItemPrice = ToGrosze(detail.NextItemAmount ?? 0),
                    Limit = BuildErliLimit(erliDeliveryMethod, detail.MaxPackageQuantity),
                    NextDayDeliveryOption = existingPrice?.NextDayDeliveryOption
                };

                // Kilka metod Allegro potrafi zmapować się na tę samą metodę Erli - zostawiamy najtańszą.
                if (pricesByMethod.TryGetValue(erliDeliveryMethod, out var current) && !IsCheaper(mappedPrice, current))
                    continue;

                pricesByMethod[erliDeliveryMethod] = mappedPrice;
            }

            return pricesByMethod.Values.ToList();
        }

        /// <summary>Czas dostawy uzywany, gdy cennik jest zakladany od zera.</summary>
        private static DeliveryTime DefaultDeliveryTime() => new()
        {
            Unit = "DAY",
            MinPeriod = 1,
            MaxPeriod = 2
        };

        private static bool IsCheaper(Prices candidate, Prices current) =>
            candidate.BasePrice < current.BasePrice
            || (candidate.BasePrice == current.BasePrice && candidate.NextItemPrice < current.NextItemPrice);

        private static int ToGrosze(decimal amount) => Convert.ToInt32(Math.Round(amount * 100, MidpointRounding.AwayFromZero));

        // ------------------------------------------------------------------ oferty

        public async Task SyncOffersWithErli(CancellationToken ct = default)
        {
            var offers = _offerRepository.GetOffersWithDetails().ToList();

            // Słownik zamiast szukania liniowego - inaczej przy kilkudziesięciu tysiącach ofert
            // każda strona z Erli przeszukuje całą listę od nowa.
            var offersById = offers
                .Where(o => !string.IsNullOrWhiteSpace(o.Id))
                .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var changed = new List<AllegroOffer>();
            var after = "0";
            int found = 0, pages = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var page = await _erliClient.PostAsync<List<ErliProduct>>("products/_search", new
                {
                    pagination = new { sortField = "externalId", after, order = "ASC", limit = SearchPageSize },
                    fields = new[] { "externalId" }
                }, ct);

                if (page == null || page.Count == 0)
                    break;

                foreach (var item in page)
                {
                    if (string.IsNullOrWhiteSpace(item.ExternalId) || !offersById.TryGetValue(item.ExternalId, out var offer))
                        continue;

                    found++;

                    if (offer.ExistsInErli)
                        continue;

                    offer.ExistsInErli = true;
                    changed.Add(offer);
                }

                if (page.Count < SearchPageSize)
                    break;

                var nextAfter = page[^1].ExternalId;

                // Kursor, który nie idzie do przodu, zapętliłby pobieranie w nieskończoność.
                if (string.IsNullOrWhiteSpace(nextAfter) || nextAfter == after)
                {
                    _logger.LogWarning("Erli pagination cursor stopped advancing at '{After}'. Ending product search.", after);
                    break;
                }

                after = nextAfter;

                if (++pages >= MaxSearchPages)
                {
                    _logger.LogWarning("Erli product search reached the page limit ({MaxPages}).", MaxSearchPages);
                    break;
                }
            }

            _offerRepository.UpdateOffersExistsInErli(changed);

            _logger.LogInformation("Erli offers matched: {Found} of {Total}, newly marked as existing: {Changed}.",
                found, offers.Count, changed.Count);
        }

        public Task CreateProductsInErli(CancellationToken ct = default) =>
            SendProductsAsync(_offerRepository.GetOffersForErliCreation(), isUpdate: false, ct);

        public Task UpdateProductsInErli(CancellationToken ct = default) =>
            SendProductsAsync(_offerRepository.GetOffersForErliUpdate(), isUpdate: true, ct);

        private async Task SendProductsAsync(IEnumerable<AllegroOffer> offers, bool isUpdate, CancellationToken ct)
        {
            var all = offers.ToList();
            var action = isUpdate ? "updated" : "created";

            // Erli identyfikuje produkt po sku, ktorym jest nasz kod produktu. Oferty wystawione
            // recznie na Allegro go nie maja - bez tego kazda konczy sie bledem "sku is not allowed to be empty".
            var toSend = all.Where(o => !string.IsNullOrWhiteSpace(o.ExternalId)).ToList();
            var withoutSku = all.Count - toSend.Count;

            if (withoutSku > 0)
            {
                _logger.LogWarning(
                    "Skipping {Count} Erli products without a product code (sku) - these offers were listed manually on Allegro.",
                    withoutSku);
            }

            if (toSend.Count == 0)
            {
                _logger.LogInformation("No Erli products to be {Action}.", action);
                return;
            }

            var failed = 0;
            var sent = 0;

            // Wysyłka szeregowa zajmowała przy kilku tysiącach produktów godziny.
            await Parallel.ForEachAsync(
                toSend,
                new ParallelOptions { MaxDegreeOfParallelism = UploadParallelism, CancellationToken = ct },
                async (offer, token) =>
                {
                    try
                    {
                        var request = ErliProductMapper.MapFromOffer(offer, _appSettings.CourierPriceSurcharge);
                        var endpoint = $"products/{offer.Id}";

                        if (isUpdate)
                            await _erliClient.PatchAsync(endpoint, request, token);
                        else
                            await _erliClient.PostAsync(endpoint, request, token);

                        Interlocked.Increment(ref sent);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Zatrzymanie usługi - przerywamy cały krok bez zgłaszania błędu.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // TaskCanceledException bez żądania anulowania to timeout HTTP -
                        // czyli błąd pojedynczego produktu, a nie powód przerwania całego kroku.
                        Interlocked.Increment(ref failed);
                        LogErliError(ex, $"{(isUpdate ? "update" : "create")} Erli product '{offer.ExternalId}'");
                    }
                });

            _logger.LogInformation("Erli products {Action}: {Sent}, failed: {Failed}.", action, sent, failed);
        }

        /// <summary>Błąd z API Erli logujemy z odczytanymi polami, a nie jako surowy tekst wyjątku.</summary>
        private void LogErliError(Exception ex, string action)
        {
            if (ex is not ErliApiException erliEx)
            {
                _logger.LogError(ex, "Failed to {Action}.", action);
                return;
            }

            _logger.LogError("Failed to {Action}: {Status} {Error}",
                action, (int)erliEx.StatusCode, erliEx.Error?.Error ?? erliEx.Error?.Message ?? erliEx.Body);

            foreach (var detail in erliEx.Error?.Details ?? new List<ErliApiErrorDetail>())
                _logger.LogError(" - {Field}: {Message}", detail.Field ?? "?", detail.Message);
        }

        // ------------------------------------------------------------------ mapowanie metod dostawy

        private static bool TryMapErliDeliveryMethod(AllegroDeliveryMethodDetails detail, out ErliDeliveryMethod method)
        {
            method = default;

            if (string.IsNullOrWhiteSpace(detail?.Name))
                return false;

            var normalized = NormalizeDeliveryName(detail.Name);

            var isCod = detail.PaymentPolicy == PaymentPolicy.CASH_ON_DELIVERY
                || normalized.Contains("pobranie", StringComparison.Ordinal)
                || normalized.Split(' ').Contains("cod");

            (string[] Keywords, ErliDeliveryMethod Standard, ErliDeliveryMethod? Cod)[] rules =
            [
                (["odbior osobisty"], ErliDeliveryMethod.odbiorOsobisty, ErliDeliveryMethod.odbiorOsobistyCod),
                (["paczkomaty inpost", "paczkomat"], ErliDeliveryMethod.paczkomat, ErliDeliveryMethod.paczkomatCod),
                (["inpost"], ErliDeliveryMethod.inPost, ErliDeliveryMethod.inPostCod),
                (["dpd pickup", "one punkt dpd", "automaty paczkowe dpd", "one box dpd", "punkt dpd"], ErliDeliveryMethod.dpdPunkt, ErliDeliveryMethod.dpdPunktCod),
                (["dpd"], ErliDeliveryMethod.dpd, ErliDeliveryMethod.dpdCod),
                (["dhl box", "automat dhl", "punkcie dhl"], ErliDeliveryMethod.erliDHLPunktyOdbioru10kg, null),
                (["dhl"], ErliDeliveryMethod.dhl, ErliDeliveryMethod.dhlCod),
                (["orlen paczka"], ErliDeliveryMethod.orlenPaczka, ErliDeliveryMethod.orlenPaczkaCod),
                (["pocztex", "poczta polska punkt"], ErliDeliveryMethod.pocztaPolskaPunkt, ErliDeliveryMethod.pocztaPolskaPunktCod),
                (["fedex"], ErliDeliveryMethod.fedex, ErliDeliveryMethod.fedexCod),
                (["gls"], ErliDeliveryMethod.gls, ErliDeliveryMethod.glsCod),
                (["przesylka kurierska", "kurier", "international", "wysylka z polski", "packeta"], ErliDeliveryMethod.courier, ErliDeliveryMethod.courierCod),
            ];

            foreach (var (keywords, standard, cod) in rules)
            {
                if (!keywords.Any(k => normalized.Contains(k, StringComparison.Ordinal)))
                    continue;

                method = isCod && cod.HasValue ? cod.Value : standard;
                return true;
            }

            return false;
        }

        private static string NormalizeDeliveryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var withoutDiacritics = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(withoutDiacritics.Length);

            foreach (var c in withoutDiacritics)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                // Separatory zamieniamy na spacje, żeby "DPD-Pickup" i "DPD Pickup" wyglądały tak samo.
                sb.Append(c is '-' or ',' ? ' ' : char.ToLowerInvariant(c));
            }

            var normalized = sb.ToString().Replace("(ad)", string.Empty);

            // Zwielokrotnione spacje zwijamy do jednej - pojedynczy Replace zostawiał część z nich.
            return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static object BuildErliLimit(ErliDeliveryMethod method, int? maxPackageQuantity)
        {
            var limit = Math.Max(1, maxPackageQuantity ?? 1);

            if (method != ErliDeliveryMethod.erliPaczkomat)
                return limit;

            return new List<DeliveryDimensionLimit>
            {
                new() { Dimension = "A", Limit = limit },
                new() { Dimension = "B", Limit = limit },
                new() { Dimension = "C", Limit = limit }
            };
        }
    }
}
