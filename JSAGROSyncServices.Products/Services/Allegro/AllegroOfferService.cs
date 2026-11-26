using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Helpers;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    public class AllegroOfferService : IAllegroOfferService
    {
        private const int OfferPageSize = 1000;
        /// <summary>Ile zapytań wysyłamy równolegle. Z konfiguracji, bo zależy od limitów konta.</summary>
        private int MaxParallelism => Math.Clamp(_appSettings.AllegroParallelism, 1, 25);
        private const int MaxOfferDetailsPerCycle = 3000;

        /// <summary>Ile ofert konczymy jedna komenda publikacji.</summary>
        private const int EndOffersBatchSize = 500;

        /// <summary>Allegro ma parametr "Producent części" pod dwoma id, zależnie od kategorii.</summary>
        private static readonly int[] ManufacturerParameterIds = [127415, 247835];

        /// <summary>
        /// Zliczanie błędów Allegro wg kodu w obrębie jednego kroku. Pojedyncze błędy nadal
        /// trafiają do logu, a to podsumowanie pokazuje, co faktycznie blokuje wystawianie.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _errorCounts = new();

        /// <summary>
        /// Oferty do zakończenia po kroku aktualizacji: produkt z katalogu nie ma kompletu parametrów
        /// obowiązkowych, a zastępczego nie znaleziono. Kończymy je zbiorczo po pętli aktualizacji.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _offersToEndAfterUpdate = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Id produktów z katalogu, które Allegro zgłosiło jako nieistniejące. Allegro nadal oddaje je
        /// przy ofercie, więc bez tej listy patch szedł z martwym id w każdym cyklu. Lista żyje tyle,
        /// co proces - usunięty produkt z katalogu nie wraca.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> DeadCatalogProductIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Oferty podpięte po stronie Allegro pod usunięty produkt - każdy patch kończy się ProductNotFoundException.
        /// Po zakończeniu ich nie patchujemy; trzeba je wystawić od nowa. Lista żyje tyle, co proces.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> UnpatchableOffers = new(StringComparer.OrdinalIgnoreCase);

        private void CountError(string code) =>
            _errorCounts.AddOrUpdate(string.IsNullOrWhiteSpace(code) ? "-" : code, 1, (_, current) => current + 1);

        private void LogErrorSummary(string step)
        {
            if (_errorCounts.IsEmpty)
                return;

            var summary = string.Join(", ", _errorCounts
                .OrderByDescending(x => x.Value)
                .Select(x => $"{x.Key}: {x.Value}"));

            _logger.LogWarning("{Step} - error breakdown: {Summary}", step, summary);
            _errorCounts.Clear();
        }

        // Cena oferty nie jest aktualizowana, wiec warunek spadku utrzymuje sie miedzy cyklami.
        // Bez tego o tym samym produkcie szedlby mail w kazdym przebiegu.
        private static readonly ConcurrentDictionary<string, DateTime> PriceDropAlerts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan PriceDropAlertInterval = TimeSpan.FromHours(24);

        private readonly ILogger<AllegroOfferService> _logger;
        private readonly ServiceContext _service;
        private readonly IProductRepository _productRepo;
        private readonly IOfferRepository _offerRepo;
        private readonly IParameterRepository _parameterRepo;
        private readonly IImageRepository _imageRepo;
        private readonly IOfferFactory _offerFactory;
        private readonly IAllegroProductService _allegroProductService;
        private readonly OfferCategoryAligner _categoryAligner;
        private readonly IEmailService _emailService;
        private readonly AllegroApiClient _apiClient;
        private readonly AppSettings _appSettings;
        private readonly PriceSettings _priceSettings;
        private readonly AllegroSettings _allegroSettings;

        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        public AllegroOfferService(
            ILogger<AllegroOfferService> logger,
            ServiceContext serviceContext,
            IProductRepository productRepo,
            IOfferRepository offerRepo,
            IParameterRepository parameterRepo,
            IImageRepository imageRepo,
            IOfferFactory offerFactory,
            IAllegroProductService allegroProductService,
            OfferCategoryAligner categoryAligner,
            IEmailService emailService,
            AllegroApiClient apiClient,
            IOptions<AppSettings> appSettings,
            IOptions<PriceSettings> priceSettings,
            IOptions<AllegroSettings> allegroSettings)
        {
            _logger = logger;
            _service = serviceContext;
            _productRepo = productRepo;
            _offerRepo = offerRepo;
            _parameterRepo = parameterRepo;
            _imageRepo = imageRepo;
            _offerFactory = offerFactory;
            _allegroProductService = allegroProductService;
            _categoryAligner = categoryAligner;
            _emailService = emailService;
            _apiClient = apiClient;
            _appSettings = appSettings.Value;
            _priceSettings = priceSettings.Value;
            _allegroSettings = allegroSettings.Value;
        }

        // ---------------------------------------------------------------- pobieranie ofert

        public async Task SyncAllegroOffers(CancellationToken ct = default)
        {
            try
            {
                var allOffers = await FetchAllOffers(ct);

                var shippingRates = await _apiClient.GetAsync<AllegroShippingRatesResponse>("/sale/shipping-rates", ct);
                var shippingDict = shippingRates?.ShippingRates?
                    .Where(rate => rate.Id != null && rate.Name != null)
                    .ToDictionary(rate => rate.Id!, rate => rate.Name!)
                    ?? new Dictionary<string, string>();

                var offersWithExternalId = allOffers.Where(o => !string.IsNullOrEmpty(o?.External?.Id)).ToList();
                var offersWithoutExternalId = allOffers.Where(o => string.IsNullOrEmpty(o?.External?.Id)).ToList();

                var latestOffers = offersWithExternalId
                    .GroupBy(o => o.External!.Id!)
                    .Select(g => g.OrderByDescending(o => o.Id).First())
                    .ToList();

                latestOffers.AddRange(offersWithoutExternalId
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                    .GroupBy(o => o.Name)
                    .Select(g => g.OrderByDescending(o => o.Id).First()));

                foreach (var offer in latestOffers)
                {
                    if (offer.Delivery?.ShippingRates?.Id != null &&
                        shippingDict.TryGetValue(offer.Delivery.ShippingRates.Id, out var name))
                    {
                        offer.Delivery.ShippingRates.Name = name;
                    }
                }

                await _offerRepo.UpsertOffers(latestOffers, ct);

                var byStatus = string.Join(", ", latestOffers
                    .GroupBy(o => o.Publication?.Status ?? "-")
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}: {g.Count()}"));

                var managed = latestOffers.Count(o => _appSettings.ManagedDeliveryNames
                    .Contains(o.Delivery?.ShippingRates?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase));

                _logger.LogInformation(
                    "Offers fetched from Allegro: {Count} (raw: {Raw}, in managed price lists: {Managed}). Statuses - {ByStatus}.",
                    latestOffers.Count, allOffers.Count, managed, byStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetching offers from Allegro failed.");
                throw;
            }
        }

        private async Task<List<Offer>> FetchAllOffers(CancellationToken ct)
        {
            var allOffers = new ConcurrentBag<Offer>();

            var firstPage = await _apiClient.GetAsync<OffersResponse>($"/sale/offers?limit={OfferPageSize}&offset=0", ct);

            if (firstPage?.Offers == null || firstPage.Offers.Count == 0)
                return new List<Offer>();

            foreach (var offer in firstPage.Offers)
                allOffers.Add(offer);

            var totalPages = (int)Math.Ceiling((double)firstPage.TotalCount / OfferPageSize);

            var offsets = Enumerable.Range(1, Math.Max(totalPages - 1, 0))
                .Select(page => page * OfferPageSize)
                .ToList();

            var failedOffsets = new ConcurrentBag<int>();

            await Parallel.ForEachAsync(offsets, ParallelOptions(ct), async (offset, token) =>
            {
                try
                {
                    var page = await _apiClient.GetAsync<OffersResponse>($"/sale/offers?limit={OfferPageSize}&offset={offset}", token);

                    if (page?.Offers == null)
                        return;

                    foreach (var offer in page.Offers)
                        allOffers.Add(offer);
                }
                catch (Exception ex)
                {
                    // Zerwane polaczenie albo blad TLS nie jest kodem HTTP, wiec polityka ponowien
                    // w kliencie go nie obsluguje. Utraconej strony nie wolno zostawic: oferty z niej
                    // nie dostalyby w tym cyklu ani aktualizacji ceny, ani stanu.
                    failedOffsets.Add(offset);
                    _logger.LogWarning(ex, "Fetching offers page (offset {Offset}) failed - retrying once after the other pages.", offset);
                }
            });

            foreach (var offset in failedOffsets)
            {
                try
                {
                    var page = await _apiClient.GetAsync<OffersResponse>($"/sale/offers?limit={OfferPageSize}&offset={offset}", ct);

                    if (page?.Offers == null)
                        continue;

                    foreach (var offer in page.Offers)
                        allOffers.Add(offer);

                    _logger.LogInformation("Offers page (offset {Offset}) fetched on the second attempt.", offset);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fetching offers page (offset {Offset}) failed twice - up to {Count} offers are missing from this cycle.",
                        offset, OfferPageSize);
                }
            }

            return allOffers.ToList();
        }

        public async Task SyncAllegroOffersDetails(CancellationToken ct = default)
        {
            try
            {
                var pending = await _offerRepo.GetOffersWithoutDetails(ct);

                if (pending.Count == 0)
                {
                    _logger.LogInformation("Offer details: nothing pending.");
                    return;
                }

                // Allegro limituje liczbę zapytań, więc zaległości nadrabiamy partiami.
                var offers = pending.Take(MaxOfferDetailsPerCycle).ToList();

                _logger.LogInformation(
                    "Offer details: {Pending} offers pending, fetching {Batch} in this cycle ({Parallelism} in parallel).",
                    pending.Count, offers.Count, MaxParallelism);

                var details = new ConcurrentBag<AllegroOfferDetails.Root>();
                var detailsOptions = ParallelOptions(ct);

                int notFound = 0, empty = 0, failed = 0;
                var sw = Stopwatch.StartNew();

                await Parallel.ForEachAsync(offers, detailsOptions, async (offer, token) =>
                {
                    try
                    {
                        var detailedOffer = await _apiClient.GetAsync<AllegroOfferDetails.Root>($"/sale/product-offers/{offer.Id}", token);

                        if (detailedOffer is null)
                        {
                            // Powod (404, limit, blad serwera) klient zalogowal juz przy zapytaniu.
                            Interlocked.Increment(ref notFound);
                            return;
                        }

                        detailedOffer.Delivery ??= new AllegroOfferDetails.Delivery();
                        detailedOffer.Delivery.ShippingRates ??= new AllegroOfferDetails.ShippingRates();
                        detailedOffer.Delivery.ShippingRates.Id = offer.DeliveryName;

                        if (detailedOffer.Description?.Sections == null || detailedOffer.Description.Sections.Count == 0)
                        {
                            // Oferta produktowa bierze opis z produktu Allegro - własnego nie ma.
                            Interlocked.Increment(ref empty);
                        }

                        // Dokładamy do zapisu niezależnie od tego, czy oferta miała własny opis -
                        // inaczej wracałaby do kolejki w każdym cyklu.
                        detailedOffer.Id ??= offer.Id;
                        details.Add(detailedOffer);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogError(ex, "Fetching details of offer {OfferId} failed.", offer.Id);
                    }
                });

                sw.Stop();

                // Znacznik "pobrane" stawiamy dopiero po udanym zapisie - inaczej chwilowy błąd bazy
                // wypchnąłby oferty z kolejki bez zapisanych opisów i parametrów.
                var savedIds = details.IsEmpty
                    ? Array.Empty<string>()
                    : (await _offerRepo.UpsertOfferDetails(details.ToList(), ct)).ToArray();

                if (savedIds.Length > 0)
                    await _offerRepo.MarkDetailsFetched(savedIds, ct);

                var remaining = pending.Count - savedIds.Length;

                _logger.LogInformation(
                    "Offer details fetched: {Fetched} (no own description: {Empty}), no data returned: {NotFound}, failed: {Failed}. " +
                    "Still pending: {Remaining}. Took {Duration}.",
                    savedIds.Length, empty, notFound, failed, remaining, Format(sw.Elapsed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fetching offer details failed.");
            }
        }

        // ---------------------------------------------------------------- wystawianie ofert

        public async Task CreateOffers(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpload(_appSettings.MinProductStock, _appSettings.MinProductPriceNet, ct);
                await LoadBlockedImages(ct);

                if (products.Count == 0)
                {
                    _logger.LogInformation("No products to create offers for.");
                    return;
                }

                _logger.LogInformation(
                    "Products to create offers for: {Count} (min stock {MinStock}, min net price {MinPrice}).",
                    products.Count, _appSettings.MinProductStock, _appSettings.MinProductPriceNet);

                await _offerFactory.PrepareAsync(ct);

                int created = 0;
                int failed = 0;
                int fixedData = 0;
                int skipped = 0;
                int noDelivery = 0;
                int noCatalogProduct = 0;

                await Parallel.ForEachAsync(products, ParallelOptions(ct), async (product, token) =>
                {
                    try
                    {
                        // Nowej oferty na podejrzanie niskiej cenie zakupu nie wystawiamy wcale -
                        // inaczej zablokowana cena zostalaby ceną startową oferty.
                        if (IsPurchasePriceDropTooLarge(product, out var dropPercent))
                        {
                            Interlocked.Increment(ref skipped);

                            _logger.LogWarning(
                                "Purchase price drop {Drop:F0}% for {Code}: {OldPurchase:F2} -> {NewPurchase:F2} PLN. Offer not created.",
                                dropPercent, product.Code, product.AcceptedPriceGross, product.PriceGross);

                            await SendPriceDropAlert(product, dropPercent, "Oferta nie została wystawiona.");
                            return;
                        }

                        // Dostawca, u ktorego wystawiamy tylko na gotowym produkcie z katalogu
                        // (Inter Cars): bez dopasowanego produktu oferta czeka na krok wyszukiwania.
                        if (_offerFactory.RequiresCatalogProduct && string.IsNullOrWhiteSpace(product.AllegroId))
                        {
                            Interlocked.Increment(ref noCatalogProduct);
                            _logger.LogDebug("No Allegro catalog product for {Code} - offer not created.", product.Code);
                            return;
                        }

                        if (_offerFactory.UsesOwnImages)
                        {
                            product.AllegroImages = await ImportImages(product, token);

                            // Allegro wymaga co najmniej jednego zdjecia - bez niego odrzuca oferte
                            // bledem GallerySize. Brak zdjec to brak danych od dostawcy albo nieudane
                            // pobranie, wiec produkt czeka na kolejny cykl.
                            if (product.AllegroImages.Count == 0)
                            {
                                Interlocked.Increment(ref skipped);
                                _logger.LogDebug("No images for {Code} - offer not created.", product.Code);
                                return;
                            }
                        }

                        // Bez pasującego cennika nie ma czym wysłać - Allegro i tak odrzuciłoby ofertę.
                        if (DeliveryMatcher.Match(product, _appSettings.Deliveries) == null)
                        {
                            Interlocked.Increment(ref noDelivery);
                            _logger.LogDebug("No delivery price list fits {Code} - offer not created.", product.Code);
                            return;
                        }

                        // Nowa oferta powstaje w kategorii produktu z katalogu - tej samej na wszystkich kontach.
                        if (await _categoryAligner.AlignBeforeCreateAsync(product, token))
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        ApplyGalleryLimit(product);

                        ProductOfferRequest BuildCreate()
                        {
                            var built = _offerFactory.BuildOffer(product);
                            BlockedImageFilter.Apply(built, BlockedImagesSnapshot(), _catalogImages);
                            return built;
                        }

                        var request = BuildCreate();
                        var response = await _apiClient.SendWithResponseAsync("/sale/product-offers", HttpMethod.Post, request, token);
                        var body = await response.Content.ReadAsStringAsync(token);

                        if (await ShrinkGalleryAfterRejection(product, product.AllegroId, response, body, token))
                        {
                            request = BuildCreate();
                            response = await _apiClient.SendWithResponseAsync("/sale/product-offers", HttpMethod.Post, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        // Produkt z katalogu ma zablokowane zdjęcie (np. logo) - wystawiamy z jego listą zdjęć bez niego.
                        var createCatalogId = request.ProductSet?.Select(s => s.ProductObject?.Id).FirstOrDefault(IsCatalogId);

                        if (IsContentRejection(response.StatusCode) && IsBlockedImage(body) && createCatalogId != null
                            && !_catalogImages.ContainsKey(createCatalogId)
                            && (await CatalogImages(createCatalogId, token))?.Any(BlockedImagesSnapshot().Contains) == true)
                        {
                            request = BuildCreate();
                            response = await _apiClient.SendWithResponseAsync("/sale/product-offers", HttpMethod.Post, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        var dataFixed = await HandleAllegroResponse(product, response, body, isUpdate: false);

                        if (response.IsSuccessStatusCode)
                            Interlocked.Increment(ref created);
                        else if (dataFixed)
                            Interlocked.Increment(ref fixedData);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogError(ex, "Offer create failed for {Code}.", product.Code);
                    }
                });

                _logger.LogInformation(
                    "Offers created: {Created}, failed: {Failed}, skipped: {Skipped}, no matching delivery price list: {NoDelivery}, " +
                    "no Allegro catalog product: {NoCatalogProduct}, data fixed (retry next cycle): {Fixed}.",
                    created, failed, skipped, noDelivery, noCatalogProduct, fixedData);
                LogErrorSummary("Offers creation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creating offers failed.");
            }
        }

        // ---------------------------------------------------------------- aktualizacja ofert

        public async Task UpdateOffers(CancellationToken ct = default)
        {
            try
            {
                await _offerFactory.PrepareAsync(ct);

                var offersToEnd = await _offerRepo.GetOffersToEnd(ct);

                var endedCount = await EndOffers(
                    offersToEnd,
                    "are outside the configured categories or their product was withdrawn by the supplier",
                    ct);

                // Zakończonych ofert nie aktualizujemy - patch ustawiłby im status z powrotem na ACTIVE.
                var offerIdsToEnd = offersToEnd
                    .Select(o => o.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var candidates = (await _offerRepo.GetOffersToUpdate(ct))
                    .Where(o => !offerIdsToEnd.Contains(o.Id))
                    .ToList();

                // Produkt, który przestał mieścić się w jakimkolwiek cenniku (zmienione wymiary
                // u dostawcy albo zmieniona konfiguracja cenników), nie ma czym jechać - oferty
                // nie da się dalej realizować, więc ją kończymy. Ofert z cenników prowadzonych
                // ręcznie to nie dotyczy, bo ich dostawy w ogóle nie ustawiamy.
                var undeliverable = candidates
                    .Where(o => !PriceHelper.IsManuallyManagedDelivery(o.DeliveryName, _appSettings.DeliveriesWithoutPriceUpdate)
                                && DeliveryMatcher.Match(o.Product!, _appSettings.Deliveries) == null)
                    .ToList();

                if (undeliverable.Count > 0)
                    endedCount += await EndOffers(undeliverable, "no longer fit any delivery price list", ct);

                var undeliverableIds = undeliverable
                    .Select(o => o.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var offers = candidates
                    .Where(o => !undeliverableIds.Contains(o.Id))
                    .ToList();

                // Przy dostawcy wystawianym tylko na produkcie z katalogu patch bez id produktu
                // bylby propozycja nowego produktu - Allegro odrzuca ja brakiem parametrow.
                // Oferta zostaje bez zmian do czasu, gdy krok wyszukiwania znajdzie produkt.
                var withoutCatalogProduct = 0;

                if (_offerFactory.RequiresCatalogProduct)
                {
                    // Decyduje id zapamiętane przy produkcie. Id na samej ofercie bywa nieaktualne:
                    // zostaje po produkcie, który zniknął z katalogu albo został odpięty, a patch
                    // z takim id wraca błędem w każdym cyklu.
                    var linked = offers
                        .Where(o => !string.IsNullOrWhiteSpace(o.Product?.AllegroId))
                        .ToList();

                    var unlinked = offers.Count - linked.Count;
                    withoutCatalogProduct = unlinked;

                    // Parametry produktu bierzemy wyłącznie z katalogu, więc oferta bez produktu
                    // z katalogu nie dostaje aktualizacji stanu ani ceny - kończymy ją, zamiast
                    // sprzedawać ze starym stanem. Dotyczy też drugiego konta, gdy produkt odpięło
                    // pierwsze. Po podpięciu nowego produktu zwykła aktualizacja wznowi ofertę.
                    if (_service.ProductParametersFromCatalog && unlinked > 0)
                    {
                        var linkedIds = linked.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var toEndUnlinked = offers.Where(o => !linkedIds.Contains(o.Id)).ToList();

                        endedCount += await EndOffers(toEndUnlinked, "have no Allegro catalog product", ct);
                    }

                    offers = linked;
                }

                _logger.LogInformation(
                    "Offers to update: {Count} (in {Parallel} parallel requests), to end: {ToEnd} (including {NoDelivery} with no matching delivery price list), " +
                    "waiting for an Allegro catalog product: {WithoutProduct}.",
                    offers.Count, MaxParallelism, offersToEnd.Count + undeliverable.Count, undeliverable.Count, withoutCatalogProduct);

                if (offers.Count == 0)
                {
                    _logger.LogInformation("No offers to update.");
                    return;
                }

                int updated = 0;
                int failed = 0;
                int fixedData = 0;
                int skipped = 0;
                int priceAndStockOnly = 0;
                int movedToCategory = 0;
                int moveRejected = 0;

                // Skróty treści z ostatnich udanych pełnych patchy - bez zmian treści wysyłamy sam stan i cenę.
                var contentStates = await _offerRepo.GetContentStates(ct);
                var contentStateChanges = new ConcurrentBag<(string OfferId, string? Hash)>();
                Interlocked.Exchange(ref _blockedImageProbes, 0);
                await LoadBlockedImages(ct);

                // Lista do podsumowania nie może zatrzymać aktualizacji ofert (np. przed migracją tabeli).
                HashSet<string> knownUnfixable;

                try
                {
                    knownUnfixable = (await _offerRepo.GetUnfixableOffers(ct)).Select(o => o.OfferId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reading the list of offers not fixable via the API failed - updating offers without it.");
                    knownUnfixable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                var unfixableChanges = new ConcurrentBag<(string OfferId, string Code, string? CatalogProductId, string? Reason)>();

                await Parallel.ForEachAsync(offers, ParallelOptions(ct), async (offer, token) =>
                {
                    try
                    {
                        var product = offer.Product!;

                        // Powód, dla którego oferty nie da się naprawić przez API (do podsumowania na końcu cyklu).
                        string? unfixableReason = null;

                        // Zakończona oferta podpięta po stronie Allegro pod usunięty produkt nie przyjmie żadnego
                        // patcha - nie wysyłamy go co cykl tylko po to, żeby dostać ten sam błąd.
                        if (UnpatchableOffers.ContainsKey(offer.Id) && !AllegroOfferStatus.IsPublished(offer.Status))
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        if (_offerFactory.UsesOwnImages && product.AllegroImages.Count == 0)
                        {
                            var images = await ImportImages(product, token);

                            if (images.Count == 0)
                            {
                                Interlocked.Increment(ref skipped);
                                _logger.LogDebug("No images for {Code} - offer not updated.", product.Code);
                                return;
                            }

                            product.AllegroImages = images;
                        }

                        // Produkt z katalogu, którego Allegro już nie ma - nie wysyłamy go ponownie.
                        if (!string.IsNullOrWhiteSpace(offer.ProductId) && DeadCatalogProductIds.ContainsKey(offer.ProductId))
                            offer.ProductId = null;

                        if (!string.IsNullOrWhiteSpace(product.AllegroId) && DeadCatalogProductIds.ContainsKey(product.AllegroId))
                            product.AllegroId = null;

                        // Oferta stoi na produkcie z katalogu, który odrzuciliśmy (np. zablokowane zdjęcie), a produkt
                        // ma już inne powiązanie - przepinamy ją na nie, żeby oba konta miały ten sam productId.
                        await AlignOfferToProductLink(offer, product, token);

                        var keepCurrentPrice = await CheckPriceDrop(offer);

                        ApplyGalleryLimit(product);

                        // Oferty produktu stoją w tej samej kategorii na wszystkich kontach: przy rozjeździe
                        // patch przenosi ofertę razem z parametrami kategorii docelowej.
                        var catalogProductId = !string.IsNullOrWhiteSpace(product.AllegroId) ? product.AllegroId : offer.ProductId;
                        var move = await _categoryAligner.PlanAsync(offer, catalogProductId, token);
                        var offerCategoryParameters = product.Parameters;

                        ProductOfferRequest BuildPatch(bool clearCompatibility = false)
                        {
                            product.Parameters = move?.Parameters ?? offerCategoryParameters;

                            var built = _offerFactory.PatchOffer(offer, keepCurrentPrice);

                            if (move != null)
                            {
                                OfferCategoryAligner.ApplyTo(move, built);

                                if (clearCompatibility)
                                    built.RemoveCompatibilityList();
                            }

                            // Zdjęcia zablokowane przez Allegro wycinamy z galerii, opisu i zdjęć produktu z katalogu.
                            BlockedImageFilter.Apply(built, BlockedImagesSnapshot(), _catalogImages);

                            return built;
                        }

                        var fullRequest = BuildPatch();

                        // Kompletność liczymy na tym, co faktycznie wysyłamy - wiersz w bazie nie znaczy, że fabryka
                        // go wyśle (np. EAN z puli RCN). Bez kompletu przeniesienie czeka na krok parametrów.
                        if (move != null)
                        {
                            var missing = OfferCategoryAligner.MissingIn(move, fullRequest);

                            if (missing.Count > 0)
                            {
                                _logger.LogDebug("Offer {OfferId} ({Code}) waits to move from category {From} to {To}: missing {Missing}.",
                                    offer.Id, product.Code, offer.CategoryId, move.CategoryId, string.Join(", ", missing));

                                move = null;
                                fullRequest = BuildPatch();
                            }
                        }
                        var contentHash = OfferPatchContent.ContentHash(fullRequest);
                        var unchanged = move == null
                            && OfferPatchContent.IsUnchanged(contentHash, contentStates.GetValueOrDefault(offer.Id), DateTime.UtcNow);

                        var request = unchanged ? OfferPatchContent.PriceAndStockOnly(fullRequest) : fullRequest;
                        var response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                        var body = await response.Content.ReadAsStringAsync(token);

                        // Patch ze stanem i ceną przechodzi pełną walidację oferty. Gdy Allegro odrzuci go
                        // z powodu treści (np. pusta galeria po asynchronicznym odrzuceniu zdjęć), od razu
                        // wysyłamy pełną strukturę - ona może to naprawić, a ponowna walidacja to jedno zapytanie.
                        if (unchanged && IsContentRejection(response.StatusCode))
                        {
                            _logger.LogInformation("Price and stock patch for {Code} (offer {OfferId}) rejected ({Status}) - sending the full offer.",
                                product.Code, offer.Id, (int)response.StatusCode);

                            unchanged = false;
                            request = fullRequest;
                            response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        // Tabela zgodności ("Pasuje do") ma typ, którego kategoria docelowa nie obsługuje - próbujemy
                        // przenieść ofertę raz jeszcze z pustą tabelą.
                        if (move != null && IsContentRejection(response.StatusCode) && body.Contains("ValidCompatibilityTable", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Moving offer {OfferId} ({Code}) to category {To}: compatibility table not supported there - retrying without it.",
                                offer.Id, product.Code, move.CategoryId);

                            fullRequest = BuildPatch(clearCompatibility: true);
                            contentHash = OfferPatchContent.ContentHash(fullRequest);
                            request = fullRequest;
                            response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        // Allegro nie przyjęło przeniesienia (np. kategoria wymaga czegoś, czego nie mamy) - oferta
                        // dostaje zwykły patch w swojej kategorii, a próba wraca dopiero po okresie karencji.
                        if (move != null && IsContentRejection(response.StatusCode))
                        {
                            _categoryAligner.RecordRejected(offer.Id, move.CategoryId);
                            Interlocked.Increment(ref moveRejected);

                            // Aktualizacja w miejscu idzie zaraz niżej, więc to informacja, nie błąd.
                            _logger.LogInformation("Offer {OfferId} ({Code}) stays in category {From} - Allegro rejected the move to {To} (next attempt in {Days} days): {Reason}",
                                offer.Id, product.Code, offer.CategoryId, move.CategoryId, OfferCategoryAligner.RetryRejectedAfter.TotalDays, RejectionReason(body));

                            move = null;
                            fullRequest = BuildPatch();
                            contentHash = OfferPatchContent.ContentHash(fullRequest);
                            request = fullRequest;
                            response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        // Za duża galeria: przycinamy własne zdjęcia do miejsca, które zostawia produkt z katalogu,
                        // i od razu ponawiamy pełny patch - inaczej oferta nie dostawała ani stanu, ani ceny.
                        if (await ShrinkGalleryAfterRejection(product, catalogProductId, response, body, token))
                        {
                            unchanged = false;
                            fullRequest = BuildPatch();
                            contentHash = OfferPatchContent.ContentHash(fullRequest);
                            request = fullRequest;
                            response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                            body = await response.Content.ReadAsStringAsync(token);
                        }

                        // Zdjęcie zablokowane przez Allegro: wycinamy tylko je. Najpierw dociągamy zdjęcia produktu
                        // z katalogu (logo bywa w nim), a gdy znane zablokowane adresy nie wystarczą - szukamy
                        // zablokowanego zdjęcia po jednym. Udana próba to od razu pełna aktualizacja oferty.
                        if (IsContentRejection(response.StatusCode) && IsBlockedImage(body))
                        {
                            var fixedBlocked = await FixBlockedImages(offer, product, BuildPatch, token);

                            if (fixedBlocked != null)
                            {
                                unchanged = false;
                                fullRequest = fixedBlocked.Value.Request;
                                contentHash = OfferPatchContent.ContentHash(fullRequest);
                                request = fullRequest;
                                response = fixedBlocked.Value.Response;
                                body = fixedBlocked.Value.Body;
                            }
                            else
                            {
                                // Lista zablokowanych mogła się zmienić - kolejne próby budujemy od nowa.
                                fullRequest = BuildPatch();
                                unchanged = false;
                            }
                        }

                        // Allegro zablokowało któreś zdjęcie, którego nie umiemy usunąć. Pełny patch nie przejdzie,
                        // ale stan i cena mogą - galeria i opis na Allegro zostają wtedy bez zmian.
                        if (!unchanged && IsContentRejection(response.StatusCode) && IsBlockedImage(body))
                        {
                            _logger.LogWarning("Offer {OfferId} ({Code}): Allegro blocked an image - updating price and stock only, gallery and description left unchanged. Sent images: {Images}",
                                offer.Id, product.Code, SentImages(fullRequest));

                            unchanged = true;
                            move = null; // patch ze stanem i ceną nie przenosi kategorii
                            request = OfferPatchContent.PriceAndStockOnly(fullRequest);
                            response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                            body = await response.Content.ReadAsStringAsync(token);

                            // Sam stan i cena też odrzucone: zablokowane zdjęcie nie jest w tym, co wysyłamy, tylko
                            // w produkcie z katalogu (productSet) albo w obecnej ofercie. Próbujemy bez productSet.
                            if (IsContentRejection(response.StatusCode) && IsBlockedImage(body))
                            {
                                _logger.LogWarning("Offer {OfferId} ({Code}): price and stock patch with catalog product {ProductId} also rejected for a blocked image - retrying without productSet.",
                                    offer.Id, product.Code, catalogProductId ?? "-");

                                request = OfferPatchContent.PriceAndStockOnly(fullRequest);
                                request.ProductSet = null!; // null pomijany w JSON - patch bez productSet
                                response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, token);
                                body = await response.Content.ReadAsStringAsync(token);
                            }

                            // Nawet stan i cena odrzucone - zablokowanego zdjęcia nie umiemy usunąć (np. jedyne zdjęcie
                            // oferty ze znakiem wodnym). Trafia do podsumowania na końcu cyklu.
                            if (IsContentRejection(response.StatusCode) && IsBlockedImage(body))
                                unfixableReason = "Zablokowane zdjęcie, którego nie da się usunąć bez opróżnienia galerii (np. jedyne zdjęcie ze znakiem wodnym) - potrzebne nowe zdjęcie albo ręczna edycja w Allegro.";
                        }

                        var dataFixed = await HandleAllegroResponse(product, response, body, isUpdate: true, offer.Id, offer.CategoryId);

                        if (response.IsSuccessStatusCode && move != null)
                        {
                            Interlocked.Increment(ref movedToCategory);
                            _logger.LogInformation("Offer {OfferId} ({Code}) moved from category {From} to {To}.",
                                offer.Id, product.Code, offer.CategoryId, move.CategoryId);
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            if (unchanged)
                                Interlocked.Increment(ref priceAndStockOnly);
                            else
                                contentStateChanges.Add((offer.Id, contentHash));
                        }
                        else if (contentStates.ContainsKey(offer.Id))
                        {
                            // Po błędzie kolejny cykl wysyła pełną strukturę - mogła to naprawić tylko ona.
                            contentStateChanges.Add((offer.Id, null));
                        }

        
                        if (response.IsSuccessStatusCode)
                        {
                            if (knownUnfixable.Contains(offer.Id))
                                unfixableChanges.Add((offer.Id, product.Code, null, null));
                        }
                        else if (unfixableReason != null)
                        {
                            unfixableChanges.Add((offer.Id, product.Code, IsCatalogId(offer.ProductId) ? offer.ProductId : null, unfixableReason));
                        }

                        if (response.IsSuccessStatusCode)
                            Interlocked.Increment(ref updated);
                        else if (dataFixed)
                            Interlocked.Increment(ref fixedData);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogError(ex, "Offer update failed for {Code}.", offer.Product!.Code);
                    }
                });

                if (!_offersToEndAfterUpdate.IsEmpty)
                {
                    var incomplete = offers.Where(o => _offersToEndAfterUpdate.ContainsKey(o.Id)).ToList();
                    _offersToEndAfterUpdate.Clear();

                    endedCount += await EndOffers(incomplete, "have an Allegro catalog product without required parameters and no complete replacement", ct);
                }

                try
                {
                    await _offerRepo.SaveContentStates(contentStateChanges.ToList(), ct);
                }
                catch (Exception ex)
                {
                    // Bez zapisu kolejny cykl wyśle po prostu pełne patche - nic się nie psuje.
                    _logger.LogWarning(ex, "Saving offer content hashes failed - the next cycle sends full updates.");
                }

                try
                {
                    await _offerRepo.SaveUnfixableOffers(unfixableChanges.ToList(), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Saving the list of offers not fixable via the API failed.");
                }

                _logger.LogInformation(
                    "Offers updated: {Updated} (price and stock only: {PriceAndStock}, full: {Full}, moved to the product category: {Moved}, move rejected: {MoveRejected}), " +
                    "failed: {Failed}, skipped: {Skipped}, ended: {Ended}, data fixed (retry next cycle): {Fixed}.",
                    updated, priceAndStockOnly, updated - priceAndStockOnly, movedToCategory, moveRejected, failed, skipped, endedCount, fixedData);
                LogErrorSummary("Offers update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Updating offers failed.");
            }
        }

        /// <summary>
        /// Podsumowanie na końcu cyklu: wszystkie oferty konta, których nie da się naprawić przez API -
        /// także te zapisane w poprzednich cyklach. Lista znika z logu dopiero, gdy oferta przejdzie.
        /// </summary>
        public async Task LogUnfixableSummary(CancellationToken ct = default)
        {
            try
            {
                var offers = await _offerRepo.GetUnfixableOffers(ct);

                if (offers.Count == 0)
                {
                    _logger.LogInformation("=== Not fixable via the Allegro API: none ===");
                    return;
                }

                _logger.LogWarning("=== Not fixable via the Allegro API: {Count} offers (including earlier cycles) - need manual action in Allegro ===", offers.Count);

                foreach (var o in offers)
                {
                    _logger.LogWarning(" - {Code} (offer {OfferId}, catalog product {CatalogProductId}, since {FirstSeen:yyyy-MM-dd HH:mm}, last {LastSeen:yyyy-MM-dd HH:mm}): {Reason}",
                        o.Code, o.OfferId, o.CatalogProductId ?? "-", o.FirstSeenAt.ToLocalTime(), o.LastSeenAt.ToLocalTime(), o.Reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading the list of offers not fixable via the API failed.");
            }
        }

        /// <summary>
        /// Kończy podane oferty. Dotyczy wyłącznie ofert z cenników obsługiwanych przez serwis -
        /// ofert wystawionych ręcznie nie ruszamy.
        ///
        /// Używamy komendy publikacji zamiast PATCH-a na ofercie: PATCH przechodzi pełną walidację
        /// i Allegro odrzuca oferty bez podpiętego produktu ("ValidProductization"), mimo że
        /// chcemy je tylko zakończyć.
        /// </summary>
        private async Task<int> EndOffers(List<AllegroOffer> offersToEnd, string reason, CancellationToken ct)
        {
            if (offersToEnd.Count == 0)
                return 0;

            // Komende END wysylamy tylko do ofert faktycznie opublikowanych. Oferta ENDED jest juz
            // zakonczona, a INACTIVE nigdy nie byla wystawiona - w obu przypadkach komenda nic nie
            // zmienia, wiec status zostaje taki sam i ta sama oferta wracalaby tu w kazdym cyklu.
            // Z aktualizacji wypada niezaleznie od tego, czy komenda poszla.
            var toEnd = offersToEnd.Where(o => AllegroOfferStatus.IsPublished(o.Status)).ToList();
            var notPublished = offersToEnd.Count - toEnd.Count;

            _logger.LogInformation(
                "{Count} offers {Reason} - ending {ToEnd} (already not published: {NotPublished}).",
                offersToEnd.Count, reason, toEnd.Count, notPublished);

            if (toEnd.Count == 0)
                return 0;

            int ended = 0, failed = 0;

            foreach (var batch in toEnd.Chunk(EndOffersBatchSize))
            {
                ct.ThrowIfCancellationRequested();

                var commandId = Guid.NewGuid().ToString();

                var request = new
                {
                    publication = new { action = "END" },
                    offerCriteria = new[]
                    {
                        new
                        {
                            offers = batch.Select(o => new { id = o.Id }).ToArray(),
                            type = "CONTAINS_OFFERS"
                        }
                    }
                };

                try
                {
                    var response = await _apiClient.SendWithResponseAsync(
                        $"/sale/offer-publication-commands/{commandId}",
                        HttpMethod.Put,
                        request,
                        ct);

                    var body = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        ended += batch.Length;

                        _logger.LogInformation("Ending {Count} offers requested (command {CommandId}).", batch.Length, commandId);

                        foreach (var offer in batch)
                            _logger.LogDebug("Offer {OfferId} ({Code}) queued to end: {Reason}.", offer.Id, offer.Product?.Code, reason);
                    }
                    else
                    {
                        failed += batch.Length;
                        CountError($"END HTTP {(int)response.StatusCode}");
                        _logger.LogError("Ending a batch of {Count} offers failed: {Status} {Body}", batch.Length, (int)response.StatusCode, Shorten(body));
                    }
                }
                catch (Exception ex)
                {
                    failed += batch.Length;
                    _logger.LogError(ex, "Ending a batch of {Count} offers failed.", batch.Length);
                }
            }

            if (failed > 0)
                _logger.LogWarning("Offers ending: requested {Ended}, failed {Failed}.", ended, failed);

            return ended;
        }

        /// <summary>
        /// Zwraca true, jeśli cena zakupu spadła skokowo ponad limit - wtedy traktujemy ją
        /// jak możliwy błąd dostawcy i nie przenosimy na Allegro.
        ///
        /// Ceną odniesienia jest ostatnia zaakceptowana cena zakupu, a nie cena oferty:
        /// cena oferty zmienia się też po zmianie marż, kosztów wysyłki czy dopłat,
        /// a te nie mają nic wspólnego z błędem w cenniku.
        /// </summary>
        private decimal PurchasePriceDropPercent(RolmarProduct product)
        {
            var reference = product.AcceptedPriceGross ?? 0m;

            if (_priceSettings.MaxPriceDropPercent <= 0 || reference <= 0 || product.PriceGross >= reference)
                return 0m;

            return (reference - product.PriceGross) / reference * 100m;
        }

        private bool IsPurchasePriceDropTooLarge(RolmarProduct product, out decimal dropPercent)
        {
            dropPercent = PurchasePriceDropPercent(product);
            return dropPercent > _priceSettings.MaxPriceDropPercent;
        }

        /// <summary>Zwraca true, jeśli cena nie ma być aktualizowana z powodu zbyt dużego spadku ceny zakupu.</summary>
        private async Task<bool> CheckPriceDrop(AllegroOffer offer)
        {
            var product = offer.Product!;

            if (!IsPurchasePriceDropTooLarge(product, out var dropPercent))
                return false;

            var newPrice = _offerFactory.CalculatePrice(product);

            _logger.LogWarning(
                "Purchase price drop {Drop:F0}% for {Code}: {OldPurchase:F2} -> {NewPurchase:F2} PLN (detected {Detected:yyyy-MM-dd}). " +
                "Offer price stays {OfferPrice:F2} PLN instead of {NewPrice:F2} PLN.",
                dropPercent, product.Code, product.AcceptedPriceGross, product.PriceGross,
                product.PriceDropDetectedAt, offer.Price, newPrice);

            await SendPriceDropAlert(product, dropPercent, $"Cena oferty pozostaje {offer.Price:F2} PLN (wyliczona: {newPrice:F2} PLN).");
            return true;
        }

        private async Task SendPriceDropAlert(RolmarProduct product, decimal dropPercent, string consequence)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.PriceDropAlertEmail))
                return;

            var now = DateTime.UtcNow;

            if (PriceDropAlerts.TryGetValue(product.Code, out var lastSentUtc) && now - lastSentUtc < PriceDropAlertInterval)
                return;

            PriceDropAlerts[product.Code] = now;

            // Slownik zyje tak dlugo jak proces - wyrzucamy wpisy, ktore sie juz przedawnily.
            foreach (var stale in PriceDropAlerts.Where(x => now - x.Value > PriceDropAlertInterval).Select(x => x.Key).ToList())
                PriceDropAlerts.TryRemove(stale, out _);

            var body = $"<p>Cena zakupu spadła o {dropPercent:F0}% (limit {_priceSettings.MaxPriceDropPercent}%) dla produktu <b>{product.Name}</b> ({product.Code}).</p>" +
                       $"<p>Cena zakupu brutto: {product.AcceptedPriceGross:F2} PLN → {product.PriceGross:F2} PLN</p>" +
                       $"<p>{consequence}</p>" +
                       $"<p>Blokada zniknie sama, gdy cena zakupu wróci do poprzedniego poziomu.</p>";

            try
            {
                await _emailService.SendEmailAsync(
                    _service.MailSender,
                    _appSettings.PriceDropAlertEmail,
                    $"Duży spadek ceny zakupu: {product.Name} ({product.Code})",
                    body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending price drop alert for {Code} failed.", product.Code);
            }
        }

        // ---------------------------------------------------------------- odpowiedzi z Allegro

        /// <summary>Zwraca true, gdy blad zostal naprawiony w danych produktu i ma sens kolejna proba.</summary>
        private async Task<bool> HandleAllegroResponse(RolmarProduct product, HttpResponseMessage response, string body, bool isUpdate, string? offerId = null, int? offerCategoryId = null)
        {
            var action = isUpdate ? "update" : "create";

            switch ((int)response.StatusCode)
            {
                case 200:
                case 201:
                case 202:
                    await _imageRepo.MarkImagesAsConnectedAsync(product.Id, CancellationToken.None);
                    return false;

                case 400:
                case 422:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    return await HandleAllegroErrors(product, response, body, isUpdate, offerId, offerCategoryId ?? product.DefaultAllegroCategory);

                case 404:
                    _logger.LogWarning("Offer {OfferId} not found in Allegro ({Code}). Removing from database.", offerId, product.Code);
                    if (!string.IsNullOrEmpty(offerId))
                        await _offerRepo.DeleteOffer(offerId, CancellationToken.None);
                    return false;

                default:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    CountError($"HTTP {(int)response.StatusCode}");
                    _logger.LogError("Offer {Action} failed for {Code}: {Status} {Body}", action, product.Code, (int)response.StatusCode, Shorten(body));
                    return false;
            }
        }

        private async Task<bool> HandleAllegroErrors(RolmarProduct product, HttpResponseMessage response, string body, bool isUpdate, string? offerId, int offerCategoryId)
        {
            var action = isUpdate ? "update" : "create";

            AllegroErrorResponse? errorResponse = null;

            try
            {
                errorResponse = JsonSerializer.Deserialize<AllegroErrorResponse>(body, _options);
            }
            catch
            {
                // obsluzone nizej - zalogujemy surowa tresc
            }

            if (errorResponse?.Errors == null || errorResponse.Errors.Count == 0)
            {
                if (body.Contains(@"The type of this ""Compatible with"" "))
                    await _productRepo.UpdateCompatibilitySet(product.Id, false, CancellationToken.None);

                CountError($"HTTP {(int)response.StatusCode}");
                _logger.LogError("Offer {Action} failed for {Code}: {Status} {Body}", action, product.Code, (int)response.StatusCode, Shorten(body));
                return false;
            }

            var fixedCount = 0;
            var unfixedCount = 0;

            // Produkt z katalogu podmieniamy najwyżej raz na odpowiedź - Allegro potrafi zgłosić
            // kilka błędów naraz, a każdy z nich prowadziłby do tej samej podmiany.
            var catalogProductReplaced = false;

            foreach (var err in errorResponse.Errors)
            {
                var userMessage = err.UserMessage ?? string.Empty;
                var message = err.Message ?? string.Empty;

                if (_service.ProductParametersFromCatalog && IsCatalogProductDefect(err.Code ?? string.Empty, userMessage, message))
                {
                    if (!catalogProductReplaced)
                    {
                        catalogProductReplaced = true;
                        await ReplaceCatalogProduct(product, err, userMessage, message, offerId, offerCategoryId);
                    }

                    fixedCount++;
                    continue;
                }

                if (await TryFixProductData(product, err, userMessage, message, response, offerId, offerCategoryId))
                {
                    fixedCount++;
                    continue;
                }

                unfixedCount++;
                CountError(err.Code ?? "-");

                _logger.LogError(
                    "Offer {Action} failed for {Code} (offer {OfferId}, category {CategoryId}): {ErrorCode} - {Message} [path: {Path}, details: {Details}]",
                    action, product.Code, offerId ?? "-", product.DefaultAllegroCategory, err.Code ?? "-",
                    string.IsNullOrWhiteSpace(userMessage) ? message : userMessage,
                    string.IsNullOrWhiteSpace(err.Path) ? "-" : err.Path,
                    string.IsNullOrWhiteSpace(err.Details) ? "-" : Shorten(err.Details));
            }

            // Ponawiamy tylko wtedy, gdy wszystkie bledy dalo sie naprawic. Przy choc jednym
            // nienaprawionym kolejna proba z tymi samymi danymi skonczy sie tak samo.
            if (fixedCount > 0 && unfixedCount > 0)
            {
                _logger.LogWarning(
                    "Offer {Action} for {Code}: fixed {Fixed} of {Total} errors, {Unfixed} need manual attention - not retrying.",
                    action, product.Code, fixedCount, fixedCount + unfixedCount, unfixedCount);
            }

            return fixedCount > 0 && unfixedCount == 0;
        }

        /// <summary>
        /// Część błędów Allegro da się naprawić w danych produktu (kategoria, parametr).
        /// Zwraca true, gdy błąd został obsłużony i nie trzeba go logować jako błędu.
        /// </summary>
        private async Task<bool> TryFixProductData(RolmarProduct product, AllegroError err, string userMessage, string message, HttpResponseMessage response, string? offerId, int offerCategoryId)
        {
            var code = err.Code ?? string.Empty;

            if (((code.Contains("ProductConstraintViolationException", StringComparison.OrdinalIgnoreCase) && userMessage.Contains("kategorii produktu", StringComparison.OrdinalIgnoreCase))
                 || code.Contains("CATEGORY_MISMATCH", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrEmpty(userMessage))
            {
                var correctCategoryId = ExtractCorrectCategoryId(userMessage);

                if (!string.IsNullOrEmpty(correctCategoryId))
                {
                    await _productRepo.UpdateProductAllegroCategory(product.Id, Convert.ToInt32(correctCategoryId), CancellationToken.None);
                    _logger.LogDebug("Category fixed for {Code} -> {CategoryId}.", product.Code, correctCategoryId);
                    return true;
                }

                return false;
            }

            // Ten sam produkt z katalogu ma już aktywną ofertę innego naszego produktu - Allegro nie pozwala
            // aktywować drugiej. Id odrzucamy dla tego produktu (zostaje przy tym, którego oferta jest aktywna)
            // i szukamy innego; wyszukiwanie pomija produkty z katalogu podpięte już gdzie indziej. Bez tego
            // oferta wracała z tym błędem w każdym cyklu.
            if (_service.ProductParametersFromCatalog && IsDuplicateActiveOffer(userMessage, message))
            {
                await _productRepo.UnlinkAllegroProduct(product.Id, offerId, reject: true, CancellationToken.None);
                var previousId = product.AllegroId;
                product.AllegroId = null;

                _logger.LogWarning(
                    "Allegro product {ProductId} for {Code} (offer {OfferId}) already has an active offer of another product - " +
                    "id rejected for this product, looking for another one.",
                    previousId ?? "-", product.Code, offerId ?? "-");

                return await RelinkCatalogProduct(product, offerId);
            }

            // Allegro odłączyło produkt z katalogu od oferty i nie pozwala go podpiąć ponownie.
            // Zapamiętujemy jego id (wyszukiwanie go pominie), czyścimy je i szukamy innego produktu -
            // inaczej ta sama oferta wracała z tym błędem w każdym cyklu.
            if (code == "ProductConstraintViolationException.DataIntegrity"
                && userMessage.Contains("odłączony", StringComparison.OrdinalIgnoreCase))
            {
                await _productRepo.RejectAllegroProduct(product.Id, offerId, CancellationToken.None);
                product.AllegroId = null;

                _logger.LogWarning("Allegro detached catalog product from offer {OfferId} ({Code}) - id rejected, looking for another one.",
                    offerId ?? "-", product.Code);

                // Bez innego produktu oferta poczeka - ponowna próba z tym samym id skończyłaby się tak samo.
                await RelinkCatalogProduct(product, offerId);
                return true;
            }

            // Brak parametrów obowiązkowych: przeliczamy parametry produktu (także dla kategorii,
            // w której stoi oferta), a przy produkcie z katalogu wysyłamy je razem z jego id.
            if (code.EndsWith("MissingRequiredParameters", StringComparison.OrdinalIgnoreCase))
            {
                var alreadyRequested = await _productRepo.RequestCatalogParameters(product.Id, CancellationToken.None);

                if (alreadyRequested)
                    return false;

                _logger.LogInformation("Missing required parameters for {Code} (offer {OfferId}) - parameters will be recomputed and sent with the product.",
                    product.Code, offerId ?? "-");
                return true;
            }

            // Allegro samo podaje wartość, jakiej oczekuje - stosujemy ją, zamiast wpisywać własną.
            if ((code == "PARAMETER_MISMATCH" && !string.IsNullOrEmpty(userMessage))
                || (code == "ProductConstraintViolationException.DataIntegrity"
                    && ((message.Contains("Incorrect value of the") && message.Contains("parameter for the offered product"))
                        // Ten sam blad po polsku - np. zly EAN, gdzie Allegro podaje prawidlowa wartosc.
                        || userMessage.Contains("Niepoprawna wartość parametru", StringComparison.OrdinalIgnoreCase))))
            {
                var sourceMessage = string.IsNullOrEmpty(userMessage) ? message : userMessage;
                var correctValue = ExtractCorrectParameterValue(sourceMessage);

                if (string.IsNullOrEmpty(correctValue))
                {
                    _logger.LogWarning("Cannot read the expected parameter value for {Code}. Message: {Message}", product.Code, Shorten(sourceMessage));
                    return false;
                }

                // Komunikat podaje id tylko czasem; zwykle mamy samą nazwę parametru.
                var parameterId = ExtractParameterIdFromConstraintMessage(message) ?? ExtractParameterIdFromConstraintMessage(userMessage);
                var parameterName = ExtractParameterNameFromMismatchMessage(sourceMessage);

                bool updated;

                if (!string.IsNullOrEmpty(parameterId))
                    updated = await _parameterRepo.UpdateParameter(product.Id, Convert.ToInt32(parameterId), correctValue, CancellationToken.None);
                else if (!string.IsNullOrEmpty(parameterName))
                    updated = await _parameterRepo.UpdateParameterByName(product.Id, parameterName, correctValue, CancellationToken.None);
                else
                {
                    _logger.LogWarning("Cannot identify the parameter to fix for {Code}. Message: {Message}", product.Code, Shorten(sourceMessage));
                    return false;
                }

                // Brak zmiany: parametr ma już tę wartość, ale nie dociera do Allegro (brak wiersza
                // w kategorii oferty albo parametr produktu, którego przy produkcie z katalogu nie
                // wysyłaliśmy) - np. "Producent części" `John Deere` zamiast `JAG` w każdym cyklu.
                if (!updated && !string.IsNullOrEmpty(parameterName))
                    updated = await _parameterRepo.SetParameterByName(product.Id, offerCategoryId, parameterName, correctValue, CancellationToken.None);

                if (!updated)
                {
                    // Brak zmiany oznacza, ze parametr juz ma te wartosc albo nie ma go w kategorii -
                    // ponowna proba z tymi samymi danymi skonczy sie tym samym bledem.
                    _logger.LogWarning(
                        "Parameter '{Parameter}' for {Code} was not changed (already '{Value}' or missing in category {CategoryId}).",
                        parameterName ?? parameterId ?? "-", product.Code, correctValue, product.DefaultAllegroCategory);

                    return false;
                }

                _logger.LogInformation("Parameter '{Parameter}' fixed for {Code} -> '{Value}'.",
                    parameterName ?? parameterId, product.Code, correctValue);

                return true;
            }

            // Dopiero gdy Allegro nie podaje oczekiwanej wartości, wstawiamy własnego producenta.
            if (HasDefaultManufacturer
                && code == "ProductValidationException"
                && userMessage.Contains("Tworzenie produktu z wartością", StringComparison.OrdinalIgnoreCase)
                && userMessage.Contains("parametrze Producent części", StringComparison.OrdinalIgnoreCase))
            {
                // "Producent części" ma w Allegro dwa id zależnie od kategorii - produkt ma tylko jedno z nich.
                var updated = false;

                foreach (var manufacturerParameterId in ManufacturerParameterIds)
                    updated |= await _parameterRepo.UpdateParameter(product.Id, manufacturerParameterId, _allegroSettings.DefaultPartsManufacturer, CancellationToken.None);

                if (!updated)
                {
                    _logger.LogWarning(
                        "Cannot fix manufacturer for {Code}: no 'Producent części' parameter in category {CategoryId}.",
                        product.Code, product.DefaultAllegroCategory);

                    return false;
                }

                _logger.LogInformation("Manufacturer set to '{Manufacturer}' for {Code}.", _allegroSettings.DefaultPartsManufacturer, product.Code);
                return true;
            }

            // Wartość spoza słownika kategorii ("An incorrect value `Inna` has been entered for parameter `Marka`").
            // Marce dajemy "bez marki", inny parametr usuwamy - obowiązkowy zostanie wyliczony
            // ponownie, a nieobowiązkowy po prostu nie pójdzie.
            if (code == "DictionaryParameterValueNotFound")
            {
                var (badValue, parameterName) = ExtractDictionaryValueError(string.IsNullOrEmpty(message) ? userMessage : message);

                if (string.IsNullOrEmpty(parameterName))
                    return false;

                if (string.Equals(parameterName, "Marka", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(badValue, ParameterFallbacks.UnbrandedValue, StringComparison.OrdinalIgnoreCase))
                        return false;

                    var changed = await _parameterRepo.SetParameterByName(product.Id, offerCategoryId, parameterName, ParameterFallbacks.UnbrandedValue, CancellationToken.None);

                    if (changed)
                        _logger.LogInformation("Parameter 'Marka' for {Code}: '{Value}' is not in the dictionary - set to '{Unbranded}'.",
                            product.Code, badValue, ParameterFallbacks.UnbrandedValue);

                    return changed;
                }

                await _parameterRepo.DeleteParameter(parameterName, product.Id, CancellationToken.None);
                _logger.LogInformation("Parameter '{Parameter}' removed for {Code}: value '{Value}' is not in the category dictionary.",
                    parameterName, product.Code, badValue);
                return true;
            }

            if (code == "ParameterNameNotFoundException" && !string.IsNullOrEmpty(userMessage))
            {
                var parameterName = ExtractParameterNameFromNotFoundMessage(userMessage);

                if (!string.IsNullOrEmpty(parameterName))
                {
                    await _parameterRepo.DeleteParameter(parameterName, product.Id, CancellationToken.None);
                    _logger.LogDebug("Parameter '{ParameterName}' removed for {Code}: not available in category.", parameterName, product.Code);
                    return true;
                }

                return false;
            }

            if (userMessage.Contains("Podany adres obrazka jest nieprawidłowy."))
            {
                await _imageRepo.DeleteProductImagesAsync(product.Id, CancellationToken.None);
                _logger.LogWarning("Images reset for {Code}: Allegro rejected the image address.", product.Code);
                return true;
            }

            if (message.Contains(@"The type of this ""Compatible with"" "))
            {
                await _productRepo.UpdateCompatibilitySet(product.Id, false, CancellationToken.None);
                return true;
            }

            if (code == "OfferNotFoundException" && response.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(offerId))
            {
                await _offerRepo.DeleteOffer(offerId, CancellationToken.None);
                _logger.LogWarning("Offer {OfferId} not found in Allegro ({Code}). Removed from database.", offerId, product.Code);
                return true;
            }

            // Produkt z katalogu Allegro zniknal (scalony albo usuniety). Czyscimy jego id,
            // zeby kolejna proba nie poszla z martwym id.
            //
            // Samo czyszczenie nie wystarcza: Allegro zglasza ten blad takze wtedy, gdy my zadnego
            // id nie wyslalismy, bo sama oferta jest u nich nadal podpieta pod usuniety produkt.
            // Dlatego po wyczyszczeniu szukamy zastepnika w katalogu - inaczej ta sama oferta
            // wracalaby z tym samym bledem w kazdym cyklu.
            if (code == "ProductNotFoundException")
            {
                foreach (var deadId in new[] { ExtractMissingProductId(message) ?? ExtractMissingProductId(userMessage), product.AllegroId })
                {
                    if (!string.IsNullOrWhiteSpace(deadId))
                        DeadCatalogProductIds.TryAdd(deadId, 0);
                }

                if (!string.IsNullOrWhiteSpace(product.AllegroId))
                {
                    await _productRepo.UpdateProductAllegroId(product.Id, null, product.DefaultAllegroCategory.ToString(CultureInfo.InvariantCulture), null, CancellationToken.None);
                    product.AllegroId = null;

                    _logger.LogWarning("Allegro product id cleared for {Code}: the product no longer exists in the catalog.", product.Code);
                }

                if (!string.IsNullOrEmpty(offerId))
                    await _offerRepo.UpdateProductId(offerId, null, CancellationToken.None);

                if (await RelinkCatalogProduct(product, offerId))
                    return true;

                // Oferta podpięta po stronie Allegro pod usunięty produkt nie przyjmuje żadnego patcha,
                // więc nie dostaje ani stanu, ani ceny. Kończymy ją, zamiast sprzedawać ze starym stanem -
                // do ponownego wystawienia trzeba ją wystawić od nowa.
                if (!string.IsNullOrEmpty(offerId))
                {
                    _offersToEndAfterUpdate.TryAdd(offerId, 0);
                    UnpatchableOffers.TryAdd(offerId, 0);
                    _logger.LogWarning("Offer {OfferId} ({Code}) is bound to a deleted Allegro product and has no replacement - ending it; it needs to be listed again.",
                        offerId, product.Code);
                }

                return false;
            }

            // Oferta nie jest podpieta pod produkt z katalogu. Szukamy produktu po EAN,
            // kodzie i nazwie, zapisujemy jego id i kategorie - patch wyjdzie juz z productSet.
            if (code == "OfferWithoutProductException" || code == "ProductNotFoundExceptionForOffer")
                return await RelinkCatalogProduct(product, offerId);

            // Oferta bez galerii: u dostawcy bez własnych zdjęć pokazuje ją produkt z katalogu,
            // a ten żadnych nie ma. Odpinamy go - wyszukiwanie pomija kandydatów bez zdjęć,
            // więc albo znajdzie się produkt z galerią, albo oferta zaczeka, zamiast wracać
            // z tym samym błędem w każdym cyklu.
            if (code == "ConstraintViolationException.GallerySize" && !_offerFactory.UsesOwnImages)
            {
                if (!string.IsNullOrWhiteSpace(product.AllegroId))
                {
                    await _productRepo.UpdateProductAllegroId(product.Id, null, product.DefaultAllegroCategory.ToString(CultureInfo.InvariantCulture), null, CancellationToken.None);
                    product.AllegroId = null;
                }

                if (!string.IsNullOrEmpty(offerId))
                    await _offerRepo.UpdateProductId(offerId, null, CancellationToken.None);

                _logger.LogWarning(
                    "Allegro catalog product for {Code} has no images - unlinked, the offer waits for one with a gallery.",
                    product.Code);

                return false;
            }

            if (code == "MultipleProductsFoundException" && !string.IsNullOrEmpty(offerId))
            {
                await _offerRepo.UpdateProductId(offerId, null, CancellationToken.None);
                _logger.LogDebug("Product id cleared for offer {OfferId} ({Code}): multiple products matched.", offerId, product.Code);
                return true;
            }

            if (userMessage.Contains("bez wybierania wartości niejednoznacznej"))
            {
                // Nie da sie tego naprawic automatycznie - zwracamy false, zeby nie zglaszac
                // ponownej proby jako "dane poprawione" i nie krecic sie w kolko.
                _logger.LogWarning("Ambiguous parameter value for {Code} requires manual selection in Allegro.", product.Code);
                return false;
            }

            return false;
        }

        /// <summary>
        /// Błąd, który przy parametrach branych z katalogu oznacza wadę samego produktu z katalogu:
        /// brak obowiązkowego parametru albo wartość parametru produktu sprzeczna z ofertą.
        /// Własnych parametrów produktu nie wysyłamy, więc naprawić to może tylko inny produkt.
        /// </summary>
        internal static bool IsCatalogProductDefect(string code, string userMessage, string message) =>
            code.EndsWith("MissingRequiredParameters", StringComparison.OrdinalIgnoreCase)
            || (code == "ProductConstraintViolationException.DataIntegrity"
                && (userMessage.Contains("Niepoprawna wartość parametru", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Incorrect value of the", StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Podmienia produkt z katalogu, któremu brakuje parametrów obowiązkowych (albo który jest
        /// sprzeczny z ofertą), na inny z kompletem. Gdy takiego nie ma: odpina produkt (kolejny cykl
        /// szuka od nowa), a istniejącą ofertę kończy - bez kompletu parametrów i tak nie przejdzie.
        /// </summary>
        private async Task ReplaceCatalogProduct(RolmarProduct product, AllegroError err, string userMessage, string message, string? offerId, int offerCategoryId)
        {
            var currentId = product.AllegroId;
            var reason = string.IsNullOrWhiteSpace(userMessage) ? message : userMessage;

            // Produkt sprzeczny z ofertą (inny EAN) to inny towar - nie wraca. Niekompletny może zostać
            // uzupełniony w katalogu, więc tylko pomijamy go w tym wyszukiwaniu.
            var reject = !(err.Code ?? string.Empty).EndsWith("MissingRequiredParameters", StringComparison.OrdinalIgnoreCase);

            var missingNames = ExtractMissingParameterNames(reason);

            if (!reject && missingNames.Count > 0)
            {
                // O tym, co jest parametrem oferty, decyduje kategoria, w której oferta stoi.
                var offerOnly = await OfferOnlyParameters(offerCategoryId > 0 ? offerCategoryId : product.DefaultAllegroCategory, missingNames);

                // Brakuje wyłącznie parametrów oferty (np. "Wyrób medyczny") - uzupełnia je oferta, nie produkt
                // z katalogu, więc podmiana nic nie da. Wcześniej taka oferta odpinała i podpinała produkt co cykl.
                if (offerOnly.Count == missingNames.Count)
                {
                    _logger.LogWarning(
                        "Offer for {Code} (offer {OfferId}) lacks required offer parameters that are not computed from supplier data: {Parameters}. " +
                        "The Allegro product stays linked.",
                        product.Code, offerId ?? "-", string.Join(", ", offerOnly));
                    return;
                }

                missingNames = missingNames.Except(offerOnly, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var excluded = string.IsNullOrWhiteSpace(currentId) ? Array.Empty<string>() : new[] { currentId };
            var found = await _allegroProductService.FindCatalogProduct(product, CancellationToken.None, excluded, missingNames);

            if (found.ProductId != null && found.CategoryId != null
                && !string.Equals(found.ProductId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                if (reject)
                    await _productRepo.RejectAllegroProduct(product.Id, offerId, CancellationToken.None);

                await _productRepo.UpdateProductAllegroId(product.Id, found.ProductId, found.CategoryId, found.Name, CancellationToken.None);
                product.AllegroId = found.ProductId;

                if (!string.IsNullOrEmpty(offerId))
                    await _offerRepo.UpdateProductId(offerId, found.ProductId, CancellationToken.None);

                _logger.LogWarning(
                    "Allegro product {OldId} for {Code} (offer {OfferId}) does not fit: {Reason} Replaced with {NewId} in category {CategoryId}.",
                    currentId ?? "-", product.Code, offerId ?? "-", Shorten(reason), found.ProductId, found.CategoryId);
                return;
            }

            await _productRepo.UnlinkAllegroProduct(product.Id, offerId, reject, CancellationToken.None);
            product.AllegroId = null;

            if (!string.IsNullOrEmpty(offerId))
                _offersToEndAfterUpdate.TryAdd(offerId, 0);

            _logger.LogWarning(
                "Allegro product {OldId} for {Code} (offer {OfferId}) does not fit: {Reason} No complete replacement found - " +
                "product unlinked{Ending}, the search retries next cycle.",
                currentId ?? "-", product.Code, offerId ?? "-", Shorten(reason), string.IsNullOrEmpty(offerId) ? string.Empty : " and offer ended");
        }

        /// <summary>
        /// Oferta stoi na produkcie z katalogu, który odrzuciliśmy, a produkt ma już inne powiązanie (np. podmienione
        /// na jednym koncie). Przepinamy ofertę, żeby oba konta miały ten sam productId.
        /// Listę odrzuconych czytamy tylko przy rozjeździe id - zwykle oferta i produkt mają to samo.
        /// </summary>
        private async Task AlignOfferToProductLink(AllegroOffer offer, RolmarProduct product, CancellationToken ct)
        {
            if (!LinksDiffer(offer.ProductId, product.AllegroId))
                return;

            var rejected = await _productRepo.GetRejectedAllegroIds(product.Id, ct);

            if (!rejected.Contains(offer.ProductId!))
                return;

            _logger.LogInformation("Offer {OfferId} ({Code}) moves from rejected Allegro product {OldId} to {NewId} linked to the product.",
                offer.Id, product.Code, offer.ProductId, product.AllegroId);

            offer.ProductId = product.AllegroId;
        }


        // ---------------------------------------------------------------- zablokowane zdjęcia

        /// <summary>Adresy zdjęć zablokowanych przez Allegro - wspólne dla kont (identyczny plik = ten sam adres).</summary>
        private readonly ConcurrentDictionary<string, byte> _blockedImages = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Zdjęcia produktów z katalogu, w których szukaliśmy zablokowanego zdjęcia (id produktu -> adresy).</summary>
        private readonly ConcurrentDictionary<string, List<string>> _catalogImages = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ile próbnych patchy (szukanie zablokowanego zdjęcia po jednym) wolno wysłać w cyklu. Każdy nowo wykryty
        /// adres naprawia od razu wszystkie oferty z tym zdjęciem, więc po kilku cyklach prób prawie nie ma.
        /// </summary>
        private const int MaxBlockedImageProbesPerCycle = 3000;

        /// <summary>Ile zdjęć jednej oferty sprawdzamy po kolei - galeria ma do 16 własnych i kilka z katalogu.</summary>
        private const int MaxProbesPerOffer = 24;

        private int _blockedImageProbes;

        private IReadOnlySet<string> BlockedImagesSnapshot() => new HashSet<string>(_blockedImages.Keys, StringComparer.OrdinalIgnoreCase);

        private async Task LoadBlockedImages(CancellationToken ct)
        {
            try
            {
                foreach (var url in await _imageRepo.GetBlockedImageUrlsAsync(ct))
                    _blockedImages.TryAdd(url, 0);

                foreach (var (productId, images) in await _imageRepo.GetCatalogProductImagesAsync(ct))
                    _catalogImages[productId] = images;

                _logger.LogInformation("Blocked images known: {Blocked}, catalog products with checked images: {Catalog}.",
                    _blockedImages.Count, _catalogImages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Loading blocked images failed - offers are updated without the filter.");
            }
        }

        /// <summary>
        /// Zdjęcia produktu z katalogu (z pamięci albo z API). Zapamiętujemy je tylko wtedy, gdy zawierają zablokowany
        /// adres albo właśnie go szukamy - patch podaje wtedy listę zdjęć produktu bez zablokowanych.
        /// </summary>
        private async Task<List<string>?> CatalogImages(string catalogProductId, CancellationToken ct)
        {
            if (_catalogImages.TryGetValue(catalogProductId, out var cached))
                return cached;

            try
            {
                var catalogProduct = await _apiClient.GetAsync<SearchProdustsResponse.Product>($"/sale/products/{catalogProductId}", ct);
                var images = catalogProduct?.Images.Select(i => i.Url).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).ToList();

                if (images == null || images.Count == 0)
                    return null;

                _catalogImages[catalogProductId] = images;
                await _imageRepo.SaveCatalogProductImagesAsync(catalogProductId, images, ct);
                return images;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reading images of Allegro product {ProductId} failed.", catalogProductId);
                return null;
            }
        }

        private async Task MarkImageBlocked(string url, AllegroOffer offer, string reason)
        {
            if (!_blockedImages.TryAdd(url, 0))
                return;

            try
            {
                await _imageRepo.AddBlockedImageUrlAsync(url, reason, offer.Id, offer.Product?.Code, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Saving blocked image {Url} failed.", url);
            }

            _logger.LogWarning("Image {Url} is blocked by Allegro (found on offer {OfferId}, {Code}) - removed from all offers from now on.",
                url, offer.Id, offer.Product?.Code ?? "-");
        }

        /// <summary>
        /// Naprawia patch odrzucony za zablokowane zdjęcie, usuwając jak najmniej zdjęć:
        /// 1) dociąga zdjęcia produktu z katalogu i wycina znane zablokowane adresy (także z produktu - przez
        ///    productSet z listą zdjęć), 2) gdy to nie wystarcza, usuwa po jednym kolejne zdjęcia i zapamiętuje to,
        ///    którego usunięcie zdjęło blokadę. Zwraca ostatnią odpowiedź, która nie jest już blokadą zdjęcia
        ///    (sukces albo inny błąd), albo null, gdy blokada została.
        /// </summary>
        private async Task<(ProductOfferRequest Request, HttpResponseMessage Response, string Body)?> FixBlockedImages(
            AllegroOffer offer, RolmarProduct product, Func<bool, ProductOfferRequest> buildPatch, CancellationToken ct)
        {
            async Task<(ProductOfferRequest Request, HttpResponseMessage Response, string Body)> Send(ProductOfferRequest request)
            {
                var response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", HttpMethod.Patch, request, ct);
                return (request, response, await response.Content.ReadAsStringAsync(ct));
            }

            bool StillBlocked((ProductOfferRequest Request, HttpResponseMessage Response, string Body) r) =>
                IsContentRejection(r.Response.StatusCode) && IsBlockedImage(r.Body);

            // 1) Zdjęcia produktu z katalogu - jeśli zawierają znany zablokowany adres, patch poda je bez niego.
            var catalogId = buildPatch(false).ProductSet?.Select(s => s.ProductObject?.Id).FirstOrDefault(IsCatalogId);
            var hadCatalogImages = catalogId != null && _catalogImages.ContainsKey(catalogId);

            if (catalogId != null)
                await CatalogImages(catalogId, ct);

            var blocked = BlockedImagesSnapshot();
            var catalogNowFiltered = catalogId != null && !hadCatalogImages
                && _catalogImages.TryGetValue(catalogId, out var catalogList) && catalogList.Any(blocked.Contains);

            if (catalogNowFiltered)
            {
                var attempt = await Send(buildPatch(false));

                if (!StillBlocked(attempt))
                {
                    _logger.LogInformation("Offer {OfferId} ({Code}): blocked image removed from catalog product {ProductId} images in the patch - {Result}.",
                        offer.Id, product.Code, catalogId, attempt.Response.IsSuccessStatusCode ? "updated" : "other error follows");
                    return attempt;
                }
            }

            // 2) Usuwamy kolejne zdjęcia narastająco, aż blokada zniknie, a potem przywracamy po jednym wszystkie
            //    poza ostatnim: zdjęcie, którego powrót znów blokuje, jest zablokowane. Ostatnie usunięte też (z nim
            //    było zablokowane, bez niego - nie). Zostają usunięte tylko zablokowane, nawet gdy jest ich kilka.
            var candidates = BlockedImageFilter.Candidates(buildPatch(false), _catalogImages, blocked).Take(MaxProbesPerOffer).ToList();

            ProductOfferRequest Without(IEnumerable<string> removed)
            {
                var probe = buildPatch(false);
                var set = new HashSet<string>(BlockedImagesSnapshot(), StringComparer.OrdinalIgnoreCase);
                set.UnionWith(removed);
                BlockedImageFilter.Apply(probe, set, _catalogImages);
                return probe;
            }

            bool OverBudget()
            {
                if (Interlocked.Increment(ref _blockedImageProbes) <= MaxBlockedImageProbesPerCycle)
                    return false;

                _logger.LogDebug("Blocked image probe limit reached - offer {OfferId} ({Code}) waits for the next cycle.", offer.Id, product.Code);
                return true;
            }

            var removedSoFar = new List<string>();

            foreach (var url in candidates)
            {
                if (OverBudget())
                    return null;

                removedSoFar.Add(url);
                var attempt = await Send(Without(removedSoFar));

                if (StillBlocked(attempt))
                    continue;

                // Blokada zniknęła. Wynik, który zwracamy, to ostatnia wysłana wersja, która nie była blokowana -
                // odrzucone próby niczego w ofercie nie zmieniają.
                var lastUnblocked = attempt;
                var found = new List<string> { url };
                var stillRemoved = new HashSet<string>(removedSoFar, StringComparer.OrdinalIgnoreCase);

                foreach (var earlier in removedSoFar.Where(r => !string.Equals(r, url, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    if (OverBudget())
                        break;

                    stillRemoved.Remove(earlier);
                    var restored = await Send(Without(stillRemoved));

                    if (StillBlocked(restored))
                    {
                        stillRemoved.Add(earlier);
                        found.Add(earlier);
                    }
                    else
                    {
                        lastUnblocked = restored;
                    }
                }

                foreach (var blockedUrl in found)
                    await MarkImageBlocked(blockedUrl, offer, "Wykryte: patch bez tego zdjęcia przestał być blokowany");

                _logger.LogInformation("Offer {OfferId} ({Code}): {Count} blocked image(s) removed ({Urls}) - {Result}.",
                    offer.Id, product.Code, found.Count, string.Join(" ", found),
                    lastUnblocked.Response.IsSuccessStatusCode ? "updated" : "other error follows");

                return lastUnblocked;
            }

            // 3) Usuwanie narastające zależy od kolejności (np. gdy zablokowana jest cała własna galeria, a czyste są tylko
            //    zdjęcia produktu). Sprawdzamy każde zdjęcie osobno: patch z samym tym zdjęciem przechodzi tylko wtedy,
            //    gdy ono nie jest zablokowane. Na koniec patch bez wszystkich zablokowanych.
            var classifiedBlocked = new List<string>();
            var anyClean = false;

            foreach (var url in candidates)
            {
                if (OverBudget())
                    return null;

                var onlyThis = await Send(Without(candidates.Where(c => !string.Equals(c, url, StringComparison.OrdinalIgnoreCase))));

                if (StillBlocked(onlyThis))
                    classifiedBlocked.Add(url);
                else
                    anyClean = true;
            }

            if (anyClean && classifiedBlocked.Count > 0 && !OverBudget())
            {
                var final = await Send(Without(classifiedBlocked));

                if (!StillBlocked(final))
                {
                    foreach (var blockedUrl in classifiedBlocked)
                        await MarkImageBlocked(blockedUrl, offer, "Wykryte: patch z samym tym zdjęciem jest blokowany");

                    _logger.LogInformation("Offer {OfferId} ({Code}): {Count} blocked image(s) found one by one and removed ({Urls}) - {Result}.",
                        offer.Id, product.Code, classifiedBlocked.Count, string.Join(" ", classifiedBlocked),
                        final.Response.IsSuccessStatusCode ? "updated" : "other error follows");
                    return final;
                }
            }

            _logger.LogWarning("Offer {OfferId} ({Code}): removing images does not lift the block ({Count} checked).", offer.Id, product.Code, candidates.Count);
            return null;
        }

        /// <summary>Prawdziwe id produktu z katalogu - nie puste i nie tekst "NULL", który zdarza się w bazie.</summary>
        internal static bool IsCatalogId(string? id) =>
            !string.IsNullOrWhiteSpace(id) && !string.Equals(id.Trim(), "NULL", StringComparison.OrdinalIgnoreCase);

        /// <summary>Oferta i produkt mają różne, niepuste id produktu z katalogu.</summary>
        internal static bool LinksDiffer(string? offerProductId, string? productAllegroId) =>
            IsCatalogId(offerProductId)
            && IsCatalogId(productAllegroId)
            && !string.Equals(offerProductId!.Trim(), productAllegroId!.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Nazwy parametrów z "Uzupełnij parametry obowiązkowe: A, B." / "Fill in the required parameters: A, B".</summary>
        internal static List<string> ExtractMissingParameterNames(string message)
        {
            var match = Regex.Match(message ?? string.Empty, @"(?:obowiązkowe|required parameters)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!match.Success)
                return [];

            return match.Groups[1].Value
                .TrimEnd('.', ' ')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Te z brakujących parametrów, które w kategorii są parametrami oferty (nie opisują produktu) -
        /// podaje je sprzedający, a nie produkt z katalogu. Bez listy parametrów kategorii: żaden.
        /// </summary>
        private async Task<List<string>> OfferOnlyParameters(int categoryId, IReadOnlyCollection<string> names)
        {
            if (categoryId <= 0)
                return [];

            var parameters = await _allegroProductService.GetCategoryParameters(categoryId.ToString(CultureInfo.InvariantCulture), CancellationToken.None);

            if (parameters == null)
                return [];

            return names
                .Where(name => parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                                                   && p.Options?.DescribesProduct != true
                                                   && !p.RequiredForProduct))
                .ToList();
        }

        /// <summary>
        /// Podpina ofertę pod produkt z katalogu Allegro: szuka go po EAN, kodzie i nazwie,
        /// a znalezione id zapisuje przy produkcie i przy ofercie - dzięki temu kolejna próba
        /// wychodzi już z poprawnym productSet. Zwraca false, gdy w katalogu nic nie pasuje.
        /// </summary>
        private async Task<bool> RelinkCatalogProduct(RolmarProduct product, string? offerId)
        {
            var found = await _allegroProductService.FindCatalogProduct(product, CancellationToken.None);

            if (found.ProductId == null || found.CategoryId == null)
            {
                _logger.LogWarning("No catalog product found for {Code} (EAN {Ean}) - the offer stays unlinked.", product.Code, product.Ean ?? "-");
                return false;
            }

            await _productRepo.UpdateProductAllegroId(product.Id, found.ProductId, found.CategoryId, found.Name, CancellationToken.None);
            product.AllegroId = found.ProductId;
            product.AllegroName = found.Name ?? product.AllegroName;

            if (!string.IsNullOrEmpty(offerId))
                await _offerRepo.UpdateProductId(offerId, found.ProductId, CancellationToken.None);

            _logger.LogInformation("Offer {OfferId} ({Code}) linked to Allegro product {ProductId} in category {CategoryId}.",
                offerId ?? "-", product.Code, found.ProductId, found.CategoryId);

            return true;
        }

        /// <summary>Limit galerii Allegro, gdy komunikat go nie podaje.</summary>
        private const int AllegroGalleryLimit = 16;

        /// <summary>
        /// Ile własnych zdjęć zmieści się w galerii oferty danego produktu, ustalone po odrzuceniu
        /// "You can add up to 16 images". Bez tego każdy cykl zaczynałby od odrzuconego patcha.
        /// Żyje tyle, co proces - po restarcie limit ustala się ponownie przy pierwszym odrzuceniu.
        /// </summary>
        private static readonly ConcurrentDictionary<string, int> GalleryLimits = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Klucz limitu galerii - kody produktów różnych dostawców potrafią się powtarzać.</summary>
        private string GalleryKey(RolmarProduct product) => $"{_service.Company}:{product.Code}";

        /// <summary>Przycina własne zdjęcia produktu do zapamiętanego limitu galerii.</summary>
        private void ApplyGalleryLimit(RolmarProduct product)
        {
            if (!_offerFactory.UsesOwnImages || !GalleryLimits.TryGetValue(GalleryKey(product), out var limit))
                return;

            if (product.AllegroImages.Count > limit)
                product.AllegroImages = product.AllegroImages.Take(limit).ToList();
        }

        /// <summary>
        /// Odrzucenie za za dużą galerię ("GallerySizeException - You can add up to 16 images"). Zwraca limit
        /// z komunikatu albo <c>null</c>, gdy to inny błąd - w tym "przynajmniej 1 zdjęcie", czyli galeria pusta.
        /// </summary>
        internal static int? GalleryTooLargeLimit(string body)
        {
            if (string.IsNullOrEmpty(body) || !body.Contains("GallerySizeException", StringComparison.OrdinalIgnoreCase))
                return null;

            var match = Regex.Match(body, @"(?:up to|maksymalnie|do)\s+(\d+)\s+(?:images|zdję)", RegexOptions.IgnoreCase);

            if (match.Success)
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

            return body.Contains("przynajmniej", StringComparison.OrdinalIgnoreCase) || body.Contains("at least", StringComparison.OrdinalIgnoreCase)
                ? null
                : AllegroGalleryLimit;
        }

        /// <summary>
        /// Ile własnych zdjęć wysłać, skoro Allegro odrzuciło galerię: limit minus zdjęcia produktu z katalogu,
        /// które Allegro dolicza do galerii oferty. Gdy katalogowych nie ma albo nie wiadomo ile, zdejmujemy
        /// po jednym - kolejne odrzucenie zmniejszy limit dalej. Zawsze zostaje co najmniej jedno zdjęcie.
        /// </summary>
        internal static int ReducedGallerySize(int ownImages, int catalogImages, int limit)
        {
            var size = limit - Math.Max(catalogImages, 0);

            if (size >= ownImages)
                size = ownImages - 1;

            return Math.Max(size, 1);
        }

        /// <summary>
        /// Po odrzuceniu za za dużą galerię przycina własne zdjęcia produktu i zapamiętuje limit.
        /// Zwraca true, gdy jest sens ponowić zapytanie z mniejszą galerią.
        /// </summary>
        private async Task<bool> ShrinkGalleryAfterRejection(RolmarProduct product, string? catalogProductId, HttpResponseMessage response, string body, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode || !_offerFactory.UsesOwnImages)
                return false;

            var limit = GalleryTooLargeLimit(body);
            var ownImages = product.AllegroImages.Select(i => i.Url).Distinct().Count();

            if (limit == null || ownImages <= 1)
                return false;

            var catalogImages = 0;

            if (!string.IsNullOrWhiteSpace(catalogProductId))
            {
                try
                {
                    var catalogProduct = await _apiClient.GetAsync<SearchProdustsResponse.Product>($"/sale/products/{catalogProductId}", ct);
                    catalogImages = catalogProduct?.Images.Count ?? 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Reading images of Allegro product {ProductId} failed.", catalogProductId);
                }
            }

            var size = ReducedGallerySize(ownImages, catalogImages, limit.Value);

            if (size >= ownImages)
                return false;

            GalleryLimits[GalleryKey(product)] = size;
            product.AllegroImages = product.AllegroImages
                .GroupBy(i => i.Url)
                .Select(g => g.First())
                .Take(size)
                .ToList();

            _logger.LogWarning(
                "Gallery of {Code} is over the Allegro limit of {Limit} ({Own} own images, {Catalog} from the catalog product) - sending {Size} own images.",
                product.Code, limit, ownImages, catalogImages, size);

            return true;
        }

        /// <summary>"Przedmiot, który chcesz wystawić znajduje się już na liście Twoich aktywnych ofert..."</summary>
        internal static bool IsDuplicateActiveOffer(string userMessage, string message) =>
            userMessage.Contains("już na liście Twoich aktywnych ofert", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already on the list of your active offers", StringComparison.OrdinalIgnoreCase);

        /// <summary>Kod i komunikat pierwszego błędu z odpowiedzi Allegro - do jednej linii w logu.</summary>
        internal static string RejectionReason(string body)
        {
            try
            {
                var error = JsonSerializer.Deserialize<AllegroErrorResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Errors?.FirstOrDefault();

                if (error != null)
                    return $"{error.Code}: {(string.IsNullOrWhiteSpace(error.UserMessage) ? error.Message : error.UserMessage)}";
            }
            catch (JsonException)
            {
            }

            return Shorten(body);
        }

        /// <summary>Adresy zdjęć z patcha (galeria, opis, propozycja produktu) - do logu przy zablokowanym zdjęciu.</summary>
        internal static string SentImages(ProductOfferRequest request)
        {
            var urls = (request.Images ?? new List<string>())
                .Concat(request.Description?.Sections.SelectMany(s => s.SectionItems).Select(i => i.Url) ?? Enumerable.Empty<string?>())
                .Concat(request.ProductSet?.SelectMany(p => p.ProductObject?.Images ?? new List<string>()) ?? Enumerable.Empty<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            return urls.Count == 0 ? "-" : string.Join(" ", urls);
        }

        /// <summary>"Zdjęcie zostało zablokowane" - Allegro odrzuca zdjęcie z galerii (np. ze znakiem wodnym).</summary>
        internal static bool IsBlockedImage(string body) =>
            body.Contains("Zdjęcie zostało zablokowane", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Zdj\\u0119cie zosta\\u0142o zablokowane", StringComparison.OrdinalIgnoreCase)
            || body.Contains("image has been blocked", StringComparison.OrdinalIgnoreCase);

        private static bool IsContentRejection(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity;

        private bool HasDefaultManufacturer => !string.IsNullOrWhiteSpace(_allegroSettings.DefaultPartsManufacturer);

        // ---------------------------------------------------------------- zdjecia

        /// <summary>
        /// Zdjęcia oferty. Adresy raz wysłane do Allegro zapisujemy przy produkcie i używamy
        /// ponownie - dostawcy czyszczą te wpisy dopiero wtedy, gdy zestaw plików na dysku się
        /// zmieni, więc ich obecność znaczy "te same zdjęcia, już wgrane". Dzięki temu nie
        /// wysyłamy co cykl tych samych plików od nowa.
        ///
        /// Oferta bez zdjęcia nie przechodzi walidacji Allegro (błąd GallerySize), więc pusta
        /// lista oznacza "nie wystawiaj w tym cyklu".
        /// </summary>
        private async Task<List<AllegroImages>> ImportImages(RolmarProduct product, CancellationToken ct)
        {
            var alreadyUploaded = await _imageRepo.GetProductImagesAsync(product.Id, ct);

            if (alreadyUploaded.Count > 0)
            {
                _logger.LogDebug("Reusing {Count} images already uploaded for {Code}.", alreadyUploaded.Count, product.Code);
                return alreadyUploaded;
            }

            if (!Directory.Exists(_service.ImagesFolder))
            {
                _logger.LogWarning("Images folder not found: {Path}", _service.ImagesFolder);
                return new List<AllegroImages>();
            }

            var imageFiles = ImageHelper.GetImageFiles(_service.ImagesFolder, product.Id);

            if (imageFiles.Count == 0)
                return new List<AllegroImages>();

            var uploaded = new ConcurrentBag<(string FileName, string Url)>();

            await Parallel.ForEachAsync(imageFiles, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (filePath, token) =>
            {
                try
                {
                    var imageBytes = await File.ReadAllBytesAsync(filePath, token);
                    var validatedBytes = Utils.EnsureImageMinSize(imageBytes);

                    if (validatedBytes == null)
                        return;

                    var contentType = Utils.GetContentTypeFromPath(filePath);
                    var uploadResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", validatedBytes, token, contentType);

                    if (!string.IsNullOrWhiteSpace(uploadResult?.Location))
                        uploaded.Add((Path.GetFileName(filePath), uploadResult.Location));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Uploading image {File} failed for {Code}.", Path.GetFileName(filePath), product.Code);
                }
            });

            var orderedUrls = uploaded
                .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Url)
                .ToList();

            if (orderedUrls.Count == 0)
                return new List<AllegroImages>();

            var result = new List<AllegroImages>(orderedUrls.Count);

            foreach (var url in orderedUrls)
            {
                var imageId = await _imageRepo.AddImageAsync(product.Id, url, ct);

                result.Add(new AllegroImages
                {
                    Id = imageId,
                    ProductId = product.Id,
                    Url = url,
                    Connected = false
                });
            }

            return result;
        }

        // ---------------------------------------------------------------- pomocnicze

        private ParallelOptions ParallelOptions(CancellationToken ct) => new()
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = MaxParallelism
        };

        private static string Shorten(string body) => Utils.Shorten(body);

        private static string Format(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}s";

        private static string? ExtractCorrectCategoryId(string message)
        {
            var correctMatch = Regex.Match(message, @"produktu\s*\((\d+)\)", RegexOptions.IgnoreCase);

            if (correctMatch.Success)
                return correctMatch.Groups[1].Value;

            var allMatches = Regex.Matches(message, @"\((\d+)\)");

            if (allMatches.Count > 1)
                return allMatches[^1].Groups[1].Value;

            return allMatches.Count == 1 ? allMatches[0].Groups[1].Value : null;
        }

        /// <summary>
        /// Wartość, jakiej oczekuje Allegro - z wersji angielskiej ("the correct value is `X`")
        /// i polskiej ("Prawidłowa wartość parametru dla produktu to: "X"").
        /// </summary>
        private static string? ExtractCorrectParameterValue(string message)
        {
            var match = Regex.Match(message, @"(?:value|wartość)[^`""]{0,80}?(?:\bto\b|\bis\b)\s*:?\s*[`""]([^`""]+)[`""]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        /// <summary>
        /// Wyciaga nazwe parametru z komunikatu PARAMETER_MISMATCH, np.
        /// "the value of the parameter `Producent części` in the offer is ...".
        /// </summary>
        private static string? ExtractParameterNameFromMismatchMessage(string message)
        {
            var match = Regex.Match(message, @"parametru?\s*[`""]([^`""]+)[`""]", RegexOptions.IgnoreCase);

            if (!match.Success)
                match = Regex.Match(message, @"parameter\s*[`""]([^`""]+)[`""]", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string? ExtractParameterIdFromConstraintMessage(string message)
        {
            var match = Regex.Match(message, @"id:\s*(\d+)", RegexOptions.IgnoreCase);

            if (match.Success)
                return match.Groups[1].Value;

            // Polska wersja podaje id w nawiasie: "parametru EAN/ISBN/ISSN (225693) dla produktu w ofercie".
            match = Regex.Match(message, @"parametru\s+[^()]{1,80}?\((\d+)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Wartość i nazwa parametru z "An incorrect value `X` has been entered for parameter `Y`".</summary>
        internal static (string? Value, string? Parameter) ExtractDictionaryValueError(string message)
        {
            var match = Regex.Match(message ?? string.Empty, @"value\s*`([^`]*)`.*?parameter\s*`([^`]+)`", RegexOptions.IgnoreCase);
            return match.Success ? (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()) : (null, null);
        }

        /// <summary>Id produktu z "Product with ID: X not found".</summary>
        internal static string? ExtractMissingProductId(string message)
        {
            var match = Regex.Match(message ?? string.Empty, @"Product with ID:\s*([0-9a-fA-F-]{8,})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? ExtractParameterNameFromNotFoundMessage(string message)
        {
            const string prefix = "Parameter ";
            const string suffix = " not found";

            var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            var end = message.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);

            if (start < 0 || end <= start)
                return null;

            start += prefix.Length;
            return message.Substring(start, end - start).Trim();
        }
    }
}
