using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Mappers;
using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Repositories;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Allegro.JSAGRO.InPost.ProductsService.Services
{
    public class InPostService
    {
        public const AllegroAccount Account = AllegroAccount.JSAGRO;

        /// <summary>Ile pozycji wysyłamy w jednym zbiorczym PATCH ceny / stanu.</summary>
        private const int BatchSize = 50;

        private readonly ILogger<InPostService> _logger;
        private readonly AppSettings _appSettings;
        private readonly InPostPriceSettings _priceSettings;
        private readonly InPostOfferSettings _offerSettings;
        private readonly InPostApiCredentials _credentials;
        private readonly InPostOfferRepository _repository;
        private readonly InPostClient _client;
        private readonly InPostCatalog _catalog;
        private readonly IAllegroResponsibleProducerRepository _producerRepository;
        private readonly IAllegroResponsiblePersonRepository _personRepository;

        public InPostService(
            ILogger<InPostService> logger,
            IOptions<AppSettings> appSettings,
            IOptions<InPostPriceSettings> priceSettings,
            IOptions<InPostOfferSettings> offerSettings,
            IOptions<InPostApiCredentials> credentials,
            InPostOfferRepository repository,
            InPostClient client,
            InPostCatalog catalog,
            IAllegroResponsibleProducerRepository producerRepository,
            IAllegroResponsiblePersonRepository personRepository)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            _priceSettings = priceSettings.Value;
            _offerSettings = offerSettings.Value;
            _credentials = credentials.Value;
            _repository = repository;
            _client = client;
            _catalog = catalog;
            _producerRepository = producerRepository;
            _personRepository = personRepository;
        }

        // ------------------------------------------------------------------ budowanie ofert

        /// <summary>
        /// Buduje oferty InPost dla wszystkich kandydatów i loguje, ile jest gotowych oraz dlaczego
        /// reszta odpadła. Nic nie wysyła.
        /// </summary>
        public async Task<List<OfferBuildResult>> BuildOffersAsync(CancellationToken ct)
        {
            if (_appSettings.DeliveryPriceLists.Count == 0)
                throw new InvalidOperationException("Brak 'AppSettings:DeliveryPriceLists' - nie wiadomo, które oferty wysłać.");

            await _catalog.LoadAsync(ct);
            ValidateCategoryMap();

            var mapper = new CategoryMapper(await _repository.GetAllegroCategoryNodesAsync(ct), _offerSettings.CategoryMap, _catalog.Roots, _offerSettings.CategoryPathAliases);
            var builder = new InPostOfferBuilder(
                _appSettings,
                _priceSettings,
                _offerSettings,
                await _producerRepository.GetAllegroResponsibleProducers(ct),
                await _personRepository.GetAllegroResponsiblePersons(ct));

            var testIds = _appSettings.TestOfferIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();

            var candidates = await _repository.GetCandidatesAsync(
                Account, _appSettings.DeliveryPriceLists, testIds, ct);

            _logger.LogInformation("InPost candidates: {Count} active offers from {Lists}{Test}.",
                candidates.Count,
                string.Join(", ", _appSettings.DeliveryPriceLists.Select(l => l.Name)),
                testIds.Count > 0 ? $" (test mode: {testIds.Count} selected offers)" : string.Empty);

            if (testIds.Count > 0)
            {
                foreach (var missing in testIds.Except(candidates.Select(c => c.Id), StringComparer.OrdinalIgnoreCase))
                    _logger.LogWarning("Test offer {OfferId} is not a candidate (inactive, other price list or no supplier product).", missing);
            }

            var results = new List<OfferBuildResult>();

            // Jeden produkt = jedna oferta InPost (SKU musi być unikalne). Gdy produkt ma kilka ofert
            // na Allegro, zostaje ta już wysłana albo najczęściej oglądana.
            foreach (var group in candidates.GroupBy(c => c.Product.Code.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();

                var ordered = group.OrderByDescending(o => o.InPostOfferId != null).ThenByDescending(o => o.VisitsCount).ToList();

                foreach (var duplicate in ordered.Skip(1))
                    results.Add(new OfferBuildResult { Offer = duplicate }.Skip($"Duplikat produktu {group.Key} - wysyłana jest oferta {ordered[0].Id}."));

                var offer = ordered[0];
                var match = mapper.Map(offer.CategoryId);
                var categoryId = match.InPostCategoryId;

                if (categoryId == null || _catalog.Find(categoryId) is not { Leaf: true })
                {
                    results.Add(new OfferBuildResult { Offer = offer }.Skip(match.Reason ?? $"Kategoria InPost {categoryId} nie istnieje albo nie jest liściem."));
                    continue;
                }

                var attributes = await _catalog.GetAttributesAsync(categoryId, ct);
                results.Add(builder.Build(offer, categoryId, attributes));
            }

            var ready = results.Count(r => !r.IsSkipped);
            _logger.LogInformation("InPost offers built: {Ready} ready, {Skipped} skipped.", ready, results.Count - ready);

            foreach (var reason in results.Where(r => r.IsSkipped).GroupBy(r => ReasonGroup(r.SkipReason!)).OrderByDescending(g => g.Count()))
                _logger.LogInformation(" - skipped {Count}: {Reason}", reason.Count(), reason.Key);

            return results;
        }

        // ------------------------------------------------------------------ wysyłka

        /// <summary>
        /// Pełny cykl: budowa ofert, a poza trybem próbnym - odświeżenie statusów, założenie nowych
        /// ofert (z limitem na cykl), aktualizacja ceny i stanu oraz zamknięcie ofert, których
        /// już nie sprzedajemy.
        /// </summary>
        public async Task SyncAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_credentials.OrganizationId))
                throw new InvalidOperationException("Brak 'InPostApiCredentials:OrganizationId'.");

            var results = await BuildOffersAsync(ct);

            if (_appSettings.DryRun)
            {
                _logger.LogInformation("Dry run - nothing was sent to InPost. Set AppSettings:DryRun=false to publish.");
                return;
            }

            var tracked = (await _repository.GetTrackedAsync(Account, ct))
                .ToDictionary(t => t.AllegroOfferId, StringComparer.OrdinalIgnoreCase);

            await ResolvePendingCommandsAsync(tracked.Values, ct);

            await RefreshStatusesAsync(tracked.Values.Where(t => t.InPostOfferId != null && t.Status is null or "CREATING" or "PENDING"), ct);

            tracked = (await _repository.GetTrackedAsync(Account, ct))
                .ToDictionary(t => t.AllegroOfferId, StringComparer.OrdinalIgnoreCase);

            var ready = results.Where(r => !r.IsSkipped).ToList();

            if (await ReassignChangedAllegroOffersAsync(ready, tracked, ct))
            {
                tracked = (await _repository.GetTrackedAsync(Account, ct))
                    .ToDictionary(t => t.AllegroOfferId, StringComparer.OrdinalIgnoreCase);
            }

            // Oferta, której komenda utworzenia się nie powiodła (FAILED), idzie jeszcze raz.
            await CreateOffersAsync(ready
                .Where(r => !tracked.TryGetValue(r.Offer.Id, out var t) || t.InPostOfferId == null || t.Status == "FAILED")
                .ToList(), ct);

            var live = ready
                .Where(r => tracked.TryGetValue(r.Offer.Id, out var t) && t.InPostOfferId != null && t.Status != "FAILED")
                .Select(r => (Result: r, Tracked: tracked[r.Offer.Id]))
                .ToList();

            // Treść (opis, atrybuty, zdjęcia, wymiary...) aktualizujemy, gdy zmienił się jej skrót, a ofertę
            // z błędami walidacji (PENDING) - w każdym cyklu, bo poprawka mogła przyjść z danych dostawcy.
            var patched = await UpdateContentAsync(live
                .Where(x => x.Tracked.Status is "PENDING"
                    || (x.Tracked.Status is "PUBLISHED" or "SOLDOUT" or "CLOSED" && x.Tracked.ContentHash != x.Result.ContentHash))
                .OrderBy(x => x.Tracked.Status == "PENDING" ? 0 : 1)
                .ToList(), ct);

            var notPatched = live.Where(x => !patched.Contains(x.Result.Offer.Id)).ToList();

            await ReopenOffersAsync(live.Where(x => x.Tracked.Status == "CLOSED").ToList(), ct);
            await UpdatePricesAsync(notPatched
                .Where(x => x.Tracked.Status is "PUBLISHED" or "SOLDOUT"
                    && x.Tracked.Price != x.Result.Proposal!.Price.GrossPrice.Amount
                    && x.Tracked.PendingPrice != x.Result.Proposal!.Price.GrossPrice.Amount)
                .ToList(), ct);

            await UpdateStocksAsync(notPatched
                .Where(x => x.Tracked.Status is "PUBLISHED" or "SOLDOUT"
                    && x.Tracked.Stock != x.Result.Proposal!.Stock.Quantity
                    && x.Tracked.PendingStock != x.Result.Proposal!.Stock.Quantity)
                .ToList(), ct);

            // Zamykamy tylko przy pełnym przebiegu: w trybie testowym kandydatów jest kilku
            // i wszystko inne wyglądałoby na ofertę do zamknięcia.
            if (_appSettings.TestOfferIds.Count == 0 && results.Count > 0)
            {
                var readyIds = ready.Select(r => r.Offer.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var open = tracked.Values
                    .Where(t => t.InPostOfferId != null && t.Status is "PUBLISHED" or "SOLDOUT" or "PENDING")
                    .ToList();

                var toClose = open.Where(t => !readyIds.Contains(t.AllegroOfferId)).ToList();

                if (IsMassClose(toClose.Count, open.Count))
                {
                    _logger.LogError(
                        "InPost: {ToClose} of {Open} offers would be closed in this cycle - more than {Percent}%, so nothing was closed. " +
                        "Check supplier stock, price lists and category mapping; raise AppSettings:MaxClosePercentPerCycle if this is intended.",
                        toClose.Count, open.Count, _appSettings.MaxClosePercentPerCycle);
                }
                else
                {
                    await CloseOffersAsync(toClose, ct);
                }
            }
        }

        private async Task CreateOffersAsync(List<OfferBuildResult> toCreate, CancellationToken ct)
        {
            var limit = _appSettings.MaxNewOffersPerCycle > 0 ? _appSettings.MaxNewOffersPerCycle : int.MaxValue;
            int created = 0, failed = 0;

            foreach (var result in toCreate.Take(limit))
            {
                ct.ThrowIfCancellationRequested();

                var proposal = result.Proposal!;

                try
                {
                    var response = await _client.PostAsync<OfferCreated>(_client.Org("offers"), proposal, ct);

                    await _repository.UpsertAsync(Account, result.Offer.Id, proposal.ExternalId!,
                        inPostOfferId: response.OfferId, lastCommandId: response.CommandId, status: "CREATING",
                        price: proposal.Price.GrossPrice.Amount, stock: proposal.Stock.Quantity,
                        validationErrors: string.Empty, lastError: string.Empty, contentHash: result.ContentHash, ct: ct);

                    created++;
                    _logger.LogInformation("InPost offer created for {Code} ({AllegroId}): {OfferId}.", proposal.ExternalId, result.Offer.Id, response.OfferId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    await _repository.UpsertAsync(Account, result.Offer.Id, proposal.ExternalId!, lastError: Describe(ex), ct: ct);
                    _logger.LogError("Failed to create InPost offer {Code}: {Error}", proposal.ExternalId, Describe(ex));
                }
            }

            _logger.LogInformation("InPost offers created: {Created}, failed: {Failed}, waiting for next cycles: {Waiting}.",
                created, failed, Math.Max(0, toCreate.Count - limit));
        }

        /// <summary>
        /// Oferta po POST powstaje asynchronicznie. Sprawdzamy jej status i błędy walidacji -
        /// a gdy oferty jeszcze nie ma, status komendy (z powodem niepowodzenia).
        /// </summary>
        private async Task RefreshStatusesAsync(IEnumerable<TrackedInPostOffer> offers, CancellationToken ct)
        {
            foreach (var offer in offers)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var details = await _client.GetAsync<OfferDetails>(_client.Org($"offers/{offer.InPostOfferId}"), ct);
                    var errors = FormatValidationErrors(details);

                    await _repository.UpsertAsync(Account, offer.AllegroOfferId, offer.ExternalId,
                        status: details.Offer?.Status, validationErrors: errors ?? string.Empty, ct: ct);

                    if (errors != null)
                        _logger.LogWarning("InPost offer {Code} is {Status}: {Errors}", offer.ExternalId, details.Offer?.Status, errors);
                }
                catch (InPostApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound && offer.LastCommandId != null)
                {
                    var command = await _client.GetAsync<CommandDetails>(_client.Org($"offers/commands/{offer.LastCommandId}"), ct);

                    if (command.Status == "FAILURE")
                    {
                        var errors = string.Join("; ", (command.Errors ?? new List<CommandError>()).Select(e => $"{e.FieldName}: {e.Message}"));

                        // Oferty nie ma - czyścimy status, żeby w kolejnym cyklu założyć ją ponownie.
                        await _repository.UpsertAsync(Account, offer.AllegroOfferId, offer.ExternalId,
                            status: "FAILED", lastError: errors, ct: ct);
                        _logger.LogError("InPost offer {Code} creation failed: {Errors}", offer.ExternalId, errors);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Failed to refresh InPost offer {Code}: {Error}", offer.ExternalId, Describe(ex));
                }
            }
        }

        /// <summary>
        /// PATCH treści oferty razem z ceną i stanem (JSON Merge Patch). Odpowiedź od razu zawiera ofertę
        /// po zmianie - zapisujemy jej status i ewentualne błędy walidacji. InPost nie zaleca dużych zmian
        /// w opublikowanych ofertach; jeśli zmiana nie przejdzie walidacji, oferta wróci do PENDING
        /// z opisem błędu, a my spróbujemy ponownie w kolejnym cyklu.
        /// </summary>
        /// <returns>Id ofert Allegro, które zostały zaktualizowane (z ceną i stanem).</returns>
        private async Task<HashSet<string>> UpdateContentAsync(List<(OfferBuildResult Result, TrackedInPostOffer Tracked)> offers, CancellationToken ct)
        {
            var patched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var limit = _appSettings.MaxContentUpdatesPerCycle > 0 ? _appSettings.MaxContentUpdatesPerCycle : int.MaxValue;
            var failed = 0;

            foreach (var (result, tracked) in offers.Take(limit))
            {
                ct.ThrowIfCancellationRequested();

                var p = result.Proposal!;

                try
                {
                    var details = await _client.MergePatchAsync<OfferDetails>(_client.Org($"offers/{tracked.InPostOfferId}"), new
                    {
                        product = p.Product,
                        stock = p.Stock,
                        price = p.Price,
                        gpsr = p.Gpsr,
                        shippingTime = p.ShippingTime,
                        images = p.Images,
                        postSale = p.PostSale
                    }, ct);

                    var errors = FormatValidationErrors(details);

                    await _repository.UpsertAsync(Account, result.Offer.Id, tracked.ExternalId,
                        status: details.Offer?.Status, price: p.Price.GrossPrice.Amount, stock: p.Stock.Quantity,
                        validationErrors: errors ?? string.Empty, lastError: string.Empty,
                        contentHash: result.ContentHash, clearPending: true, ct: ct);

                    patched.Add(result.Offer.Id);

                    if (errors != null)
                        _logger.LogWarning("InPost offer {Code} updated but is {Status}: {Errors}", tracked.ExternalId, details.Offer?.Status, errors);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    await _repository.UpsertAsync(Account, result.Offer.Id, tracked.ExternalId, lastError: Describe(ex), ct: ct);
                    _logger.LogError("Failed to update InPost offer {Code}: {Error}", tracked.ExternalId, Describe(ex));
                }
            }

            _logger.LogInformation("InPost offer content updated: {Updated}, failed: {Failed}, waiting for next cycles: {Waiting}.",
                patched.Count, failed, Math.Max(0, offers.Count - limit));

            return patched;
        }

        private Task UpdatePricesAsync(List<(OfferBuildResult Result, TrackedInPostOffer Tracked)> offers, CancellationToken ct) =>
            SendBatchAsync(offers, "price", "offers/prices",
                x => new OfferPricePatchItem { OfferId = x.Tracked.InPostOfferId!.Value, Price = x.Result.Proposal!.Price.GrossPrice },
                x => x.Result.Proposal!.Price.GrossPrice.Amount,
                ct);

        private Task UpdateStocksAsync(List<(OfferBuildResult Result, TrackedInPostOffer Tracked)> offers, CancellationToken ct) =>
            SendBatchAsync(offers, "stock", "offers/stocks",
                x => new OfferStockPatchItem { OfferId = x.Tracked.InPostOfferId!.Value, Stock = x.Result.Proposal!.Stock },
                x => x.Result.Proposal!.Stock.Quantity,
                ct);

        /// <summary>
        /// Zbiorcza zmiana ceny / stanu. InPost zwraca komendę dla każdej oferty: SUCCESS zatwierdzamy
        /// od razu, FAILURE zapisujemy jako błąd (stara wartość zostaje, więc zmiana wróci w kolejnym
        /// cyklu), a PENDING zapisujemy jako wartość "w drodze" i sprawdzamy w następnym cyklu.
        /// </summary>
        private async Task SendBatchAsync<TItem>(
            List<(OfferBuildResult Result, TrackedInPostOffer Tracked)> offers,
            string kind,
            string endpoint,
            Func<(OfferBuildResult Result, TrackedInPostOffer Tracked), TItem> toItem,
            Func<(OfferBuildResult Result, TrackedInPostOffer Tracked), decimal> value,
            CancellationToken ct)
        {
            int confirmed = 0, pending = 0, failed = 0;

            foreach (var chunk in offers.Chunk(BatchSize))
            {
                try
                {
                    var commands = await _client.PatchAsync<List<CommandDetails>>(_client.Org(endpoint), chunk.Select(toItem).ToList(), ct);
                    var byOffer = commands.Where(c => c.OfferId != null).GroupBy(c => c.OfferId!.Value).ToDictionary(g => g.Key, g => g.First());

                    foreach (var x in chunk)
                    {
                        // Brak komendy dla oferty = nie wiemy, czy zmiana przeszła; różnica wróci w kolejnym cyklu.
                        if (!byOffer.TryGetValue(x.Tracked.InPostOfferId!.Value, out var command))
                            continue;

                        switch (command.Status)
                        {
                            case "SUCCESS":
                                confirmed++;
                                await _repository.UpsertAsync(Account, x.Result.Offer.Id, x.Tracked.ExternalId,
                                    price: kind == "price" ? value(x) : null,
                                    stock: kind == "stock" ? (int)value(x) : null,
                                    ct: ct);
                                break;

                            case "FAILURE":
                                failed++;
                                var error = $"{kind}: " + string.Join("; ", (command.Errors ?? new List<CommandError>()).Select(e => e.Message));
                                await _repository.UpsertAsync(Account, x.Result.Offer.Id, x.Tracked.ExternalId, lastError: error, ct: ct);
                                _logger.LogError("InPost rejected {Kind} update of {Code}: {Error}", kind, x.Tracked.ExternalId, error);
                                break;

                            default:
                                pending++;
                                await _repository.SetPendingAsync(x.Result.Offer.Id, kind, value(x), command.CommandId, ct);
                                break;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Failed to update InPost {Kind}: {Error}", kind, Describe(ex));
                }
            }

            _logger.LogInformation("InPost {Kind} updates: {Total} sent, {Confirmed} confirmed, {Pending} pending, {Failed} rejected.",
                kind, offers.Count, confirmed, pending, failed);
        }

        /// <summary>
        /// Wyniki komend ceny i stanu wysłanych w poprzednich cyklach. InPost trzyma komendy przez 24 h;
        /// komenda, której już nie ma, jest traktowana jak nieudana - stara wartość zostaje i różnica
        /// wróci do wysłania.
        /// </summary>
        private async Task ResolvePendingCommandsAsync(IEnumerable<TrackedInPostOffer> offers, CancellationToken ct)
        {
            foreach (var offer in offers)
            {
                foreach (var (kind, commandId) in new[] { ("price", offer.PriceCommandId), ("stock", offer.StockCommandId) })
                {
                    if (commandId == null)
                        continue;

                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var command = await _client.GetAsync<CommandDetails>(_client.Org($"offers/commands/{commandId}"), ct);

                        if (command.Status == "SUCCESS")
                        {
                            await _repository.ResolvePendingAsync(offer.AllegroOfferId, kind, commandId.Value, success: true, error: null, ct);
                        }
                        else if (command.Status == "FAILURE")
                        {
                            var error = $"{kind}: " + string.Join("; ", (command.Errors ?? new List<CommandError>()).Select(e => e.Message));
                            await _repository.ResolvePendingAsync(offer.AllegroOfferId, kind, commandId.Value, success: false, error, ct);
                            _logger.LogError("InPost rejected {Kind} update of {Code}: {Error}", kind, offer.ExternalId, error);
                        }
                    }
                    catch (InPostApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        await _repository.ResolvePendingAsync(offer.AllegroOfferId, kind, commandId.Value, success: false,
                            $"{kind}: komenda {commandId} wygasła bez wyniku - zmiana zostanie wysłana ponownie.", ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError("Failed to check InPost {Kind} command of {Code}: {Error}", kind, offer.ExternalId, Describe(ex));
                    }
                }
            }
        }

        /// <summary>
        /// Gdy oferta Allegro, za którą stała oferta InPost, przestała być kandydatem (np. zakończona),
        /// a ten sam produkt ma inną aktywną ofertę Allegro - przepinamy ofertę InPost na nową, zamiast
        /// zakładać drugą z tym samym SKU i zamykać starą.
        /// </summary>
        /// <returns>Czy coś zostało przepięte (trzeba wtedy przeładować stan).</returns>
        private async Task<bool> ReassignChangedAllegroOffersAsync(
            List<OfferBuildResult> ready,
            Dictionary<string, TrackedInPostOffer> tracked,
            CancellationToken ct)
        {
            var readyIds = ready.Select(r => r.Offer.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphansByCode = tracked.Values
                .Where(t => t.InPostOfferId != null && t.Status != "FAILED" && !readyIds.Contains(t.AllegroOfferId))
                .GroupBy(t => t.ExternalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var reassigned = 0;

            foreach (var result in ready)
            {
                if (tracked.TryGetValue(result.Offer.Id, out var own) && own.InPostOfferId != null)
                    continue;

                if (!orphansByCode.Remove(result.Proposal!.ExternalId!, out var orphan))
                    continue;

                await _repository.ReassignAsync(orphan.AllegroOfferId, result.Offer.Id, ct);
                reassigned++;

                _logger.LogInformation("InPost offer {Code} moved from Allegro offer {From} to {To}.",
                    orphan.ExternalId, orphan.AllegroOfferId, result.Offer.Id);
            }

            return reassigned > 0;
        }

        /// <summary>Czy zamknięcie tylu ofert to podejrzanie dużo (bezpiecznik z konfiguracji).</summary>
        internal static bool IsMassClose(int toClose, int open, decimal maxPercent) =>
            maxPercent > 0 && open > 0 && toClose > Math.Max(10, open * maxPercent / 100m);

        private bool IsMassClose(int toClose, int open) => IsMassClose(toClose, open, _appSettings.MaxClosePercentPerCycle);

        private async Task CloseOffersAsync(List<TrackedInPostOffer> offers, CancellationToken ct)
        {
            foreach (var offer in offers)
            {
                try
                {
                    await _client.PostAsync<CommandDetails>(_client.Org($"offers/{offer.InPostOfferId}/close"), null, ct);
                    await _repository.UpsertAsync(Account, offer.AllegroOfferId, offer.ExternalId, status: "CLOSED", ct: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Failed to close InPost offer {Code}: {Error}", offer.ExternalId, Describe(ex));
                }
            }

            _logger.LogInformation("InPost offers closed: {Count}.", offers.Count);
        }

        private async Task ReopenOffersAsync(List<(OfferBuildResult Result, TrackedInPostOffer Tracked)> offers, CancellationToken ct)
        {
            foreach (var (result, tracked) in offers)
            {
                try
                {
                    await _client.PostAsync<CommandDetails>(_client.Org($"offers/{tracked.InPostOfferId}/reopen"), null, ct);
                    await _repository.UpsertAsync(Account, result.Offer.Id, tracked.ExternalId, status: "PENDING", ct: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Failed to reopen InPost offer {Code}: {Error}", tracked.ExternalId, Describe(ex));
                }
            }
        }

        private void ValidateCategoryMap()
        {
            foreach (var (allegroId, inPostId) in _offerSettings.CategoryMap)
            {
                var category = _catalog.Resolve(inPostId);

                if (category == null)
                    _logger.LogError("CategoryMap: Allegro {AllegroId} -> InPost {InPostId} does not exist in InPost category tree.", allegroId, inPostId);
                else if (!category.Leaf)
                    _logger.LogError("CategoryMap: InPost category {Path} is not a leaf - offers cannot be listed there.", _catalog.PathOf(category.Id));
            }
        }

        private static string ReasonGroup(string reason)
        {
            var cut = reason.IndexOfAny(new[] { ':', '(' });
            var group = cut > 0 ? reason[..cut] : reason;

            return group.StartsWith("Brak nazwy kategorii", StringComparison.Ordinal) ? "Brak ścieżki kategorii Allegro w bazie"
                : group.StartsWith("Brak odpowiednika", StringComparison.Ordinal) ? "Brak odpowiednika kategorii w InPost"
                : group.StartsWith("Kategoria Allegro", StringComparison.Ordinal) ? "Kategoria InPost nie jest liściem"
                : group.StartsWith("Stan u dostawcy", StringComparison.Ordinal) ? "Stan u dostawcy poniżej minimum"
                : group.StartsWith("Duplikat", StringComparison.Ordinal) ? "Duplikat produktu"
                : group.Trim();
        }

        private static string? FormatValidationErrors(OfferDetails details) =>
            details.Metadata?.ValidationErrors is { Count: > 0 } list
                ? string.Join("; ", list.Select(e => $"{e.ValidationCode}: {e.ValidationMessage}"))
                : null;

        private static string Describe(Exception ex) => ex is InPostApiException api ? api.Describe() : ex.Message;
    }
}
